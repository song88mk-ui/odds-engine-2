
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using OE.Job.Cleanup.Telemetry;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace OE.Job.Cleanup.Repository
{
    public class SportsbookRepository : ISportsbookRepository
    {

        private readonly ILogger<SportsbookRepository> _logger;
        private readonly IConfiguration _configuration;
        private readonly IBetFixtureCache _betFixtureCache;

        private int _sportRetention;

        // the orphan sweep walks every market and selection, so it is throttled to once per
        // Custom:OrphanSweepIntervalHours instead of running on all ~276 dispatches a prd day
        private DateTime _lastOrphanSweepUtc = DateTime.MinValue;

        private IMongoCollection<Fixture> _fixtureCollection;
        private IMongoCollection<Market> _marketCollection;
        private IMongoCollection<Selection> _selectionCollection;

        //private IMongoCollection<FixtureMapping> _fixtureMappingCollection;
        //private IMongoCollection<MarketMapping> _marketMappingCollection;
        //private IMongoCollection<SelectionMapping> _selectionMappingCollection;

        private readonly Counter<long> _jobRunFixturesDeleted;
        private readonly Counter<long> _jobRunOrphansDeleted;

        public SportsbookRepository(IConfiguration configuration, ILogger<SportsbookRepository> logger, IBetFixtureCache betFixtureCache, Instrumentation instrumentation)
        {
            _configuration = configuration;
            _logger = logger;
            _betFixtureCache = betFixtureCache;
            _jobRunFixturesDeleted = instrumentation.JobRunFixturesDeleted;
            _jobRunOrphansDeleted = instrumentation.JobRunOrphansDeleted;

            _sportRetention = _configuration.GetValue<int>("Custom:SportRetention", -30);

            var mongoClient = new MongoClient(_configuration["Custom:MongoDB-Sportsbook"]);
            var db = mongoClient.GetDatabase("Sportsbook");

            _fixtureCollection = db.GetCollection<Fixture>("Fixture");
            _marketCollection = db.GetCollection<Market>("Market");
            _selectionCollection = db.GetCollection<Selection>("Selection");

            // _fixtureMappingCollection
            // _marketMappingCollection
            // _selectionMappingCollection
        }

        public async Task Cleanup(CancellationToken cancellationToken)
        {
            // fixtures with bet placements must be retained for bet history; without the
            // preloaded set every fixture would look bet-free and could be deleted
            if (!_betFixtureCache.IsLoaded)
                throw new InvalidOperationException("Bet placement preload has not completed; refusing to clean up.");

            // all fixtures:
            // type: match
            // status: ended, closed, cancelled, abandoned, unknown, postponed, interrupted etc
            // non virtual sports
            // started 30 days ago (retentionValue)
            var statusToBeDeleted = new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 99 }; // > 2 will include MatchABoutToStart, so we exclude it?

            var batchSize = _configuration.GetValue<int>("Custom:BatchSize", 5000);
            var batchDelayMs = _configuration.GetValue<int>("Custom:BatchDelayMs", 2000);
            var startHour = _configuration.GetValue<uint>("Custom:UtcWindowHours:Start", 1);
            var endHour = _configuration.GetValue<uint>("Custom:UtcWindowHours:End", 10);

            var fixtureStatusFilter = Builders<Fixture>.Filter.In(f => f.FiStId, statusToBeDeleted);
            var fixtureTypeFilter = Builders<Fixture>.Filter.Eq(f => f.FiTyId, 0);
            var fixtureDateFilter = Builders<Fixture>.Filter.Lt(f => f.FiStDa, DateTime.UtcNow.AddDays(_sportRetention));

            var fixtureFilter = fixtureStatusFilter & fixtureTypeFilter & fixtureDateFilter;

            var fixtureProjection = Builders<Fixture>.Projection
                .Include(c => c.Id)
                .Include(c => c.FiId)
                .Include(c => c.FiSp)
                .Include(c => c.SpId)
                .Include(c => c.FiStDa);

            _logger.LogDebug("Retrieving list of fixtures");

            var fixtures = await _fixtureCollection.Find(fixtureFilter)
                .Project<FixtureProjection>(fixtureProjection).ToListAsync(cancellationToken);

            var sortedFixtures = fixtures
                .Where(f => !_betFixtureCache.Contains(f.FiId))
                .OrderBy(o => o.FiStDa).ToList();
            var totalCount = sortedFixtures.Count;

            _logger.LogInformation("Got {Count} fixture record(s) for deletion ({Skipped} retained due to bet placements)",
                    totalCount, fixtures.Count - totalCount);

            for (var offset = 0; offset < sortedFixtures.Count; offset += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // stop when the maintenance window closes; the worker re-dispatches in the next window
                if (!IsWindowOpen(startHour, endHour, DateTime.UtcNow))
                {
                    _logger.LogInformation("UTC window closed, stopping with {Remaining} fixture(s) left for the next run",
                            sortedFixtures.Count - offset);
                    break;
                }

                var batch = sortedFixtures.Skip(offset).Take(batchSize).ToList();
                var fixtureIds = batch.Select(b => b.FiId).ToArray();

                _logger.LogDebug("removing markets for {Count} fixture(s)", batch.Count);
                var marketCollectionFilter = Builders<Market>.Filter.In(c => c.FiId, fixtureIds);
                var marketResult = await _marketCollection.DeleteManyAsync(marketCollectionFilter, cancellationToken);

                _logger.LogDebug("removing selections");
                var selectionCollectionFilter = Builders<Selection>.Filter.In(c => c.FiId, fixtureIds);
                var selectionResult = await _selectionCollection.DeleteManyAsync(selectionCollectionFilter, cancellationToken);

                // mappings collections

                // finally remove the fixtures
                var fixtureCollectionFilter = Builders<Fixture>.Filter.In(c => c.FiId, fixtureIds);
                var fixtureResult = await _fixtureCollection.DeleteManyAsync(fixtureCollectionFilter, cancellationToken);

                if (_jobRunFixturesDeleted.Enabled)
                    _jobRunFixturesDeleted.Add(fixtureResult.DeletedCount);

                _logger.LogInformation("Removed batch: Fixtures={Fixtures} Markets={Markets} Selections={Selections} Remaining={Remaining}",
                        fixtureResult.DeletedCount, marketResult.DeletedCount, selectionResult.DeletedCount,
                        Math.Max(0, sortedFixtures.Count - offset - batch.Count));

                if (offset + batchSize < sortedFixtures.Count)
                    await Task.Delay(batchDelayMs, cancellationToken);
            }

            await CleanupOrphans(batchSize, batchDelayMs, startHour, endHour, cancellationToken);

            _logger.LogDebug("cleanup crew completed the job");
        }

        // The pass above only ever reaches a market or selection through the fixture that owns it,
        // so a child whose fixture document is missing is invisible to it and survives forever.
        // Those orphans are real: OE.Sportsbook.Service upserts markets with IsUpsert = true and
        // never checks that the fixture exists, so a market-change landing after its fixture was
        // deleted, or for a fixture that was never ingested, writes the market on its own. They stay
        // resolvable by the Platform.Sportsbook.API paths that look a market up by MaId alone
        // (betslip validation, selection probabilities, bet-fixture translation), which is how a
        // market that is closed at the vendor can still be quoted as MaStId = Active. Same retention
        // cutoff, same bet-placement protection and same batching as the fixture-driven pass.
        private async Task CleanupOrphans(int batchSize, int batchDelayMs, uint startHour, uint endHour, CancellationToken cancellationToken)
        {
            if (!IsWindowOpen(startHour, endHour, DateTime.UtcNow))
            {
                _logger.LogInformation("UTC window closed, skipping the orphan sweep");
                return;
            }

            // the timestamp is taken up front so a sweep that fails halfway does not re-scan both
            // collections on the next dispatch five minutes later
            var sweepInterval = TimeSpan.FromHours(_configuration.GetValue<double>("Custom:OrphanSweepIntervalHours", 24));
            if (!IsSweepDue(_lastOrphanSweepUtc, DateTime.UtcNow, sweepInterval))
            {
                _logger.LogDebug("Orphan sweep already ran at {Last}, next one is due after {Interval}",
                        _lastOrphanSweepUtc, sweepInterval);
                return;
            }

            _lastOrphanSweepUtc = DateTime.UtcNow;

            var cutoff = DateTime.UtcNow.AddDays(_sportRetention);
            var dryRun = _configuration.GetValue<bool>("Custom:OrphanCleanupDryRun", false);

            _logger.LogDebug("Collecting fixture ids referenced by markets and selections");

            var candidates = new HashSet<long>();
            await CollectFixtureIds(_marketCollection, Builders<Market>.Filter.Empty, candidates, cancellationToken);
            await CollectFixtureIds(_selectionCollection, Builders<Selection>.Filter.Empty, candidates, cancellationToken);

            _logger.LogInformation("Checking {Count} referenced fixture id(s) for orphaned markets and selections",
                    candidates.Count);

            long orphanTotal = 0;
            long marketTotal = 0;
            long selectionTotal = 0;

            foreach (var chunk in candidates.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsWindowOpen(startHour, endHour, DateTime.UtcNow))
                {
                    _logger.LogInformation("UTC window closed, stopping the orphan sweep for the next run");
                    break;
                }

                var existing = new HashSet<long>();
                await CollectFixtureIds(_fixtureCollection, Builders<Fixture>.Filter.In(f => f.FiId, chunk),
                        existing, cancellationToken);

                var orphans = SelectOrphans(chunk, existing, _betFixtureCache);

                if (orphans.Length == 0)
                    continue;

                var marketFilter = OrphanMarketFilter(orphans, cutoff);
                var selectionFilter = OrphanSelectionFilter(orphans, cutoff);

                orphanTotal += orphans.Length;

                if (dryRun)
                {
                    var marketCandidates = await _marketCollection
                        .CountDocumentsAsync(marketFilter, cancellationToken: cancellationToken);
                    var selectionCandidates = await _selectionCollection
                        .CountDocumentsAsync(selectionFilter, cancellationToken: cancellationToken);

                    marketTotal += marketCandidates;
                    selectionTotal += selectionCandidates;

                    _logger.LogInformation("Dry run, orphan batch kept: Fixtures={Fixtures} Markets={Markets} Selections={Selections}",
                            orphans.Length, marketCandidates, selectionCandidates);
                }
                else
                {
                    var marketResult = await _marketCollection.DeleteManyAsync(marketFilter, cancellationToken);
                    var selectionResult = await _selectionCollection.DeleteManyAsync(selectionFilter, cancellationToken);

                    marketTotal += marketResult.DeletedCount;
                    selectionTotal += selectionResult.DeletedCount;

                    if (_jobRunOrphansDeleted.Enabled)
                        _jobRunOrphansDeleted.Add(orphans.Length);

                    _logger.LogInformation("Removed orphan batch: Fixtures={Fixtures} Markets={Markets} Selections={Selections}",
                            orphans.Length, marketResult.DeletedCount, selectionResult.DeletedCount);
                }

                await Task.Delay(batchDelayMs, cancellationToken);
            }

            _logger.LogInformation("Orphan sweep complete ({Mode}): Fixtures={Fixtures} Markets={Markets} Selections={Selections}",
                    dryRun ? "dry run" : "deleted", orphanTotal, marketTotal, selectionTotal);
        }

        // Streams distinct FiId values out of a collection into the given set. The {FiId: 1, _id: 0}
        // projection is served from an index on every collection this is used with - the unfiltered
        // scan of Market and Selection by the (FiId, MaStId, MaTeId) index they both carry, and the
        // per-chunk Fixture lookup by the unique idx_fiid - so it never has to fetch the documents
        // themselves. The retention cutoff is deliberately not applied to the child scans: FiStDa is
        // unindexed there and filtering on it would turn a covered index scan into a full scan.
        private static async Task CollectFixtureIds<T>(IMongoCollection<T> collection, FilterDefinition<T> filter, HashSet<long> fixtureIds, CancellationToken cancellationToken)
        {
            var projection = Builders<T>.Projection.Include("FiId").Exclude("_id");

            using (var cursor = await collection.Find(filter)
                    .Project<BsonDocument>(projection).ToCursorAsync(cancellationToken))
            {
                while (await cursor.MoveNextAsync(cancellationToken))
                {
                    foreach (var document in cursor.Current)
                    {
                        if (TryReadFixtureId(document, out var fixtureId))
                            fixtureIds.Add(fixtureId);
                    }
                }
            }
        }

        // A fixture id is an orphan when the Fixture collection has no document for it. Ids with bet
        // placements are kept regardless: their markets and selections still back bet history, even
        // once the fixture document itself is gone.
        // Internal (not private) so OE.Job.Cleanup.Tests can cover the retention decision without a
        // Mongo connection - same rationale as OE.Sportsbook.Service's ToMarketAndSelectionChange.
        internal static long[] SelectOrphans(IEnumerable<long> fixtureIds, HashSet<long> existingFixtureIds, IBetFixtureCache betFixtureCache)
        {
            return fixtureIds
                .Where(id => !existingFixtureIds.Contains(id) && !betFixtureCache.Contains(id))
                .ToArray();
        }

        // The retention cutoff is re-applied per document: a rescheduled fixture can leave children
        // carrying different FiStDa values, and only the ones past retention are ours to remove. A
        // document with no FiStDa at all is left alone by design - the server-side comparison is
        // type bracketed and never matches a missing field.
        internal static FilterDefinition<Market> OrphanMarketFilter(long[] orphans, DateTime cutoff)
        {
            return Builders<Market>.Filter.In(c => c.FiId, orphans)
                    & Builders<Market>.Filter.Lt(c => c.FiStDa, cutoff);
        }

        internal static FilterDefinition<Selection> OrphanSelectionFilter(long[] orphans, DateTime cutoff)
        {
            return Builders<Selection>.Filter.In(c => c.FiId, orphans)
                    & Builders<Selection>.Filter.Lt(c => c.FiStDa, cutoff);
        }

        // Reads FiId out of a projected document. A document without the field, or with a
        // non-numeric one, is skipped rather than throwing: one malformed document must not abort
        // a scan of the whole collection.
        internal static bool TryReadFixtureId(BsonDocument document, out long fixtureId)
        {
            if (document.TryGetValue("FiId", out var value) && value.IsNumeric)
            {
                fixtureId = value.ToInt64();
                return true;
            }

            fixtureId = 0;
            return false;
        }

        // A never-run sweep (DateTime.MinValue) is always due, so a restarted container sweeps on
        // its first dispatch inside the window.
        internal static bool IsSweepDue(DateTime lastSweepUtc, DateTime utcNow, TimeSpan interval)
        {
            return utcNow - lastSweepUtc >= interval;
        }

        // the worker re-dispatches every 5 minutes, so whatever a run leaves behind when the
        // window closes is picked up by the next run inside the window
        internal static bool IsWindowOpen(uint startHour, uint endHour, DateTime utcNow)
        {
            var hour = utcNow.TimeOfDay.Hours;
            return hour >= startHour && hour < endHour;
        }
    }

    public class FixtureProjection
    {
        [BsonId]
        public string Id { get; set; }
        public long FiId { get; set; }
        public string FiSp { get; set; }
        public int SpId { get; set; }
        public DateTime FiStDa { get; set; }
    }
}

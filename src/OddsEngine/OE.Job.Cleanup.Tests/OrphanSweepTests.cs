using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using OE.Job.Cleanup.Repository;
using OE.Shared.Data.Sportsbook.MongoDB.Collections;

namespace OE.Job.Cleanup.Tests
{
    // Covers the orphan sweep added for GM-842: markets and selections whose fixture document no
    // longer exists are invisible to the fixture-driven pass, which only reaches a child through
    // its parent. These tests pin the decisions that pass makes - which ids count as orphans, what
    // the delete filters ask Mongo for, when the sweep is allowed to run - without a connection.
    public class OrphanSweepTests
    {
        private static readonly DateTime Cutoff = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

        private sealed class FakeBetFixtureCache : IBetFixtureCache
        {
            private readonly HashSet<long> _fixtureIds;

            public FakeBetFixtureCache(params long[] fixtureIds) => _fixtureIds = new HashSet<long>(fixtureIds);

            public bool IsLoaded => true;
            public int Count => _fixtureIds.Count;
            public Task Preload(CancellationToken cancellationToken) => Task.CompletedTask;
            public void Add(long fixtureId) => _fixtureIds.Add(fixtureId);
            public bool Contains(long fixtureId) => _fixtureIds.Contains(fixtureId);
        }

        private static BsonDocument Render<T>(FilterDefinition<T> filter)
        {
            return filter.Render(new RenderArgs<T>(
                BsonSerializer.SerializerRegistry.GetSerializer<T>(),
                BsonSerializer.SerializerRegistry));
        }

        // --- which ids are swept -------------------------------------------------------------

        [Fact]
        public void SelectOrphans_ReturnsIdsWithNoFixtureDocument()
        {
            var orphans = SportsbookRepository.SelectOrphans(
                new long[] { 1479898508708569088, 2 },
                new HashSet<long> { 2 },
                new FakeBetFixtureCache());

            Assert.Equal(new long[] { 1479898508708569088 }, orphans);
        }

        [Fact]
        public void SelectOrphans_KeepsIdsWhoseFixtureStillExists()
        {
            var orphans = SportsbookRepository.SelectOrphans(
                new long[] { 1, 2, 3 },
                new HashSet<long> { 1, 2, 3 },
                new FakeBetFixtureCache());

            Assert.Empty(orphans);
        }

        // The whole point of the bet-placement preload: bet history still reads these markets and
        // selections after the fixture document itself has been purged.
        [Fact]
        public void SelectOrphans_KeepsAMissingFixtureThatHasBetPlacements()
        {
            var orphans = SportsbookRepository.SelectOrphans(
                new long[] { 10, 11 },
                new HashSet<long>(),
                new FakeBetFixtureCache(11));

            Assert.Equal(new long[] { 10 }, orphans);
        }

        [Fact]
        public void SelectOrphans_ReturnsEmptyForAnEmptyChunk()
        {
            var orphans = SportsbookRepository.SelectOrphans(
                Array.Empty<long>(),
                new HashSet<long>(),
                new FakeBetFixtureCache());

            Assert.Empty(orphans);
        }

        // --- what the deletes ask Mongo for --------------------------------------------------

        [Fact]
        public void OrphanMarketFilter_MatchesTheOrphanIdsUnderTheRetentionCutoff()
        {
            var rendered = Render(SportsbookRepository.OrphanMarketFilter(new long[] { 7, 8 }, Cutoff));

            Assert.Equal(new BsonArray { 7L, 8L }, rendered["FiId"]["$in"].AsBsonArray);
            Assert.Equal(Cutoff, rendered["FiStDa"]["$lt"].ToUniversalTime());
        }

        [Fact]
        public void OrphanSelectionFilter_MatchesTheOrphanIdsUnderTheRetentionCutoff()
        {
            var rendered = Render(SportsbookRepository.OrphanSelectionFilter(new long[] { 7, 8 }, Cutoff));

            Assert.Equal(new BsonArray { 7L, 8L }, rendered["FiId"]["$in"].AsBsonArray);
            Assert.Equal(Cutoff, rendered["FiStDa"]["$lt"].ToUniversalTime());
        }

        // FiId leads so the delete rides the (FiId, MaStId, MaTeId) index both child collections
        // carry, rather than scanning on the unindexed FiStDa.
        [Fact]
        public void OrphanMarketFilter_LeadsWithTheIndexedFixtureId()
        {
            var rendered = Render(SportsbookRepository.OrphanMarketFilter(new long[] { 7 }, Cutoff));

            Assert.Equal("FiId", rendered.Names.First());
        }

        // A date-typed $lt is type bracketed server side, so a document with no FiStDa never
        // matches and is deliberately left alone instead of being deleted on an absent field.
        [Fact]
        public void OrphanMarketFilter_ComparesFiStDaAsADateSoAMissingFieldCannotMatch()
        {
            var rendered = Render(SportsbookRepository.OrphanMarketFilter(new long[] { 7 }, Cutoff));

            Assert.Equal(BsonType.DateTime, rendered["FiStDa"]["$lt"].BsonType);
        }

        // --- reading ids out of the projected scan -------------------------------------------

        [Fact]
        public void TryReadFixtureId_ReadsAnInt64Id()
        {
            Assert.True(SportsbookRepository.TryReadFixtureId(
                new BsonDocument { { "FiId", 1479898508708569088L } }, out var fixtureId));
            Assert.Equal(1479898508708569088, fixtureId);
        }

        [Fact]
        public void TryReadFixtureId_WidensAnInt32Id()
        {
            Assert.True(SportsbookRepository.TryReadFixtureId(
                new BsonDocument { { "FiId", 42 } }, out var fixtureId));
            Assert.Equal(42, fixtureId);
        }

        // One malformed document must not abort the scan of a whole collection.
        [Fact]
        public void TryReadFixtureId_SkipsADocumentWithoutTheField()
        {
            Assert.False(SportsbookRepository.TryReadFixtureId(new BsonDocument(), out var fixtureId));
            Assert.Equal(0, fixtureId);
        }

        [Fact]
        public void TryReadFixtureId_SkipsANonNumericId()
        {
            Assert.False(SportsbookRepository.TryReadFixtureId(
                new BsonDocument { { "FiId", "not-a-number" } }, out _));
        }

        // --- when the sweep may run ----------------------------------------------------------

        // A restarted container has no recorded sweep, so it must sweep on its first dispatch.
        [Fact]
        public void IsSweepDue_IsDueWhenItHasNeverRun()
        {
            Assert.True(SportsbookRepository.IsSweepDue(
                DateTime.MinValue, new DateTime(2026, 9, 16, 2, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(24)));
        }

        [Fact]
        public void IsSweepDue_IsNotDueOnTheNextFiveMinuteDispatch()
        {
            var lastSweep = new DateTime(2026, 9, 16, 2, 0, 0, DateTimeKind.Utc);

            Assert.False(SportsbookRepository.IsSweepDue(
                lastSweep, lastSweep.AddMinutes(5), TimeSpan.FromHours(24)));
        }

        [Fact]
        public void IsSweepDue_IsDueOnceTheIntervalHasElapsed()
        {
            var lastSweep = new DateTime(2026, 9, 16, 2, 0, 0, DateTimeKind.Utc);

            Assert.True(SportsbookRepository.IsSweepDue(
                lastSweep, lastSweep.AddHours(24), TimeSpan.FromHours(24)));
        }

        // --- the maintenance window ----------------------------------------------------------

        [Theory]
        [InlineData(1, 10, 0, false)]   // before the window opens
        [InlineData(1, 10, 1, true)]    // start hour is inclusive
        [InlineData(1, 10, 9, true)]
        [InlineData(1, 10, 10, false)]  // end hour is exclusive
        [InlineData(1, 24, 23, true)]   // prd window, last hour of the day
        [InlineData(0, 24, 0, true)]    // stg and dev window, always open
        public void IsWindowOpen_HonorsAnInclusiveStartAndExclusiveEnd(uint start, uint end, int hour, bool expected)
        {
            var utcNow = new DateTime(2026, 9, 16, hour, 30, 0, DateTimeKind.Utc);

            Assert.Equal(expected, SportsbookRepository.IsWindowOpen(start, end, utcNow));
        }
    }
}

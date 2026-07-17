using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Data.Translation;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Translation.Service.Repositories;

namespace OE.Translation.Service.Tests
{
    // Seeding semantics for the flat TranslationCache2 — the single translation store (2026-07
    // translation-flow redesign; the retired SportsbookDynamic/SportsbookGlossary tables are
    // gone). The service owns the SOURCE side and the liveness touch; it must NEVER touch the
    // TARGET_TEXT_* columns, which belong to the external translation pipeline (empty target =
    // the pipeline's worklist).
    public class TranslationRepositoryTests
    {
        #region Setup

        private static (TranslationRepository Repository, ServiceProvider Provider) CreateRepository(
            string databaseName,
            bool cacheSeed = true)
        {
            var services = new ServiceCollection();
            services.AddDbContext<TranslationContext>(options => options.UseInMemoryDatabase(databaseName));
            var provider = services.BuildServiceProvider();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Custom:Translation:CacheSeed"] = cacheSeed.ToString()
                })
                .Build();

            var repository = new TranslationRepository(
                NullLogger<TranslationRepository>.Instance,
                new FakeMemCache(),
                provider.GetRequiredService<IServiceScopeFactory>(),
                configuration);

            return (repository, provider);
        }

        private static TranslationContext GetContext(ServiceProvider provider)
        {
            return provider.CreateScope().ServiceProvider.GetRequiredService<TranslationContext>();
        }

        private static OnFixtureChangeMessage FixtureMessage(
            string homeName = "Arsenal",
            string leagueName = "Premier League")
        {
            return new OnFixtureChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                SpId = 1,
                SpNa = "Soccer",
                SegId = 2,
                SegNa = "England",
                LeId = 3,
                LeNa = leagueName,
                FiNa = "Arsenal vs. Chelsea",
                HoId = "H1",
                HoNa = homeName,
                AwId = "A1",
                AwNa = "Chelsea"
            };
        }

        private static OnMarketChangeMessage MarketMessage(int? compId = null, string seNa = "Arsenal")
        {
            return new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M1",
                        MaTeId = "16",
                        MaTeNa = "Match Result",
                        MaNa = "Match Result",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M1",
                                PrSeId = "S1",
                                MaTeId = "16",
                                SeTeId = "1",
                                SeTeNa = "Home",
                                SeNa = seNa,
                                CompId = compId
                            }
                        }
                    }
                }
            };
        }

        #endregion

        [Fact]
        public async Task FixtureUpdate_SeedsSourceRowsWithEmptyTargets()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_SeedsSourceRowsWithEmptyTargets));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(new[] { "LeId:3", "SegId:2", "SpId:1", "Te:A1", "Te:H1" }, rows.Keys.OrderBy(k => k).ToArray());
            Assert.Equal("Arsenal", rows["Te:H1"].SourceText);
            Assert.Equal("Premier League", rows["LeId:3"].SourceText);

            foreach (var row in rows.Values)
            {
                Assert.Equal("EN", row.SourceLanguage);
                Assert.Equal(string.Empty, row.TargetTextZh);
                Assert.Equal(string.Empty, row.TargetTextEs);
                Assert.Equal(string.Empty, row.TargetTextFr);
                Assert.False(row.HasBetPlacement);
                Assert.False(row.Processed); // fresh rows are on the pipeline's worklist
                Assert.NotNull(row.CreatedAt);
            }
        }

        // Player roster (GM-528): every roster entry seeds a Pl:{PlId} row with the English name
        // as SOURCE_TEXT — deliberately NOT Co:{id}, whose SOURCE_TEXT is a selection's SeNa.
        [Fact]
        public async Task FixtureUpdate_WithRoster_SeedsPlRowsPerPlayer()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_WithRoster_SeedsPlRowsPerPlayer));

            var message = FixtureMessage();
            message.Pl = new List<PlayerMessage>
            {
                new PlayerMessage { PlId = 150762, PlNa = "Sharma, Rohit" },
                new PlayerMessage { PlId = 150763, PlNa = " " } // blank name — skipped by AddIfValid
            };

            await repository.OnProcessFixtureSetUpdate(message);

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "Pl:150762");
            Assert.Equal("Sharma, Rohit", row.SourceText);
            Assert.False(row.Processed);
            Assert.False(context.TranslationCache2.Any(r => r.StringId == "Pl:150763"));
        }

        [Fact]
        public async Task FixtureUpdate_SourceNameChange_ClearsTargetsAndReopensWorklist()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_SourceNameChange_ClearsTargetsAndReopensWorklist));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(homeName: "Arsenal"));

            using (var context = GetContext(provider))
            {
                var row = context.TranslationCache2.Single(r => r.StringId == "Te:H1");
                row.TargetTextZh = "阿森纳"; // the external pipeline translated it in the meantime
                row.Processed = true;
                context.SaveChanges();
            }

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(homeName: "Arsenal FC"));

            using var verify = GetContext(provider);
            var updated = verify.TranslationCache2.Single(r => r.StringId == "Te:H1");

            Assert.Equal("Arsenal FC", updated.SourceText); // source side updated
            Assert.Equal(string.Empty, updated.TargetTextZh); // stale target invalidated
            Assert.False(updated.Processed); // back on the pipeline's worklist
        }

        // 2026-07-14 prod incident ("Odd Or Even Total"): TranslationCache2 rows seeded with a
        // SOURCE_TEXT whose casing differed from the live feed name made every sighting look like
        // a rename. A case-only variant is the same text — align SOURCE_TEXT with the feed but
        // never discard the targets for it (the old behavior would have wiped 266 valid ZH
        // translations the moment CacheSeed was enabled in prod).
        [Fact]
        public async Task FixtureUpdate_SourceCaseOnlyChange_AlignsSourceTextWithoutClearingTargets()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_SourceCaseOnlyChange_AlignsSourceTextWithoutClearingTargets));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(homeName: "Arsenal fc"));

            using (var context = GetContext(provider))
            {
                var row = context.TranslationCache2.Single(r => r.StringId == "Te:H1");
                row.TargetTextZh = "阿森纳"; // the external pipeline translated it in the meantime
                row.Processed = true;
                context.SaveChanges();
            }

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(homeName: "Arsenal FC"));

            using var verify = GetContext(provider);
            var updated = verify.TranslationCache2.Single(r => r.StringId == "Te:H1");

            Assert.Equal("Arsenal FC", updated.SourceText); // re-cased to match the live feed
            Assert.Equal("阿森纳", updated.TargetTextZh);   // translation preserved
            Assert.True(updated.Processed);                 // NOT reopened on the pipeline's worklist
        }

        [Fact]
        public async Task FixtureUpdate_UnchangedSource_LivenessTouchesUpdatedAtWithoutDuplicating()
        {
            var (repository, provider) = CreateRepository(nameof(FixtureUpdate_UnchangedSource_LivenessTouchesUpdatedAtWithoutDuplicating));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using (var context = GetContext(provider))
            {
                // Inserts don't set UPDATED_AT; only re-sightings do.
                Assert.All(context.TranslationCache2, r => Assert.Null(r.UpdatedAt));
            }

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using var verify = GetContext(provider);

            // Still one row per key, but every row got the liveness touch that shields it from
            // OE.Translation.Job's retention sweep.
            Assert.Equal(5, verify.TranslationCache2.Count());
            Assert.All(verify.TranslationCache2, r => Assert.NotNull(r.UpdatedAt));
        }

        [Fact]
        public async Task MarketUpdate_SeedsTemplateAndNamespacedSelectionKeys()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_SeedsTemplateAndNamespacedSelectionKeys));

            await repository.OnProcessMarketSetUpdate(MarketMessage());

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(2, rows.Count);
            Assert.Equal("Match Result", rows["MaTe:16"].SourceText);
            Assert.Equal("Home", rows["SeTe:16:1"].SourceText);
        }

        // Co:/SeTe: are mutually exclusive per selection (2026-07-09) — a competitor-specific
        // selection (CompId != null) seeds Co:{CompId} with the as-is SeNa and never SeTe:.
        [Fact]
        public async Task MarketUpdate_CompetitorSelection_SeedsCoKeyNotSeTe()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_CompetitorSelection_SeedsCoKeyNotSeTe));

            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 810068, seNa: "Schwartzman, DS"));

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(2, rows.Count);
            Assert.Equal("Match Result", rows["MaTe:16"].SourceText);
            Assert.True(rows.ContainsKey("Co:810068"));
            Assert.Equal("Schwartzman, DS", rows["Co:810068"].SourceText);
            Assert.False(rows.ContainsKey("SeTe:16:1"));
        }

        [Fact]
        public async Task MarketUpdate_NonCompetitorSelection_SeedsSeTeKeyNotCo()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_NonCompetitorSelection_SeedsSeTeKeyNotCo));

            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: null));

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.True(rows.ContainsKey("SeTe:16:1"));
            Assert.DoesNotContain(rows.Keys, k => k.StartsWith("Co:"));
        }

        // 2026-07-09 fix: a handicap-market selection whose CompId is one of the fixture's own two
        // participants is skipped here — it's already covered by Te:{HoId}/Te:{AwId} (seeded from
        // OnProcessFixtureSetUpdate's HoNa/AwNa). Seeding Co:{CompId} from SeNa would re-seed a
        // redundant row keyed off text with the handicap line baked in (e.g. "Karpaty Lviv(-0.25)").
        [Fact]
        public async Task MarketUpdate_HandicapSelectionMatchingHomeTeam_SkipsCoKeySeeding()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_HandicapSelectionMatchingHomeTeam_SkipsCoKeySeeding));

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                HoId = "116180",
                AwId = "129527",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M4",
                        MaTeId = "82",
                        MaTeNa = "Asian Handicap",
                        MaNa = "Asian Handicap",
                        IsHa = true,
                        Li = "0.25",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M4",
                                PrSeId = "S5",
                                MaTeId = "82",
                                SeTeId = "1",
                                SeTeNa = "Home",
                                SeNa = "Karpaty Lviv(-0.25)",
                                CompId = 116180
                            }
                        }
                    }
                }
            };

            await repository.OnProcessMarketSetUpdate(message);

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(new[] { "MaTe:82" }, rows.Keys.OrderBy(k => k).ToArray()); // no Co:116180, no SeTe:82:1
        }

        // 2026-07-10 fix: same CompId-matches-HoId shape, but a non-handicap market (e.g. Correct
        // Score, where SeNa fuses the team name with a volatile per-selection score) is now ALSO
        // skipped here, not just handicap markets — reversing the prior "still seeds Co:{CompId}"
        // behavior. Verified against live production data: Co:{CompId}'s cached SOURCE_TEXT can
        // only ever hold ONE selection's fused text at a time, but Correct Score has many selections
        // sharing one CompId (one per scoreline) — so seeding it here just let whichever scoreline
        // was processed last permanently overwrite the row, corrupting every sibling scoreline (and,
        // across fixtures, colliding with unrelated matches sharing the same CompId/SeTeId). The
        // read side (SelectionTranslationKeyResolver) now redirects this shape to Te:{HoId}/{AwId}
        // instead, so no Co:{CompId} row is needed or wanted for it.
        [Fact]
        public async Task MarketUpdate_NonHandicapSelectionMatchingHomeTeam_SkipsCoKeySeeding()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_NonHandicapSelectionMatchingHomeTeam_SkipsCoKeySeeding));

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                HoId = "116180",
                AwId = "129527",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M5",
                        MaTeId = "91",
                        MaTeNa = "Correct Score",
                        MaNa = "Correct Score",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M5",
                                PrSeId = "S6",
                                MaTeId = "91",
                                SeTeId = "99",
                                SeTeNa = "Karpaty Lviv 1-0",
                                SeNa = "Karpaty Lviv 1-0",
                                CompId = 116180
                            }
                        }
                    }
                }
            };

            await repository.OnProcessMarketSetUpdate(message);

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(new[] { "MaTe:91" }, rows.Keys.OrderBy(k => k).ToArray()); // no Co:116180, no SeTe:91:99
        }

        // 2026-07-10 fix: a CompId-null selection whose SeTeNa is actually this fixture's own team
        // name fused with a volatile score (the vendor didn't attach a CompId for this specific
        // Correct Score outcome) is also skipped from SeTe: seeding — already covered by
        // Te:{HoId}/Te:{AwId}. Verified against live SQL data that Correct Score's SeTeId numbering
        // isn't stable across fixtures, so seeding this here let a later, unrelated fixture landing
        // on the same MaTeId/SeTeId inherit this fixture's own club name.
        [Fact]
        public async Task MarketUpdate_NonHandicapCompIdNullSeTeNaMatchesHomeTeam_SkipsSeTeKeySeeding()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_NonHandicapCompIdNullSeTeNaMatchesHomeTeam_SkipsSeTeKeySeeding));

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                HoId = "116180",
                AwId = "129527",
                HoNa = "Karpaty Lviv",
                AwNa = "CFR Cluj",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M5",
                        MaTeId = "91",
                        MaTeNa = "Correct Score",
                        MaNa = "Correct Score",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M5",
                                PrSeId = "S6",
                                MaTeId = "91",
                                SeTeId = "173",
                                SeTeNa = "Karpaty Lviv 1-0",
                                SeNa = "Karpaty Lviv 1-0",
                                CompId = null
                            }
                        }
                    }
                }
            };

            await repository.OnProcessMarketSetUpdate(message);

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            // HoNa/AwNa are seeded as Te:{HoId}/Te:{AwId} via the fixture-level fields on the base
            // OnFixtureChangeMessage — but this test only calls OnProcessMarketSetUpdate, so only
            // MaTe:91 should appear; the point is that no SeTe:91:173 row was seeded.
            Assert.Equal(new[] { "MaTe:91" }, rows.Keys.OrderBy(k => k).ToArray()); // no SeTe:91:173
        }

        // Regression guard (2026-07-10): this method used to have its own hand-duplicated
        // "CompId != null -> Co: else SeTe:" branch with copies of the participant-match and
        // team-name-fused skip guards, but NO digit-only guard at all — confirmed live in
        // production, it kept seeding bare numeric SeTeNa codes (e.g. "29") into SeTe: rows for
        // hours after the shared resolver had already been fixed to reject them, because this
        // seeding path never called the resolver. Now that it routes through
        // SelectionTranslationKeyResolver directly, this must be rejected the same way the
        // resolver-level test (CompIdNullNonDescriptiveNumericSeTeNa_ReturnsEmptyKey) already
        // proves — pinning it here too so the seeding and read sides can never diverge again.
        [Fact]
        public async Task MarketUpdate_CompIdNullNonDescriptiveNumericSeTeNa_SkipsSeTeKeySeeding()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_CompIdNullNonDescriptiveNumericSeTeNa_SkipsSeTeKeySeeding));

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = "1234567890",
                HoId = "H1",
                AwId = "A1",
                HoNa = "Stade Rennais",
                AwNa = "SM Caen",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        ChTyId = 0,
                        PrFiId = "1234567890",
                        PrMaId = "M6",
                        MaTeId = "91",
                        MaTeNa = "Correct Score",
                        MaNa = "Correct Score",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                ChTyId = 0,
                                PrFiId = "1234567890",
                                PrMaId = "M6",
                                PrSeId = "S7",
                                MaTeId = "91",
                                SeTeId = "29",
                                SeTeNa = "29",
                                SeNa = "Stade Rennais 4-0",
                                CompId = null
                            }
                        }
                    }
                }
            };

            await repository.OnProcessMarketSetUpdate(message);

            using var context = GetContext(provider);
            var rows = context.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(new[] { "MaTe:91" }, rows.Keys.OrderBy(k => k).ToArray()); // no SeTe:91:29
        }

        // Regression guard (2026-07-09): a content-reuse-on-rename shortcut was tried and reverted
        // — TARGET_TEXT_*/PROCESSED are the external pipeline's columns only (see
        // Scripts/TranslationCache2.sql). Even when another row already carries an identical
        // SOURCE_TEXT with a real translation, a rename must still clear targets and reset
        // PROCESSED = 0 so the row stays on update.py's worklist — update.py owns same-SOURCE_TEXT
        // fan-out (IX_TC2_SOURCE_TEXT) and is the only thing that may set PROCESSED = 1 /
        // call v1/cachepreload. Writing a target here silently breaks that notification.
        [Fact]
        public async Task MarketUpdate_RenameToTextMatchingAnotherTranslatedRow_StillClearsTargetsAndResetsWorklist()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_RenameToTextMatchingAnotherTranslatedRow_StillClearsTargetsAndResetsWorklist));

            // Seed a second competitor whose name is already fully translated (by the external
            // pipeline, simulated here).
            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 200, seNa: "Player Y"));

            using (var context = GetContext(provider))
            {
                var row = context.TranslationCache2.Single(r => r.StringId == "Co:200");
                row.TargetTextZh = "球员Y";
                row.Processed = true;
                context.SaveChanges();
            }

            // Seed the first competitor under a different name...
            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 100, seNa: "Player X"));

            // ...then rename it to the SAME text as the already-translated competitor.
            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 100, seNa: "Player Y"));

            using var verify = GetContext(provider);
            var renamed = verify.TranslationCache2.Single(r => r.StringId == "Co:100");

            Assert.Equal("Player Y", renamed.SourceText);
            Assert.Equal(string.Empty, renamed.TargetTextZh); // NOT adopted from Co:200
            Assert.False(renamed.Processed); // stays on update.py's worklist
        }

        [Fact]
        public async Task MarketUpdate_NewKeyMatchingExistingTranslatedText_StillInsertsUntranslated()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_NewKeyMatchingExistingTranslatedText_StillInsertsUntranslated));

            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 200, seNa: "Player Y"));

            using (var context = GetContext(provider))
            {
                var row = context.TranslationCache2.Single(r => r.StringId == "Co:200");
                row.TargetTextZh = "球员Y";
                row.Processed = true;
                context.SaveChanges();
            }

            // A brand-new competitor id whose literal name already matches an existing
            // translation — must still insert untranslated so update.py's own fan-out (and its
            // v1/cachepreload notification) is the thing that fills and announces it.
            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 300, seNa: "Player Y"));

            using var verify = GetContext(provider);
            var inserted = verify.TranslationCache2.Single(r => r.StringId == "Co:300");

            Assert.Equal(string.Empty, inserted.TargetTextZh);
            Assert.False(inserted.Processed);
        }

        [Fact]
        public async Task MarketUpdate_RenameToTextWithNoExistingTranslation_ClearsTargetsAsBefore()
        {
            var (repository, provider) = CreateRepository(nameof(MarketUpdate_RenameToTextWithNoExistingTranslation_ClearsTargetsAsBefore));

            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 100, seNa: "Player X"));

            using (var context = GetContext(provider))
            {
                var row = context.TranslationCache2.Single(r => r.StringId == "Co:100");
                row.TargetTextZh = "球员X";
                row.Processed = true;
                context.SaveChanges();
            }

            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 100, seNa: "Player X Renamed"));

            using var verify = GetContext(provider);
            var renamed = verify.TranslationCache2.Single(r => r.StringId == "Co:100");

            Assert.Equal("Player X Renamed", renamed.SourceText);
            Assert.Equal(string.Empty, renamed.TargetTextZh); // no content match — stale target invalidated
            Assert.False(renamed.Processed);
        }

        [Fact]
        public async Task CacheSeedDisabled_WritesNothing()
        {
            var (repository, provider) = CreateRepository(nameof(CacheSeedDisabled_WritesNothing), cacheSeed: false);

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());
            await repository.OnProcessMarketSetUpdate(MarketMessage());

            using var context = GetContext(provider);
            Assert.Empty(context.TranslationCache2);
        }

        [Fact]
        public async Task Seeding_TruncatesSourceTextToColumnLimit()
        {
            var (repository, provider) = CreateRepository(nameof(Seeding_TruncatesSourceTextToColumnLimit));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage(leagueName: new string('x', 400)));

            using var context = GetContext(provider);
            var row = context.TranslationCache2.Single(r => r.StringId == "LeId:3");

            Assert.Equal(250, row.SourceText.Length);
        }

        // SeedActiveSnapshot (Custom:Translation:SeedActiveSnapshot, the one-time startup migration
        // for entities the message-driven path never saw) is deliberately insert-only: it must never
        // touch a row that already exists, even if the live Mongo text has since drifted from it —
        // that reconciliation belongs to the message-driven path (see the rename tests above), not
        // this startup sweep.
        [Fact]
        public async Task SeedActiveSnapshot_NeverTouchesAnExistingRow_EvenOnTextMismatch()
        {
            var (repository, provider) = CreateRepository(nameof(SeedActiveSnapshot_NeverTouchesAnExistingRow_EvenOnTextMismatch));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage()); // seeds LeId:3 = "Premier League"

            using (var context = GetContext(provider))
            {
                var league = context.TranslationCache2.Single(r => r.StringId == "LeId:3");
                league.TargetTextZh = "英超联赛";
                league.Processed = true;
                context.SaveChanges();
            }

            var snapshot = new List<(string id, string value)>
            {
                ("LeId:3", "Premier League (current Mongo wording)"), // exists, text now differs
                ("Te:NEW1", "Newly Promoted FC") // does not exist yet
            };

            await repository.SeedActiveSnapshotAsync(snapshot);

            using var verify = GetContext(provider);
            var rows = verify.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(6, rows.Count); // the original 5 + one new insert

            var league2 = rows["LeId:3"];
            Assert.Equal("Premier League", league2.SourceText); // untouched
            Assert.Equal("英超联赛", league2.TargetTextZh); // untouched
            Assert.True(league2.Processed); // untouched

            var inserted = rows["Te:NEW1"];
            Assert.Equal("Newly Promoted FC", inserted.SourceText);
            Assert.Equal(string.Empty, inserted.TargetTextZh);
            Assert.False(inserted.Processed); // fresh row, on the pipeline's worklist
        }

        [Fact]
        public async Task SeedActiveSnapshot_NothingMissing_WritesNothing()
        {
            var (repository, provider) = CreateRepository(nameof(SeedActiveSnapshot_NothingMissing_WritesNothing));

            await repository.OnProcessFixtureSetUpdate(FixtureMessage());

            using (var context = GetContext(provider))
                Assert.Equal(5, context.TranslationCache2.Count());

            // Every id here was already seeded by the live path above — nothing new to insert.
            await repository.SeedActiveSnapshotAsync(new List<(string id, string value)>
            {
                ("SpId:1", "Soccer"),
                ("Te:H1", "Arsenal")
            });

            using var verify = GetContext(provider);
            Assert.Equal(5, verify.TranslationCache2.Count());
        }

        // Explicit coverage for Co: (competitor-specific selection) keys specifically — the
        // existence check must treat them like any other STRING_ID, not just SpId:/SegId:/Te:.
        [Fact]
        public async Task SeedActiveSnapshot_TreatsCompetitorKeysLikeAnyOtherKey()
        {
            var (repository, provider) = CreateRepository(nameof(SeedActiveSnapshot_TreatsCompetitorKeysLikeAnyOtherKey));

            await repository.OnProcessMarketSetUpdate(MarketMessage(compId: 810068, seNa: "Schwartzman, DS")); // seeds Co:810068

            var snapshot = new List<(string id, string value)>
            {
                ("Co:810068", "Schwartzman, D."), // exists already, text now differs
                ("Co:999999", "Newcomer, N.")      // does not exist yet
            };

            await repository.SeedActiveSnapshotAsync(snapshot);

            using var verify = GetContext(provider);
            var rows = verify.TranslationCache2.ToDictionary(r => r.StringId);

            Assert.Equal(3, rows.Count); // MaTe:16 + the original Co:810068 + the new Co:999999
            Assert.Equal("Schwartzman, DS", rows["Co:810068"].SourceText); // untouched, not overwritten
            Assert.Equal("Newcomer, N.", rows["Co:999999"].SourceText); // inserted
            Assert.False(rows["Co:999999"].Processed);
        }

        [Fact]
        public async Task SeedActiveSnapshot_EmptyOrNullSnapshot_NoOp()
        {
            var (repository, provider) = CreateRepository(nameof(SeedActiveSnapshot_EmptyOrNullSnapshot_NoOp));

            await repository.SeedActiveSnapshotAsync(new List<(string id, string value)>());
            await repository.SeedActiveSnapshotAsync(null);

            using var context = GetContext(provider);
            Assert.Empty(context.TranslationCache2);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Data.Translation;
using OE.Shared.Data.Translation.Entities;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Test;
using OE.Shared.Utilities;

namespace OE.Adapter.Service.Tests
{
    // Covers the adapter's translation enrichment (2026-07 translation-flow redesign): every
    // fixture/market message gets the *Tr lists attached from the in-memory cache (null on miss —
    // consumers fall back to the English *Na source), and ON_TRANSLATION_CHANGE updates the cache
    // then fans out to every OE.Sportsbook.Service instance.
    public class TradingRepositoryTests
    {
        private const string PROVIDER_FIXTURE_ID = "1234567890";

        private readonly FakeMessageBroker _broker = new();
        private readonly TranslationCache _cache;
        private readonly TradingRepository _repository;
        private readonly string _instanceName;

        public TradingRepositoryTests()
        {
            _cache = new TranslationCache(
                NullLogger<TranslationCache>.Instance,
                new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>());

            // Seed via Apply — no database involved.
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "SpId:1", LaCo = "ZH", Tx = "足球" },
                new TranslationChangeItem { StId = "SegId:2", LaCo = "ZH", Tx = "英格兰" },
                new TranslationChangeItem { StId = "LeId:3", LaCo = "ZH", Tx = "英超" },
                new TranslationChangeItem { StId = "Te:H1", LaCo = "ZH", Tx = "阿森纳" },
                new TranslationChangeItem { StId = "Te:A1", LaCo = "ZH", Tx = "切尔西" },
                new TranslationChangeItem { StId = "MaTe:16", LaCo = "ZH", Tx = "赛果" },
                new TranslationChangeItem { StId = "SeTe:16:1", LaCo = "ZH", Tx = "主胜" },
                new TranslationChangeItem { StId = "Pl:150762", LaCo = "ZH", Tx = "罗希特·夏尔马" }
            });

            _repository = new TradingRepository(
                new Helper(),
                NullLogger<TradingRepository>.Instance,
                _cache,
                new Instrumentation());

            _instanceName = new Helper().GetInstanceName(PROVIDER_FIXTURE_ID);
        }

        private static OnFixtureChangeMessage FixtureMessage() => new()
        {
            ChTyId = 0,
            PrFiId = PROVIDER_FIXTURE_ID,
            SpId = 1,
            SegId = 2,
            LeId = 3,
            HoId = "H1",
            AwId = "A1"
        };

        private static OnMarketChangeMessage MarketMessage()
        {
            return new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "H1",
                AwId = "A1",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M1",
                        MaTeId = "16",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage { PrFiId = PROVIDER_FIXTURE_ID, PrMaId = "M1", PrSeId = "S1", MaTeId = "16", SeTeId = "1" },
                            new SelectionMessage { PrFiId = PROVIDER_FIXTURE_ID, PrMaId = "M1", PrSeId = "S2", MaTeId = "16", SeTeId = "404" }
                        }
                    }
                }
            };
        }

        [Fact]
        public void FixtureChange_EnrichesAllTranslationLists_AndPublishesToOwningInstance()
        {
            var message = FixtureMessage();

            _repository.ProcessOnFixtureChangeMessage(_broker, message, "cid");

            Assert.Equal("足球", message.SpTr!.Single().Tx);
            Assert.Equal("英格兰", message.SegTr!.Single().Tx);
            Assert.Equal("英超", message.LeTr!.Single().Tx);
            Assert.Equal("阿森纳", message.HoTr!.Single().Tx);
            Assert.Equal("切尔西", message.AwTr!.Single().Tx);

            var published = Assert.Single(_broker.Published);
            Assert.Equal(MessageType.ON_FIXTURE_CHANGE, published.MessageType);
            Assert.Equal($"r.oe.adapter.onfixturechange.{_instanceName}", published.RoutingKey);
            Assert.Same(message, published.Payload);
        }

        // Player market (GM-528): its MaTeTr is COMPOSED — "{Pl:{CompId} translation} - {MaTe:
        // translation}" — never the raw template text (which would collapse the N per-player
        // markets into one indistinguishable translated block). Either side missing -> null, and
        // the English MaNa (player-inclusive) is the fallback.
        [Fact]
        public void MarketChange_PlayerMarket_ComposesMaTeTrFromPlayerAndTemplate()
        {
            var message = MarketMessage();
            message.Ma[0].CompId = 150762;

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            Assert.Equal("罗希特·夏尔马 - 赛果", message.Ma.Single().MaTeTr!.Single().Tx);
        }

        [Fact]
        public void MarketChange_PlayerMarket_PlaceholderRow_InterpolatesInsteadOfJoining()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "MaTeP:16", LaCo = "ZH", Tx = "{player}的赛果" }
            });
            var message = MarketMessage();
            message.Ma[0].CompId = 150762;

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            Assert.Equal("罗希特·夏尔马的赛果", message.Ma.Single().MaTeTr!.Single().Tx);
        }

        [Fact]
        public void MarketChange_PlayerMarket_UntranslatedPlayer_LeavesMaTeTrNull()
        {
            var message = MarketMessage();
            message.Ma[0].CompId = 999999; // no Pl:999999 row

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            Assert.Null(message.Ma.Single().MaTeTr);
        }

        [Fact]
        public void FixtureChange_CacheMiss_LeavesTranslationListsNull()
        {
            var message = FixtureMessage();
            message.SpId = 404;
            message.HoId = "H404";

            _repository.ProcessOnFixtureChangeMessage(_broker, message, "cid");

            Assert.Null(message.SpTr);
            Assert.Null(message.HoTr);
            Assert.NotNull(message.SegTr); // still enriched where the cache has values
        }

        [Fact]
        public void MarketChange_EnrichesTemplatesPerMarketAndSelection()
        {
            var message = MarketMessage();

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var market = message.Ma.Single();
            Assert.Equal("赛果", market.MaTeTr!.Single().Tx);

            Assert.Equal("主胜", market.Se[0].SeNaTr!.Single().Tx); // SeTe:16:1
            Assert.Null(market.Se[1].SeNaTr);                        // SeTe:16:404 — miss

            var published = Assert.Single(_broker.Published);
            Assert.Equal($"r.oe.adapter.onmarketchange.{_instanceName}", published.RoutingKey);
        }

        // Co:/SeTe: are mutually exclusive lookup keys for the same SeNaTr result (2026-07-09):
        // a competitor-specific selection (CompId != null) is enriched via Co:{CompId}.
        [Fact]
        public void MarketChange_CompetitorSelection_EnrichesSeNaTrViaCoKey()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Co:810068", LaCo = "ZH", Tx = "沙瓦兹曼" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "H1",
                AwId = "A1",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M2",
                        MaTeId = "16",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = PROVIDER_FIXTURE_ID,
                                PrMaId = "M2",
                                PrSeId = "S3",
                                MaTeId = "16",
                                SeTeId = "1",
                                CompId = 810068,
                                SeNa = "Schwartzman, DS"
                            }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selection = message.Ma.Single().Se.Single();
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("沙瓦兹曼", selection.SeNaTr!.Single().Tx);
        }

        // 2026-07-09 fix: a handicap-market selection whose CompId is one of the fixture's own two
        // participants reuses the Te:{HoId}/Te:{AwId} team-name translation instead of a separate
        // Co:{CompId} lookup keyed off SeNa (which has the handicap line baked in, e.g.
        // "Karpaty Lviv(-0.25)" — defeating the cache on every line change, and colliding with the
        // same CompId's other markets). The stale/wrong Co:116180 entry proves it's not consulted.
        [Fact]
        public void MarketChange_HandicapSelectionMatchingHomeTeam_ReusesHoTr()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:116180", LaCo = "ZH", Tx = "利沃夫卡尔帕蒂" },
                new TranslationChangeItem { StId = "Co:116180", LaCo = "ZH", Tx = "WRONG" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "116180",
                AwId = "129527",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M4",
                        MaTeId = "82",
                        IsHa = true,
                        Li = "0.25",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = PROVIDER_FIXTURE_ID,
                                PrMaId = "M4",
                                PrSeId = "S5",
                                MaTeId = "82",
                                SeTeId = "1",
                                CompId = 116180,
                                SeNa = "Karpaty Lviv(-0.25)"
                            }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selection = message.Ma.Single().Se.Single();
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("利沃夫卡尔帕蒂", selection.SeNaTr!.Single().Tx);
        }

        // 2026-07-10 fix: same CompId-matches-HoId shape, but a non-handicap market (e.g. Correct
        // Score, where SeNa fuses the team name with a volatile per-selection score) is now ALSO
        // redirected to Te:{HoId} — reversing the prior "still uses Co:{CompId}" behavior, which let
        // Co:{CompId}'s cached SOURCE_TEXT get overwritten by whichever single scoreline was seeded
        // last, corrupting every sibling scoreline sharing that CompId (verified against live
        // production data). Note this test seeds the cache via Apply() only (no Preload), so
        // GetTranslations has no tracked SOURCE_TEXT to splice a suffix against and trusts the
        // cached row as-is — see
        // NonHandicapSelectionMatchingHomeTeam_UsesTeKeyAndSplicesScoreSuffix below for the
        // realistic, Preload-backed path where the score IS correctly appended.
        [Fact]
        public void MarketChange_NonHandicapSelectionMatchingHomeTeam_UsesTeKey()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:116180", LaCo = "ZH", Tx = "利沃夫卡尔帕蒂" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "116180",
                AwId = "129527",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M5",
                        MaTeId = "91",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = PROVIDER_FIXTURE_ID,
                                PrMaId = "M5",
                                PrSeId = "S6",
                                MaTeId = "91",
                                SeTeId = "99",
                                CompId = 116180,
                                SeNa = "Karpaty Lviv 1-0"
                            }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selection = message.Ma.Single().Se.Single();
            Assert.Equal("利沃夫卡尔帕蒂", selection.SeNaTr!.Single().Tx); // Te:116180 hit, no tracked source to splice against
        }

        // Realistic end-to-end path: Te:116180 preloaded from a SQL-backed row (SOURCE_TEXT tracked,
        // unlike the Apply()-only test above), so GetTranslations' existing splice logic reunites
        // the team's stable translation with the selection's own volatile score suffix — exactly
        // the mechanism that fixes the production bug (a Correct Score selection's translated name
        // was rendering as an unrelated fixture's club instead of "<translated team> <own score>").
        [Fact]
        public void NonHandicapSelectionMatchingHomeTeam_UsesTeKeyAndSplicesScoreSuffix()
        {
            var services = new ServiceCollection();
            services.AddDbContext<TranslationContext>(options =>
                options.UseInMemoryDatabase(nameof(NonHandicapSelectionMatchingHomeTeam_UsesTeKeyAndSplicesScoreSuffix)));
            var provider = services.BuildServiceProvider();

            using (var scope = provider.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<TranslationContext>();
                context.TranslationCache2.Add(new TranslationCache2
                {
                    StringId = "Te:10032",
                    SourceLanguage = "EN",
                    SourceText = "AS Roma",
                    TargetTextZh = "罗马",
                    TargetTextEs = "",
                    TargetTextFr = ""
                });
                context.SaveChanges();
            }

            var cache = new TranslationCache(NullLogger<TranslationCache>.Instance, provider.GetRequiredService<IServiceScopeFactory>());
            cache.Preload();

            var repository = new TradingRepository(new Helper(), NullLogger<TradingRepository>.Instance, cache, new Instrumentation());

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "10032",
                AwId = "10170",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M5",
                        MaTeId = "91",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = PROVIDER_FIXTURE_ID,
                                PrMaId = "M5",
                                PrSeId = "S6",
                                MaTeId = "91",
                                SeTeId = "11",
                                CompId = 10032,
                                SeNa = "AS Roma 1-0"
                            }
                        }
                    }
                }
            };

            repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selection = message.Ma.Single().Se.Single();
            Assert.Equal("罗马 1-0", selection.SeNaTr!.Single().Tx);
        }

        // 2026-07-09 fix: the vendor never attaches a CompId to individual-sport head-to-head
        // markets (tennis/darts/snooker Match Winner, Set/Game Handicap, Frame Winner, ...) — SeNa
        // still carries the real per-fixture name ("Yamalapalli, S(-1.50)"), but SeTeNa is the
        // generic positional template ("Player 1"). Reuses Te:{HoId}/{AwId} instead of the
        // (per-competitor-shared, untranslatable-since-the-vendor-gives-no-CompId) SeTe: key.
        [Fact]
        public void MarketChange_GenericPositionalSelection_Player1ReusesHoTr()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:H1", LaCo = "ZH", Tx = "阿森纳" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "H1",
                AwId = "A1",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M6",
                        MaTeId = "8660",
                        IsHa = true,
                        Li = "-1.5",
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = PROVIDER_FIXTURE_ID,
                                PrMaId = "M6",
                                PrSeId = "S7",
                                MaTeId = "8660",
                                SeTeId = "25",
                                SeTeNa = "Player 1",
                                SeNa = "Yamalapalli, S(-1.50)"
                            }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selection = message.Ma.Single().Se.Single();
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("阿森纳", selection.SeNaTr!.Single().Tx);
        }

        [Fact]
        public void MarketChange_GenericPositionalSelection_Player2ReusesAwTr()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "Te:A1", LaCo = "ZH", Tx = "切尔西" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 1,
                SegId = 2,
                LeId = 3,
                HoId = "H1",
                AwId = "A1",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M7",
                        MaTeId = "6505",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage
                            {
                                PrFiId = PROVIDER_FIXTURE_ID,
                                PrMaId = "M7",
                                PrSeId = "S8",
                                MaTeId = "6505",
                                SeTeId = "26",
                                SeTeNa = "Player 2",
                                SeNa = "Rogers, Anna"
                            }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selection = message.Ma.Single().Se.Single();
            Assert.NotNull(selection.SeNaTr);
            Assert.Equal("切尔西", selection.SeNaTr!.Single().Tx);
        }

        // 2026-07-15 fix (prod fixture 1526096853784064000, "Korona Kielce vs. Stal Rzeszów"):
        // positional token templates ("Home or Draw"/"Home or Away"/"Draw or Away") stay on the
        // shared SeTe:{MaTeId}:{SeTeId} row, but the enriched SeNaTr must swap the translated
        // tokens (主队/客队) for the fixture's own translated team names — EN showed
        // "Korona Kielce or Draw" while ZH showed the generic 主队胜或平局.
        [Fact]
        public void MarketChange_TokenTemplateSelections_ComposeFixtureTeamNamesIntoSeNaTr()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "SeTe:7202:7", LaCo = "ZH", Tx = "主队胜或平局" },
                new TranslationChangeItem { StId = "SeTe:7202:8", LaCo = "ZH", Tx = "主队或客队" },
                new TranslationChangeItem { StId = "SeTe:7202:9", LaCo = "ZH", Tx = "平局或客队" },
                new TranslationChangeItem { StId = "Te:117198", LaCo = "ZH", Tx = "凯尔采" },
                new TranslationChangeItem { StId = "Te:139963", LaCo = "ZH", Tx = "斯塔尔热舒夫" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 10,
                SegId = 2,
                LeId = 682,
                HoId = "117198",
                HoNa = "Korona Kielce",
                AwId = "139963",
                AwNa = "Stal Rzeszów",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "142497214",
                        MaTeId = "7202",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage { PrFiId = PROVIDER_FIXTURE_ID, PrMaId = "142497214", PrSeId = "S1", MaTeId = "7202", SeTeId = "7", SeTeNa = "Home or Draw", SeNa = "Korona Kielce or Draw" },
                            new SelectionMessage { PrFiId = PROVIDER_FIXTURE_ID, PrMaId = "142497214", PrSeId = "S2", MaTeId = "7202", SeTeId = "8", SeTeNa = "Home or Away", SeNa = "Korona Kielce or Stal Rzeszów" },
                            new SelectionMessage { PrFiId = PROVIDER_FIXTURE_ID, PrMaId = "142497214", PrSeId = "S3", MaTeId = "7202", SeTeId = "9", SeTeNa = "Draw or Away", SeNa = "Draw or Stal Rzeszów" }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            var selections = message.Ma.Single().Se;
            Assert.Equal("凯尔采胜或平局", selections[0].SeNaTr!.Single().Tx);
            Assert.Equal("凯尔采或斯塔尔热舒夫", selections[1].SeNaTr!.Single().Tx);
            Assert.Equal("平局或斯塔尔热舒夫", selections[2].SeNaTr!.Single().Tx);
        }

        // Same token template, but the away team has no translation — its token stays generic
        // (the pre-compose text is the floor, never worse).
        [Fact]
        public void MarketChange_TokenTemplateWithUntranslatedTeam_KeepsGenericTokenForThatTeam()
        {
            _cache.Apply(new List<TranslationChangeItem>
            {
                new TranslationChangeItem { StId = "SeTe:7202:8", LaCo = "ZH", Tx = "主队或客队" },
                new TranslationChangeItem { StId = "Te:117198", LaCo = "ZH", Tx = "凯尔采" }
            });

            var message = new OnMarketChangeMessage
            {
                ChTyId = 0,
                PrFiId = PROVIDER_FIXTURE_ID,
                SpId = 10,
                SegId = 2,
                LeId = 682,
                HoId = "117198",
                HoNa = "Korona Kielce",
                AwId = "999999",
                AwNa = "Untranslated FC",
                Ma = new List<MarketMessage>
                {
                    new MarketMessage
                    {
                        PrFiId = PROVIDER_FIXTURE_ID,
                        PrMaId = "M9",
                        MaTeId = "7202",
                        IsHa = false,
                        Se = new List<SelectionMessage>
                        {
                            new SelectionMessage { PrFiId = PROVIDER_FIXTURE_ID, PrMaId = "M9", PrSeId = "S1", MaTeId = "7202", SeTeId = "8", SeTeNa = "Home or Away", SeNa = "Korona Kielce or Untranslated FC" }
                        }
                    }
                }
            };

            _repository.ProcessOnMarketChangeMessage(_broker, message, "cid");

            Assert.Equal("凯尔采或客队", message.Ma.Single().Se.Single().SeNaTr!.Single().Tx);
        }

        // Cache-apply only (2026-07 translation-flow redesign): persistence + notification are
        // OE.Translation.Service's job — the adapter must NOT forward the message anywhere.
        [Fact]
        public void TranslationChange_AppliesToCache_AndPublishesNothing()
        {
            var message = new OnTranslationChangeMessage
            {
                Tr = new List<TranslationChangeItem>
                {
                    new TranslationChangeItem { StId = "SpId:1", LaCo = "ZH", Tx = "足球赛" }
                }
            };

            _repository.ProcessOnTranslationChangeMessage(_broker, message, "cid");

            Assert.Equal("足球赛", _cache.GetTranslations("SpId:1", null)!.Single(t => t.LaCo == "ZH").Tx);
            Assert.Empty(_broker.Published);
        }

        [Fact]
        public void TranslationChange_EmptyPayload_PublishesNothing()
        {
            _repository.ProcessOnTranslationChangeMessage(_broker, new OnTranslationChangeMessage(), "cid");
            _repository.ProcessOnTranslationChangeMessage(_broker, null!, "cid");

            Assert.Empty(_broker.Published);
        }
    }
}

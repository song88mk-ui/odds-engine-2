using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.Repositories;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
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
                new TranslationChangeItem { StId = "SeTe:16:1", LaCo = "ZH", Tx = "主胜" }
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

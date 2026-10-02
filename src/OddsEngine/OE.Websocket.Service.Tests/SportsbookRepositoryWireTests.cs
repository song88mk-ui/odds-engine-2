using Microsoft.Extensions.Logging;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Messages;
using OE.WebSocket.Service.Repository;
using static OE.Websocket.Service.Tests.SportsbookRepositoryInternals;

namespace OE.Websocket.Service.Tests
{
    // These assert what actually leaves the service: the payload handed to the SignalR hub.
    // CLAUDE.md non-negotiable fact 4 makes every property name here a breaking wire change, and
    // the mapping was previously untested end to end because it needs a HubConnection - see
    // RecordingHubConnection for how that is supplied without a hub.
    //
    // Custom:IsOEEnabled picks between two different mappers. Both are exercised: false selects
    // the pass-through mapper (source odds), true selects the OE mapper (per-client repricing,
    // ClCo gating, bet-builder flags).
    public class SportsbookRepositoryWireTests
    {
        private const int SportId = 5;
        private const int SegmentId = 40;
        private const int LeagueId = 300;
        private const long FixtureId = 9001;

        private static OnMarketChangeMessage Message(params MarketMessage[] markets) =>
            new()
            {
                SpId = SportId,
                SpNa = "Football",
                SegId = SegmentId,
                SegNa = "England",
                LeId = LeagueId,
                LeNa = "Premier League",
                FiId = FixtureId,
                PrFiId = "bg-9001",
                FiNa = "Arsenal vs. Chelsea",
                FiStId = (int)FixtureStatus.Prematch,
                FiStNa = "Prematch",
                FiTyId = (int)FixtureType.Match,
                FiTyNa = "Match",
                FiStDa = new DateTime(2026, 9, 10, 18, 0, 0, DateTimeKind.Utc),
                CuPe = 1,
                CuMi = 12,
                CuSe = 34,
                Sc = "0-0",
                IsOfLi = true,
                StUr = "https://stream.test/9001",
                ClCo = "TSO,S88",
                Ma = markets.ToList()
            };

        private static MarketMessage Market(string maTeId, long maId, int soOr, string clCo = "TSO,S88", string clCoMa = "TSO:0.10", params SelectionMessage[] selections) =>
            new()
            {
                MaTeId = maTeId,
                MaId = maId,
                MaNa = $"Market {maTeId}",
                Li = "0",
                MaStId = (int)MarketStatus.Active,
                MaStNa = "Active",
                SoOr = soOr,
                CaStId = 1,
                CaStNa = "Open",
                InPl = true,
                IsHa = false,
                ClCo = clCo,
                ClCoMa = clCoMa,
                Se = selections.ToList()
            };

        private static SelectionMessage Selection(long seId, decimal odds, int soOr, string seTeId = "1") =>
            new() { SeId = seId, Od = odds, IsAc = true, SoOr = soOr, SeTeId = seTeId, SeNa = $"Selection {seId}" };

        private static ClientContext ReadyClient(string code, params string[] mainMarketTemplates)
        {
            var client = BuildClient(code);
            client.MainMarketTemplates.Add(new Tuple<int, List<string>>(SportId, mainMarketTemplates.ToList()));

            return client;
        }

        // ---- Main markets, pass-through mapper (IsOEEnabled=false) ----

        [Fact]
        public async Task MainMarkets_PublishesUnderTheMainMarketMethodName()
        {
            var hub = new RecordingHubConnection();
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(code, "16"), Message(Market("16", 1, 1)), hub);

            Assert.Equal("PublishOddsChangeForMainMarkets", Assert.Single(hub.Sent).Method);
            Assert.Equal(code, hub.ClientCode());
        }

        // The fixture header is the part every client page reads. FiId and CuPe cross from
        // numbers to strings on the way out - a silent type change if it ever regressed.
        [Fact]
        public async Task MainMarkets_MapsTheFixtureHeaderOntoTheWire()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), Message(Market("16", 1, 1)), hub);

            var fixture = hub.Payload<OutgoingOddsChange>().Pa;
            Assert.Equal(SportId, fixture.SpId);
            Assert.Equal("Football", fixture.SpNa);
            Assert.Equal(SegmentId, fixture.SegId);
            Assert.Equal("England", fixture.SegNa);
            Assert.Equal(LeagueId, fixture.LeId);
            Assert.Equal("Premier League", fixture.LeNa);
            Assert.Equal("9001", fixture.FiId);
            Assert.Equal("bg-9001", fixture.PrFiId);
            Assert.Equal("Arsenal vs. Chelsea", fixture.FiNa);
            Assert.Equal("Arsenal", fixture.HoNa);
            Assert.Equal("Chelsea", fixture.AwNa);
            Assert.Equal("1", fixture.CuPe);
            Assert.Equal(12, fixture.CuMi);
            Assert.Equal(34, fixture.CuSe);
            Assert.Equal("0-0", fixture.Sc);
            Assert.True(fixture.IsOfLi);
            Assert.Equal((int)FixtureStatus.Prematch, fixture.FiStId);
            Assert.Equal((int)FixtureType.Match, fixture.FiTyId);
        }

        // Only the client's configured main-market templates are published; everything else on the
        // fixture is dropped for this feed.
        [Fact]
        public async Task MainMarkets_PublishesOnlyTheClientsMainMarketTemplates()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());
            var message = Message(Market("16", 1, 1), Market("77", 2, 2));

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            var market = Assert.Single(hub.Payload<OutgoingOddsChange>().Pa.Ma);
            Assert.Equal("16", market.MaTeId);
            Assert.Equal("1", market.MaId);
        }

        // An outright is not filtered by main-market templates: the whole market list goes out.
        [Fact]
        public async Task MainMarkets_Outright_PublishesEveryMarket()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());
            var message = Message(Market("16", 1, 1), Market("77", 2, 2));
            message.FiTyId = (int)FixtureType.Outright;

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            Assert.Equal(2, hub.Payload<OutgoingOddsChange>().Pa.Ma.Count);
        }

        // Markets and selections are ordered by SoOr, not by arrival order, because the client
        // renders them in list order.
        [Fact]
        public async Task MainMarkets_OrdersMarketsAndSelectionsBySortOrder()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());
            var message = Message(
                Market("16", 1, 9, selections: new[] { Selection(1, 2m, 5), Selection(2, 3m, 1) }),
                Market("18", 2, 2, selections: new[] { Selection(3, 2m, 1) }));

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16", "18"), message, hub);

            var markets = hub.Payload<OutgoingOddsChange>().Pa.Ma;
            Assert.Equal(new[] { "18", "16" }, markets.Select(m => m.MaTeId).ToArray());
            Assert.Equal(new[] { "2", "1" }, markets[1].Se.Select(s => s.SeId).ToArray());
        }

        // The pass-through mapper publishes the source price, formatted to two decimals, with the
        // three alternative formats derived from it.
        [Fact]
        public async Task MainMarkets_PublishesSourceOddsInEveryFormat()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());
            var message = Message(Market("16", 1, 1, selections: new[] { Selection(1, 2.50m, 1), Selection(2, 1.55m, 2) }));

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            var selection = hub.Payload<OutgoingOddsChange>().Pa.Ma[0].Se[0];
            Assert.Equal("2.50", selection.DeOd);
            Assert.Equal("-0.66", selection.MaOd);
            Assert.Equal("1.50", selection.HoOd);
            Assert.Equal("1.50", selection.InOd);
            Assert.Equal("1", selection.SeTeId);
            Assert.Equal("Selection 1", selection.SeNa);
            Assert.True(selection.IsAc);
        }

        // Translation deltas ride along only when present; null keeps the property off the wire so
        // clients keep the names they already have.
        [Fact]
        public async Task MainMarkets_ForwardsTranslationDeltasAndOmitsEmptyOnes()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());
            var message = Message(Market("16", 1, 1));
            message.SpTr = new List<TranslationMessage> { new() { LaCo = "th", Tx = "ฟุตบอล" } };
            message.LeTr = new List<TranslationMessage>();

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            var fixture = hub.Payload<OutgoingOddsChange>().Pa;
            Assert.Equal("th", Assert.Single(fixture.SpTr).LaCo);
            Assert.Null(fixture.LeTr);
            Assert.Null(fixture.SegTr);
        }

        // The fixture sort order and market count come from the client's caches, not the message.
        [Fact]
        public async Task MainMarkets_TakesSortOrderAndMarketCountFromTheClientCaches()
        {
            var hub = new RecordingHubConnection();
            var code = NewClientCode();
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:fso:{SportId}:{SegmentId}:{LeagueId}", new FixtureSortOrder { SortOrder = 3 }, 1);
            memCache.AddOrUpdateCache($"{code}:mc:{FixtureId}", new MarketCount { MarketIds = { 1, 2 } }, 1);

            await BuildRepository(memCache).ProcessOddsChangeForMainMarkets(ReadyClient(code, "16"), Message(Market("16", 1, 1)), hub);

            var fixture = hub.Payload<OutgoingOddsChange>().Pa;
            Assert.Equal(3, fixture.SoOr);
            Assert.Equal(2, fixture.MaCo);
        }

        // ---- Main markets, OE mapper (IsOEEnabled=true) ----

        // The OE mapper publishes only to clients named in the message's client list.
        [Fact]
        public async Task MainMarketsOE_ClientNotOnTheMessage_PublishesNothing()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true);
            var message = Message(Market("16", 1, 1, selections: new[] { Selection(1, 2m, 1) }));
            message.ClCo = "OTHER";

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            Assert.Empty(hub.Sent);
        }

        [Fact]
        public async Task MainMarketsOE_NullClientListOnTheMessage_PublishesNothing()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true);
            var message = Message(Market("16", 1, 1, selections: new[] { Selection(1, 2m, 1) }));
            // ClCo is declared non-nullable but the mapper checks for null at
            // SportsbookRepository.cs:1492, so null is a real runtime case worth covering.
            message.ClCo = null!;

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            Assert.Empty(hub.Sent);
        }

        // The client code is matched in upper case against the message, while the group names use
        // the lower-case code.
        [Fact]
        public async Task MainMarketsOE_MatchesTheClientCodeCaseInsensitively()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true);
            var message = Message(Market("16", 1, 1, clCo: "TSO", clCoMa: "TSO:0.10", selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }));
            message.ClCo = "TSO";

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient("tso", "16"), message, hub);

            Assert.Single(hub.Sent);
            Assert.Contains("endpoint:tso:sports:5:segments:40:fixtures", hub.Payload<OutgoingOddsChange>().Gr);
        }

        // A market not sold to this client is dropped from the OE payload entirely.
        [Fact]
        public async Task MainMarketsOE_MarketNotSoldToTheClient_IsDropped()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true);
            var message = Message(
                Market("16", 1, 1, clCo: "TSO", selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }),
                Market("18", 2, 2, clCo: "S88", selections: new[] { Selection(3, 2m, 1), Selection(4, 2m, 2) }));
            message.ClCo = "TSO";

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient("tso", "16", "18"), message, hub);

            Assert.Equal("16", Assert.Single(hub.Payload<OutgoingOddsChange>().Pa.Ma).MaTeId);
        }

        // The OE mapper reprices. A 2.00/2.00 book is a 100% market, so its own margin is zero;
        // the client asks for 10%, and 2.00 becomes 1.82.
        [Fact]
        public async Task MainMarketsOE_AppliesTheClientMargin()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true);
            var message = Message(Market("16", 1, 1, clCo: "TSO", clCoMa: "TSO:0.10", selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }));
            message.ClCo = "TSO";

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient("tso", "16"), message, hub);

            Assert.Equal("1.82", hub.Payload<OutgoingOddsChange>().Pa.Ma[0].Se[0].DeOd);
        }

        // The bet-builder flag is a per-sport, per-fixture-status lookup against the client's
        // cached templates.
        [Fact]
        public async Task MainMarketsOE_PrematchSportEnabledForBetBuilder_SetsTheFixtureFlag()
        {
            var hub = new RecordingHubConnection();
            var code = "tso";
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:bbmt", new BetBuilderMarketTemplatesDTO
            {
                BetBuilderMarketTemplates = { new BetBuilderMarketTemplateDTO { SportId = SportId, MaTeId = "16", Prematch = 1, Live = 0 } }
            }, 1);

            var message = Message(Market("16", 1, 1, clCo: "TSO", selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }));
            message.ClCo = "TSO";

            await BuildRepository(memCache, isOEEnabled: true).ProcessOddsChangeForMainMarkets(ReadyClient(code, "16"), message, hub);

            Assert.True(hub.Payload<OutgoingOddsChange>().Pa.IsBeBu);
        }

        [Fact]
        public async Task MainMarketsOE_SportEnabledForLiveOnly_LeavesThePrematchFixtureFlagOff()
        {
            var hub = new RecordingHubConnection();
            var code = "tso";
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:bbmt", new BetBuilderMarketTemplatesDTO
            {
                BetBuilderMarketTemplates = { new BetBuilderMarketTemplateDTO { SportId = SportId, MaTeId = "16", Prematch = 0, Live = 1 } }
            }, 1);

            var message = Message(Market("16", 1, 1, clCo: "TSO", selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }));
            message.ClCo = "TSO";

            await BuildRepository(memCache, isOEEnabled: true).ProcessOddsChangeForMainMarkets(ReadyClient(code, "16"), message, hub);

            Assert.False(hub.Payload<OutgoingOddsChange>().Pa.IsBeBu);
        }

        // GM-810: a market with no client-odds configuration (empty/unnamed ClCoMa) used to make
        // GetClientOdds return an empty list, which the per-selection FirstOrDefault dereferenced
        // unchecked - throwing inside the mapper. The public wrapper (ProcessOddsChangeForMainMarkets)
        // caught that exception and logged it, silently dropping the WHOLE fixture update, not just
        // the offending market. Fixed: the market is now published with source odds (the same
        // fallback the OE-disabled pass-through mapper already uses when no per-client repricing
        // applies) and the decision is logged once as an explicit warning, not discovered via a
        // caught exception.
        [Fact]
        public async Task MainMarketsOE_MarketWithNoClientOddsConfiguration_PublishesWithSourceOddsAndLogsOnce()
        {
            var hub = new RecordingHubConnection();
            var logger = new CapturingLogger<SportsbookRepository>();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true, logger: logger);
            var message = Message(Market("16", 1, 1, clCo: "TSO", clCoMa: string.Empty, selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }));
            message.ClCo = "TSO";

            await repository.ProcessOddsChangeForMainMarkets(ReadyClient("tso", "16"), message, hub);

            // The rest of the fixture update still publishes - the missing config for one market no
            // longer discards the whole payload.
            var market = Assert.Single(hub.Payload<OutgoingOddsChange>().Pa.Ma);
            Assert.Equal("2.00", market.Se[0].DeOd);
            Assert.Equal("2.00", market.Se[1].DeOd);

            // The decision was made explicitly and logged exactly once, not surfaced through a
            // caught exception.
            var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
            Assert.Single(warnings);
            Assert.Contains("no client-odds configuration", warnings[0].Message);
            Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
        }

        // Same defect, same fix, on the all-markets path.
        [Fact]
        public async Task AllMarketsOE_MarketWithNoClientOddsConfiguration_PublishesWithSourceOddsAndLogsOnce()
        {
            var hub = new RecordingHubConnection();
            var logger = new CapturingLogger<SportsbookRepository>();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true, logger: logger);
            var message = Message(Market("16", 1, 1, clCo: "TSO", clCoMa: string.Empty, selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }));
            message.ClCo = "TSO";

            await repository.ProcessOddsChangeForAllMarkets(ReadyClient("tso", "16"), message, hub);

            var market = Assert.Single(hub.Payload<OutgoingOddsChange>().Pa.Ma);
            Assert.Equal("2.00", market.Se[0].DeOd);
            Assert.Equal("2.00", market.Se[1].DeOd);

            var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();
            Assert.Single(warnings);
            Assert.Contains("no client-odds configuration", warnings[0].Message);
            Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
        }

        // ---- All markets ----

        [Fact]
        public async Task AllMarkets_PublishesEveryMarketUnderTheAllMarketsMethodName()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());
            var message = Message(Market("16", 1, 1), Market("77", 2, 2));

            await repository.ProcessOddsChangeForAllMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            Assert.Equal("PublishOddsChangeForAllMarkets", Assert.Single(hub.Sent).Method);
            Assert.Equal(2, hub.Payload<OutgoingOddsChange>().Pa.Ma.Count);
        }

        // The all-markets feed is the fixture-details page, so it carries the stream URL that the
        // main-markets feed leaves out.
        [Fact]
        public async Task AllMarkets_CarriesTheStreamUrlAndTheFixtureDetailsGroup()
        {
            var hub = new RecordingHubConnection();
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());

            await repository.ProcessOddsChangeForAllMarkets(ReadyClient(code, "16"), Message(Market("16", 1, 1)), hub);

            var payload = hub.Payload<OutgoingOddsChange>();
            Assert.Equal("https://stream.test/9001", payload.Pa.StUr);
            Assert.Contains($"endpoint:{code}:fixturedetails:9001", payload.Gr);
        }

        [Fact]
        public async Task AllMarketsOE_ClientNotOnTheMessage_PublishesNothing()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache(), isOEEnabled: true);
            var message = Message(Market("16", 1, 1, selections: new[] { Selection(1, 2m, 1) }));
            message.ClCo = "OTHER";

            await repository.ProcessOddsChangeForAllMarkets(ReadyClient(NewClientCode(), "16"), message, hub);

            Assert.Empty(hub.Sent);
        }

        // In the OE all-markets mapper the market sort order comes from the client's cached
        // ordering, replacing the message's own SoOr.
        [Fact]
        public async Task AllMarketsOE_TakesTheMarketOrderFromTheClientCache()
        {
            var hub = new RecordingHubConnection();
            var code = "tso";
            var memCache = new MemCache();
            memCache.AddOrUpdateCache($"{code}:pmso:{SportId}", new ClientMarketSortOrder { MaTeIds = { "18", "16" } }, 1);

            var message = Message(
                Market("16", 1, 1, clCo: "TSO", selections: new[] { Selection(1, 2m, 1), Selection(2, 2m, 2) }),
                Market("18", 2, 2, clCo: "TSO", selections: new[] { Selection(3, 2m, 1), Selection(4, 2m, 2) }));
            message.ClCo = "TSO";

            await BuildRepository(memCache, isOEEnabled: true).ProcessOddsChangeForAllMarkets(ReadyClient(code, "16", "18"), message, hub);

            var markets = hub.Payload<OutgoingOddsChange>().Pa.Ma;
            Assert.Equal(new[] { "18", "16" }, markets.Select(m => m.MaTeId).ToArray());
            Assert.Equal(new[] { 1, 2 }, markets.Select(m => m.SoOr).ToArray());
        }

        // ---- Bet stop ----

        [Fact]
        public async Task BetStopGlobal_PublishesTheFlagWithNoGroups()
        {
            var hub = new RecordingHubConnection();
            var repository = BuildRepository(new MemCache());

            await repository.ProcessBetStopGlobal(BuildClient(NewClientCode()), new OnBetStopGlobalMessage { IsBetStop = true }, hub);

            Assert.Equal("PublishBetStopGlobal", Assert.Single(hub.Sent).Method);
            Assert.True(hub.Payload<OutgoingBetStopGlobal>().IsBeSt);
        }

        [Fact]
        public async Task BetStopFixture_PublishesTheFixtureFlagsAndItsGroups()
        {
            var hub = new RecordingHubConnection();
            var code = NewClientCode();
            var repository = BuildRepository(new MemCache());
            var message = new OnBetStopFixtureMessage
            {
                SpId = SportId,
                SegId = SegmentId,
                LeId = LeagueId,
                FiId = FixtureId,
                FiStId = (int)FixtureStatus.Live,
                FiTyId = (int)FixtureType.Match,
                IsBetStop = true,
                SetActiveMarketsToInactive = true
            };

            await repository.ProcessBetStopFixture(BuildClient(code), message, hub);

            var payload = hub.Payload<OutgoingBetStopFixture>();
            Assert.Equal("PublishBetStopFixture", hub.Sent[0].Method);
            Assert.Equal("9001", payload.Pa.FiId);
            Assert.True(payload.Pa.IsBeSt);
            Assert.True(payload.Pa.SeAcMaToIn);
            Assert.Contains($"endpoint:{code}:sports:5:livefixtures", payload.Gr);
        }
    }
}

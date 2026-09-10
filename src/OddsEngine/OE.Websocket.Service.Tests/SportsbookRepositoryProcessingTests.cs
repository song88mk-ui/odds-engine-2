using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.SignalR.Client;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Messages;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Telemetry;

namespace OE.Websocket.Service.Tests
{
    // Covers SportsbookRepository's cache-backed "Process*" methods (the 11 of 19 that don't push
    // to a SignalR HubConnection — those are covered separately, and only as smoke tests: see the
    // "Hub-push methods" region below for why). Uses the REAL MemCache (matches
    // SportsbookRepositoryStartupLoadTests' convention) with Guid-suffixed client codes per test to
    // avoid colliding with any other test touching the same process-wide cache.
    public class SportsbookRepositoryProcessingTests
    {
        private static IConfiguration BuildConfiguration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Custom:AsianClientCodes", "TSO,S88" },
                    { "Custom:IsOEEnabled", "false" }
                })
                .Build();

        private static SportsbookRepository BuildRepository(MemCache memCache) =>
            new(BuildConfiguration(), memCache, new Instrumentation(), NullLogger<SportsbookRepository>.Instance);

        private static string NewClientCode() => $"t{Guid.NewGuid():N}"[..12];

        private static ClientContext BuildClient(string clientCode, Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHttpMessageHandler handler)
        {
            handler = new FakeHttpMessageHandler(responder);
            return new ClientContext
            {
                ClientId = "11111111-1111-1111-1111-111111111111",
                ClientCode = clientCode,
                HttpClient = new HttpClient(handler) { BaseAddress = new Uri("http://platform-api.test/") }
            };
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body) };

        // ---- ProcessTopFixtures ----

        [Fact]
        public async Task ProcessTopFixtures_CacheMiss_Success_CachesTopFixtureList()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => Json(HttpStatusCode.OK,
                """{"StatusCode":100,"Result":{"Fixtures":[{"FiId":"1"},{"FiId":"2"}]}}"""), out _);
            var repository = BuildRepository(memCache);

            await repository.ProcessTopFixtures(client, new OnMarketChangeMessage { FiTyId = (int)FixtureType.Match, SpId = 1 });

            var cached = memCache.GetCache<TopFixture>($"{clientCode}:TFL");
            Assert.NotNull(cached);
            Assert.Equal(new[] { 1L, 2L }, cached!.FiIds);
        }

        [Fact]
        public async Task ProcessTopFixtures_MarkerCacheAlreadyPresent_SkipsHttpCall()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            memCache.AddOrUpdateCache($"{clientCode}:TF", new TopCache { Id = 0 }, DateTimeOffset.UtcNow.AddMinutes(5));
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("should not call HTTP"), out var handler);
            var repository = BuildRepository(memCache);

            await repository.ProcessTopFixtures(client, new OnMarketChangeMessage { FiTyId = (int)FixtureType.Match, SpId = 1 });

            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task ProcessTopFixtures_OutrightFixture_SkipsEntirely()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("should not call HTTP"), out var handler);
            var repository = BuildRepository(memCache);

            await repository.ProcessTopFixtures(client, new OnMarketChangeMessage { FiTyId = (int)FixtureType.Outright, SpId = 1 });

            Assert.Equal(0, handler.CallCount);
            Assert.False(memCache.Contains($"{clientCode}:TF"));
        }

        // ---- ProcessTopLeagues ----

        [Fact]
        public async Task ProcessTopLeagues_CacheMiss_Success_CachesSegmentLeagueIds()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => Json(HttpStatusCode.OK,
                """{"StatusCode":100,"Result":{"TopLeagues":[{"SegmentId":2,"LeagueId":3}]}}"""), out _);
            var repository = BuildRepository(memCache);

            await repository.ProcessTopLeagues(client, new OnMarketChangeMessage { FiTyId = (int)FixtureType.Match, SpId = 10 });

            var cached = memCache.GetCache<TopLeague>($"{clientCode}:TLL:10");
            Assert.NotNull(cached);
            Assert.Equal(new Tuple<int, int>(2, 3), Assert.Single(cached!.SegmentLeagueIds));
        }

        [Fact]
        public async Task ProcessTopLeagues_MarkerCacheAlreadyPresent_SkipsHttpCall()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            memCache.AddOrUpdateCache($"{clientCode}:TL:10", new TopCache { Id = 0 }, DateTimeOffset.UtcNow.AddMinutes(5));
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("should not call HTTP"), out var handler);
            var repository = BuildRepository(memCache);

            await repository.ProcessTopLeagues(client, new OnMarketChangeMessage { FiTyId = (int)FixtureType.Match, SpId = 10 });

            Assert.Equal(0, handler.CallCount);
        }

        // ---- ProcessMarketCount ----

        [Fact]
        public async Task ProcessMarketCount_FixtureStatusNotTracked_DeletesCacheAndReturns()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var key = $"{clientCode}:mc:99";
            memCache.AddOrUpdateCache(key, new MarketCount { FiId = 99, MarketIds = new List<long> { 1 } }, DateTimeOffset.UtcNow.AddDays(1));
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("should not call HTTP"), out var handler);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketCount(client, new OnMarketChangeMessage
            {
                FiId = 99,
                FiStId = (int)FixtureStatus.Ended,
                Ma = new List<MarketMessage>()
            });

            Assert.Equal(0, handler.CallCount);
            Assert.False(memCache.Contains(key));
        }

        [Fact]
        public async Task ProcessMarketCount_CacheMiss_FetchesFixtureDetails_AndCachesMarketIds()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => Json(HttpStatusCode.OK,
                """{"StatusCode":100,"Result":{"Fixtures":[{"FiId":"1","Markets":[{"MarketId":"501"},{"MarketId":"502"}]}]}}"""), out var handler);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketCount(client, new OnMarketChangeMessage
            {
                FiId = 1,
                FiStId = (int)FixtureStatus.Live,
                Ma = new List<MarketMessage>()
            });

            Assert.Equal(1, handler.CallCount);
            var cached = memCache.GetCache<MarketCount>($"{clientCode}:mc:1");
            Assert.NotNull(cached);
            Assert.Equal(new[] { 501L, 502L }, cached!.MarketIds.OrderBy(x => x));
        }

        [Fact]
        public async Task ProcessMarketCount_CacheHit_UsesCachedIds_SkipsHttpCall()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            memCache.AddOrUpdateCache($"{clientCode}:mc:1", new MarketCount { FiId = 1, MarketIds = new List<long> { 501 } }, DateTimeOffset.UtcNow.AddDays(1));
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("should not call HTTP"), out var handler);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketCount(client, new OnMarketChangeMessage
            {
                FiId = 1,
                FiStId = (int)FixtureStatus.Live,
                Ma = new List<MarketMessage>()
            });

            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task ProcessMarketCount_ActiveMarketAdded_UnionsIntoCurrentIds()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            memCache.AddOrUpdateCache($"{clientCode}:mc:1", new MarketCount { FiId = 1, MarketIds = new List<long> { 501 } }, DateTimeOffset.UtcNow.AddDays(1));
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("should not call HTTP"), out _);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketCount(client, new OnMarketChangeMessage
            {
                FiId = 1,
                FiStId = (int)FixtureStatus.Live,
                SpId = 1,
                FiTyId = (int)FixtureType.Match,
                Ma = new List<MarketMessage> { new() { MaId = 777, MaStId = (int)MarketStatus.Active } }
            });

            var cached = memCache.GetCache<MarketCount>($"{clientCode}:mc:1");
            Assert.Equal(new[] { 501L, 777L }, cached!.MarketIds.OrderBy(x => x));
        }

        // ---- ProcessFavorites / ProcessRemoveFavorites ----

        [Fact]
        public async Task ProcessFavorites_NewFavorite_CreatesCacheWithUser()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var repository = BuildRepository(memCache);
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessFavorites(client, new IncomingVisitFavorites
            {
                UserId = "U1",
                Favorites = new List<UserFavorites> { new() { HierarchyId = 2, SportId = 1, Id = "42" } }
            });

            var cached = memCache.GetCache<Favorites>($"{clientCode}:fli:1:42");
            Assert.Equal(new[] { "U1" }, cached!.UserIds);
        }

        [Fact]
        public async Task ProcessFavorites_NonLeagueHierarchy_UsesFfiKey()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var repository = BuildRepository(memCache);
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessFavorites(client, new IncomingVisitFavorites
            {
                UserId = "U1",
                Favorites = new List<UserFavorites> { new() { HierarchyId = 1, SportId = 1, Id = "42" } }
            });

            Assert.NotNull(memCache.GetCache<Favorites>($"{clientCode}:ffi:1:42"));
        }

        [Fact]
        public async Task ProcessFavorites_SameUserTwice_DoesNotDuplicate()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var repository = BuildRepository(memCache);
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("no HTTP expected"), out _);
            var message = new IncomingVisitFavorites
            {
                UserId = "U1",
                Favorites = new List<UserFavorites> { new() { HierarchyId = 2, SportId = 1, Id = "42" } }
            };

            await repository.ProcessFavorites(client, message);
            await repository.ProcessFavorites(client, message);

            var cached = memCache.GetCache<Favorites>($"{clientCode}:fli:1:42");
            Assert.Equal(new[] { "U1" }, cached!.UserIds);
        }

        [Fact]
        public async Task ProcessRemoveFavorites_ExistingUser_RemovesIt()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            memCache.AddOrUpdateCache($"{clientCode}:fli:1:42", new Favorites { UserIds = new List<string> { "U1", "U2" } }, DateTimeOffset.UtcNow.AddDays(1));
            var repository = BuildRepository(memCache);
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessRemoveFavorites(client, new IncomingVisitFavorites
            {
                UserId = "U1",
                Favorites = new List<UserFavorites> { new() { HierarchyId = 2, SportId = 1, Id = "42" } }
            });

            Assert.Equal(new[] { "U2" }, memCache.GetCache<Favorites>($"{clientCode}:fli:1:42")!.UserIds);
        }

        [Fact]
        public async Task ProcessRemoveFavorites_NoExistingCache_NoOp()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var repository = BuildRepository(memCache);
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessRemoveFavorites(client, new IncomingVisitFavorites
            {
                UserId = "U1",
                Favorites = new List<UserFavorites> { new() { HierarchyId = 2, SportId = 1, Id = "42" } }
            });

            Assert.Null(memCache.GetCache<Favorites>($"{clientCode}:fli:1:42"));
        }

        // ---- ProcessBoostedFixtures ----

        [Fact]
        public async Task ProcessBoostedFixtures_CachesFixtureIds()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var repository = BuildRepository(memCache);
            var client = BuildClient(clientCode, _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessBoostedFixtures(client, new IncomingVisitBoostedFixtures { FixtureIds = new List<long> { 1, 2, 3 } });

            Assert.Equal(new List<long> { 1, 2, 3 }, memCache.GetCache<List<long>>($"{clientCode}:bf"));
        }

        // ---- ProcessMarketSortOrder ----

        [Fact]
        public async Task ProcessMarketSortOrder_Prematch_CachesUnderPmsoKey()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => Json(HttpStatusCode.OK,
                """{"StatusCode":100,"Result":{"MarketTemplateGroups":[{"FiStId":0,"SportId":1,"MaTeIds":"16,18"}]}}"""), out _);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketSortOrder(client);

            var cached = memCache.GetCache<ClientMarketSortOrder>($"{clientCode}:pmso:1");
            Assert.Equal(new[] { "16", "18" }, cached!.MaTeIds);
        }

        [Fact]
        public async Task ProcessMarketSortOrder_Live_CachesUnderLmsoKey()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => Json(HttpStatusCode.OK,
                """{"StatusCode":100,"Result":{"MarketTemplateGroups":[{"FiStId":1,"SportId":1,"MaTeIds":"66"}]}}"""), out _);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketSortOrder(client);

            Assert.NotNull(memCache.GetCache<ClientMarketSortOrder>($"{clientCode}:lmso:1"));
        }

        [Fact]
        public async Task ProcessMarketSortOrder_PlatformApiError_CachesNothing()
        {
            var memCache = new MemCache();
            var clientCode = NewClientCode();
            var client = BuildClient(clientCode, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent(string.Empty)
            }, out _);
            var repository = BuildRepository(memCache);

            await repository.ProcessMarketSortOrder(client);

            Assert.False(memCache.Contains($"{clientCode}:pmso:1"));
            Assert.False(memCache.Contains($"{clientCode}:lmso:1"));
        }

        // ---- Hub-push methods: smoke tests only ----
        //
        // These methods (ProcessBetStopGlobal, ProcessBetStatus, ProcessCashOutChange,
        // ProcessWalletChange, ProcessPayableChange, ProcessBetStopFixture,
        // ProcessOddsChangeForMainMarkets, ProcessOddsChangeForAllMarkets) all funnel through the
        // private PublishToWebSocket, which calls the concrete (non-virtual, unmockable)
        // HubConnection.SendAsync. There is no seam to intercept the outgoing payload, and every
        // one of these methods catches and swallows any exception SendAsync throws — so the most
        // that can be verified without a live SignalR connection (this repo has no such test
        // infrastructure, and adding one is an architecture decision, not a test-writing one) is
        // that a disconnected HubConnection doesn't crash the caller. Payload-shape correctness for
        // these methods is NOT covered.
        private static HubConnection UnstartedHubConnection() =>
            new HubConnectionBuilder().WithUrl("http://127.0.0.1:1/platform").Build();

        [Fact]
        public async Task ProcessBetStopGlobal_DisconnectedHub_DoesNotThrow()
        {
            var memCache = new MemCache();
            var repository = BuildRepository(memCache);
            var client = BuildClient(NewClientCode(), _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessBetStopGlobal(client, new OnBetStopGlobalMessage { IsBetStop = true }, UnstartedHubConnection());
        }

        [Fact]
        public async Task ProcessBetStatus_DisconnectedHub_DoesNotThrow()
        {
            var memCache = new MemCache();
            var repository = BuildRepository(memCache);
            var client = BuildClient(NewClientCode(), _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessBetStatus(client, new IncomingBetStatus
            {
                UserId = "U1",
                BetId = "b1",
                BetStatus = BetStatus.Won
            }, UnstartedHubConnection());
        }

        [Fact]
        public async Task ProcessPayableChange_DisconnectedHub_DoesNotThrow()
        {
            var memCache = new MemCache();
            var repository = BuildRepository(memCache);
            var client = BuildClient(NewClientCode(), _ => throw new InvalidOperationException("no HTTP expected"), out _);

            await repository.ProcessPayableChange(client, new IncomingPayableChange { UserId = "U1", Payable = 10m }, UnstartedHubConnection());
        }
        private sealed class FakeHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public int CallCount { get; private set; }

            public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return Task.FromResult(_responder(request));
            }
        }
    }
}

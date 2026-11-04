using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Adapter.Service.ClientVisibility;

namespace OE.Adapter.Service.Tests
{
    // The lists in use: loaded before the adapter consumes, replaced as a whole, and kept when a load fails, so an
    // outage of Trading or the Platform API never changes what is stamped.
    public class ClientVisibilityCacheTests
    {
        private sealed class FakeSource : IClientVisibilitySource
        {
            public List<string> Clients { get; set; } = new() { "G01", "TSO" };
            public List<LeagueRestriction> Restrictions { get; } = new();
            public int ClientReads { get; private set; }

            // the next reads of the client list that fail
            public int FailTimes { get; set; }

            public Task<IReadOnlyList<string>> ReadClientCodesAsync(CancellationToken cancellationToken)
            {
                ClientReads++;

                if (FailTimes > 0)
                {
                    FailTimes--;
                    throw new InvalidOperationException("source down");
                }

                return Task.FromResult<IReadOnlyList<string>>(Clients.ToList());
            }

            public Task<IReadOnlyList<LeagueRestriction>> ReadRestrictionsAsync(CancellationToken cancellationToken)
                => Task.FromResult<IReadOnlyList<LeagueRestriction>>(Restrictions.ToList());
        }

        private static ClientVisibilityCache Build(FakeSource source, string? trading = "Server=.;Database=Trading;", string? platform = "http://platform.test/")
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Trading"] = trading,
                    ["API:PlatformAPI"] = platform,
                    ["Custom:ClientVisibilityPreloadRetryCount"] = "3",
                    ["Custom:ClientVisibilityPreloadRetryDelaySeconds"] = "0"
                })
                .Build();

            return new ClientVisibilityCache(source, configuration, NullLogger<ClientVisibilityCache>.Instance);
        }

        [Theory]
        [InlineData("Server=.;", "http://platform.test/", true)]
        [InlineData(null, "http://platform.test/", false)]
        [InlineData("Server=.;", null, false)]
        [InlineData("  ", "http://platform.test/", false)]
        public void IsConfigured_NeedsBothTheTradingConnectionStringAndThePlatformAddress(string? trading, string? platform, bool expected)
        {
            Assert.Equal(expected, Build(new FakeSource(), trading, platform).IsConfigured);
        }

        [Fact]
        public async Task Preload_NotConfigured_ReadsNothing_AndLeavesTheListsUnloaded()
        {
            var source = new FakeSource();
            var cache = Build(source, trading: null);

            await cache.PreloadAsync(CancellationToken.None);

            Assert.Equal(0, source.ClientReads);
            Assert.False(cache.IsLoaded);
            Assert.Null(cache.ClientCodesFor(1, 2, 3));
        }

        [Fact]
        public async Task Preload_LoadsTheLists()
        {
            var source = new FakeSource();
            source.Restrictions.Add(new LeagueRestriction { ClientCode = "TSO", League = new LeagueKey(1, 2, 3) });
            var cache = Build(source);

            Assert.Null(cache.ClientCodesFor(1, 2, 3)); // nothing before the first load

            await cache.PreloadAsync(CancellationToken.None);

            Assert.True(cache.IsLoaded);
            Assert.Equal("G01", cache.ClientCodesFor(1, 2, 3));
            Assert.Equal("G01|TSO", cache.ClientCodesFor(1, 2, 4));
        }

        [Fact]
        public async Task Preload_RetriesAFailedReadUntilItWorks()
        {
            var source = new FakeSource { FailTimes = 2 };
            var cache = Build(source);

            await cache.PreloadAsync(CancellationToken.None);

            Assert.Equal(3, source.ClientReads);
            Assert.True(cache.IsLoaded);
        }

        [Fact]
        public async Task Preload_GivesUpAfterTheRetryCount_SoTheServiceRestarts()
        {
            var source = new FakeSource { FailTimes = 10 };
            var cache = Build(source);

            await Assert.ThrowsAsync<InvalidOperationException>(() => cache.PreloadAsync(CancellationToken.None));

            Assert.Equal(3, source.ClientReads);
            Assert.False(cache.IsLoaded);
        }

        [Fact]
        public async Task Refresh_TheFirstLoadReportsNoChange_ALaterOneReportsTheChangedLeague()
        {
            var source = new FakeSource();
            var cache = Build(source);

            Assert.Empty(await cache.RefreshAsync(CancellationToken.None));

            source.Restrictions.Add(new LeagueRestriction { ClientCode = "TSO", League = new LeagueKey(1, 2, 3) });

            Assert.Equal(new[] { new LeagueKey(1, 2, 3) }, await cache.RefreshAsync(CancellationToken.None));
            Assert.Equal("G01", cache.ClientCodesFor(1, 2, 3));
        }

        [Fact]
        public async Task Refresh_AFailedRead_KeepsTheListsInUse()
        {
            var source = new FakeSource();
            var cache = Build(source);
            await cache.RefreshAsync(CancellationToken.None);

            source.FailTimes = 1;
            await Assert.ThrowsAsync<InvalidOperationException>(() => cache.RefreshAsync(CancellationToken.None));

            Assert.Equal("G01|TSO", cache.ClientCodesFor(1, 2, 3));
        }

        [Fact]
        public async Task Refresh_AnEmptyClientList_IsAFailedRead_NotAnEmptyClCoForEveryLeague()
        {
            var source = new FakeSource();
            var cache = Build(source);
            await cache.RefreshAsync(CancellationToken.None);

            source.Clients = new List<string>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => cache.RefreshAsync(CancellationToken.None));

            Assert.Equal("G01|TSO", cache.ClientCodesFor(1, 2, 3));
        }
    }
}

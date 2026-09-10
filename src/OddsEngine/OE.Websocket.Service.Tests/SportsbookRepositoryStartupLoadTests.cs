using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OE.Shared.Utilities;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Repository;
using OE.WebSocket.Service.Telemetry;

namespace OE.Websocket.Service.Tests
{
    // GM-791 log noise: the startup loaders dereferenced result.Result blindly, so a
    // Platform API error surfaced as one NullReference/ArgumentNull stack trace per
    // client per endpoint (96 lines in a 10 second window). They must now log a single
    // warning carrying the API status and skip, caching nothing.
    public class SportsbookRepositoryStartupLoadTests
    {
        [Fact]
        public async Task PlatformApiError_LogsOneWarningPerLoad_CachesNothing()
        {
            var logger = new CapturingLogger<SportsbookRepository>();
            var memCache = new MemCache();
            var repository = new SportsbookRepository(BuildConfiguration(), memCache, new Instrumentation(), logger);
            var client = BuildClient("gm791", HttpStatusCode.NotFound);

            var templatesLoaded = await repository.ProcessMainMarketTemplates(client);
            await repository.ProcessBetBuilderMarketTemplates(client);
            await repository.ProcessMarketTemplatePlayerProps(client);
            await repository.ProcessFixtureSortOrder(client);

            var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).ToList();

            // A failed template load must be reported so the Worker keeps the client
            // out of the odds fan-out instead of publishing with an empty template list.
            Assert.False(templatesLoaded);
            Assert.Empty(client.MainMarketTemplates);
            Assert.Equal(4, warnings.Count);
            Assert.All(warnings, w => Assert.Contains("Not Found", w.Message));
            Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Error);
            Assert.False(memCache.Contains("gm791:bbmt"));
            Assert.False(memCache.Contains("gm791:mtpp"));
        }
        private static IConfiguration BuildConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "Custom:AsianClientCodes", "TSO,S88" },
                    { "Custom:IsOEEnabled", "false" }
                })
                .Build();
        }

        private static ClientContext BuildClient(string clientCode, HttpStatusCode platformApiStatus)
        {
            return new ClientContext
            {
                ClientId = "11111111-1111-1111-1111-111111111111",
                ClientCode = clientCode,
                HttpClient = new HttpClient(new FixedStatusHandler(platformApiStatus))
                {
                    BaseAddress = new Uri("http://platform-api.test/")
                }
            };
        }

        // Every Platform API call answers with the same status and an empty body.
        private sealed class FixedStatusHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;

            public FixedStatusHandler(HttpStatusCode status) => _status = status;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(string.Empty) });
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}

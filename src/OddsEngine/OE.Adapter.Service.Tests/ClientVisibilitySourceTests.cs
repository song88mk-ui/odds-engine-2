using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OE.Adapter.Service.ClientVisibility;
using OE.Shared.Test;

namespace OE.Adapter.Service.Tests
{
    // The client list is the one Backoffice's Config page shows: GET v1/clients of the Platform API with the internal
    // key. The Trading read of the restrictions is a plain query and is checked on DEV, as this project has no SQL
    // fixture.
    public class ClientVisibilitySourceTests
    {
        private static ClientVisibilitySource Build(FakeHttpMessageHandler handler)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["API:PlatformAPI"] = "http://platform.test/",
                    ["API:APIKey"] = "test-key"
                })
                .Build();

            return new ClientVisibilitySource(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), configuration, handler);
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

        [Fact]
        public async Task ReadClientCodes_CallsTheClientsRouteWithTheApiKey_AndReturnsTheCodes()
        {
            var handler = new FakeHttpMessageHandler(_ => Json(
                "{\"statusCode\":100,\"message\":\"Success\",\"result\":[{\"clientId\":\"6f5c3f44-0000-0000-0000-000000000001\",\"clientName\":\"One\",\"clientCode\":\"G01\"},{\"clientCode\":\"TSO\"}]}"));

            var codes = await Build(handler).ReadClientCodesAsync(CancellationToken.None);

            Assert.Equal(new[] { "G01", "TSO" }, codes);
            Assert.Equal("/v1/clients", handler.LastRequest!.RequestUri!.AbsolutePath);
            Assert.Equal("test-key", Assert.Single(handler.LastRequest.Headers.GetValues("x-api-key")));
        }

        [Fact]
        public async Task ReadClientCodes_DropsBlankCodes()
        {
            var handler = new FakeHttpMessageHandler(_ => Json("{\"statusCode\":100,\"result\":[{\"clientCode\":\"G01\"},{\"clientCode\":\"  \"},{}]}"));

            Assert.Equal(new[] { "G01" }, await Build(handler).ReadClientCodesAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadClientCodes_AnErrorStatusInTheBody_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ => Json("{\"statusCode\":101,\"message\":\"Missing key\",\"result\":null}"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(handler).ReadClientCodesAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadClientCodes_AnErrorStatusEvenWithAResultInTheBody_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ => Json("{\"statusCode\":101,\"message\":\"Missing key\",\"result\":[{\"clientCode\":\"G01\"}]}"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => Build(handler).ReadClientCodesAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadClientCodes_AFailedHttpCall_Throws()
        {
            var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

            await Assert.ThrowsAsync<HttpRequestException>(() => Build(handler).ReadClientCodesAsync(CancellationToken.None));
        }

        [Fact]
        public async Task ReadClientCodes_ReusesOneHttpClient()
        {
            var handler = new FakeHttpMessageHandler(_ => Json("{\"statusCode\":100,\"result\":[{\"clientCode\":\"G01\"}]}"));
            var source = Build(handler);

            await source.ReadClientCodesAsync(CancellationToken.None);
            await source.ReadClientCodesAsync(CancellationToken.None);

            Assert.Equal(2, handler.CallCount);
        }
    }
}

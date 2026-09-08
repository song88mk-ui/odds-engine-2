using System.Net;
using OE.Shared.Enums;
using OE.Shared.Models;
using OE.Shared.Repositories;

namespace OE.Shared.Test.Repositories
{
    // Publishes via the BetGenius-hosted HTTP broker bridge (used where a direct AMQP connection
    // isn't available). Every path swallows exceptions and reports them as a StatusCode.Error DTO
    // rather than throwing, so callers never need a try/catch around Publish*.
    public class RabbitMQRepositoryTests
    {
        private static readonly MessageRequestDTO Request = new()
        {
            Payload = "{}",
            MessageType = "ON_FIXTURE_CHANGE",
            RoutingKey = "r.oe.adapter.onfixturechange.00",
            CorrelationId = "cid"
        };

        [Fact]
        public void PublishViaHttp_Success_DeserializesResponse()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"StatusCode":100,"Message":"Success","Result":{"IsSuccess":true}}""")
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.Equal(StatusCode.Success, result.StatusCode);
            Assert.True(result.Result.IsSuccess);
        }

        [Fact]
        public void PublishViaHttp_BadRequest_DeserializesErrorBody()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"StatusCode":301,"Message":"Missing_Bet_Id","Result":null}""")
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.Equal(StatusCode.Missing_Bet_Id, result.StatusCode);
        }

        [Fact]
        public void PublishViaHttp_OtherErrorStatus_ReturnsGenericError()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("Service Unavailable", result.Message);
        }

        [Fact]
        public void PublishViaHttp_TransportException_ReturnsErrorWithMessage()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => throw new HttpRequestException("connection refused"), out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("connection refused", result.Message);
        }

        [Fact]
        public async Task PublishViaHttpAsync_Success_DeserializesResponse()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"StatusCode":100,"Message":"Success","Result":{"IsSuccess":true}}""")
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = await repository.PublishViaHttpAsync(Request);

            Assert.Equal(StatusCode.Success, result.StatusCode);
            Assert.True(result.Result.IsSuccess);
        }

        [Fact]
        public async Task PublishViaHttpAsync_OtherErrorStatus_ReturnsGenericError()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = "Internal Server Error"
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = await repository.PublishViaHttpAsync(Request);

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("Internal Server Error", result.Message);
        }

        [Fact]
        public async Task PublishViaHttpAsync_TransportException_ReturnsErrorWithMessage()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => throw new HttpRequestException("timed out"), out _);
            var repository = new RabbitMQRepository(client);

            var result = await repository.PublishViaHttpAsync(Request);

            Assert.Equal(StatusCode.Error, result.StatusCode);
            Assert.Contains("timed out", result.Message);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void PublishViaHttp_Success_DeserializesResponse_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"StatusCode":100,"Message":"Success","Result":{"IsSuccess":true}}""")
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(result.Result.IsSuccess));
        }

        [Fact]
        public void PublishViaHttp_BadRequest_DeserializesErrorBody_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"StatusCode":301,"Message":"Missing_Bet_Id","Result":null}""")
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
        }

        [Fact]
        public void PublishViaHttp_OtherErrorStatus_ReturnsGenericError_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                ReasonPhrase = "Service Unavailable"
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("Service Unavailable", result.Message));
        }

        [Fact]
        public void PublishViaHttp_TransportException_ReturnsErrorWithMessage_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => throw new HttpRequestException("connection refused"), out _);
            var repository = new RabbitMQRepository(client);

            var result = repository.PublishViaHttp(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("connection refused", result.Message));
        }

        [Fact]
        public async Task PublishViaHttpAsync_Success_DeserializesResponse_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"StatusCode":100,"Message":"Success","Result":{"IsSuccess":true}}""")
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = await repository.PublishViaHttpAsync(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Error, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(result.Result.IsSuccess));
        }

        [Fact]
        public async Task PublishViaHttpAsync_OtherErrorStatus_ReturnsGenericError_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                ReasonPhrase = "Internal Server Error"
            }, out _);
            var repository = new RabbitMQRepository(client);

            var result = await repository.PublishViaHttpAsync(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("Internal Server Error", result.Message));
        }

        [Fact]
        public async Task PublishViaHttpAsync_TransportException_ReturnsErrorWithMessage_MutationCheck()
        {
            var client = FakeHttpMessageHandler.ClientFor(_ => throw new HttpRequestException("timed out"), out _);
            var repository = new RabbitMQRepository(client);

            var result = await repository.PublishViaHttpAsync(Request);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(StatusCode.Success, result.StatusCode));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("timed out", result.Message));
        }

        #endregion
    }
}

using System.Net.Http;

namespace OE.Shared.Test
{
    // Stands in for the real transport so HttpClient-based repositories can be exercised without a
    // live endpoint. Records the last request so tests can assert on headers/URI, and the responder
    // can throw to simulate transport failures (timeouts, DNS errors, etc.).
    public class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            LastRequestBody = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            return Task.FromResult(_responder(request));
        }

        public static HttpClient ClientFor(Func<HttpRequestMessage, HttpResponseMessage> responder, out FakeHttpMessageHandler handler)
        {
            handler = new FakeHttpMessageHandler(responder);
            return new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        }
    }
}

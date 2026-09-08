using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OE.WebSocket.Service.Telemetry
{

    public class Instrumentation : IDisposable
    {
        public ActivitySource ActivitySource { get; }
        public Counter<long> PublishedToWebSocketCounter { get; }

        internal const string ActivitySourceName = "OE.WebSocket.Service";
        internal const string MeterName = "OE.WebSocket.Service";

        private readonly Meter _meter;

        public Instrumentation()
        {
            var version = "latest";
            ActivitySource = new ActivitySource(ActivitySourceName, version);
            _meter = new Meter(MeterName, version);

            PublishedToWebSocketCounter = _meter.CreateCounter<long>("published.to.ws", "count", "Number of published messages to WS");
        }

        public void Dispose()
        {
            ActivitySource.Dispose();
            _meter.Dispose();
        }
    }
}

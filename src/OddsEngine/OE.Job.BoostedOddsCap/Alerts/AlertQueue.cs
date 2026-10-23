using System.Threading.Channels;
using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // The hand-off between the odds consumer and the delivery worker. Bounded, and it drops rather than waits
    // when full, so the consumer is never blocked by a slow mail server: a stuck delivery loses alerts (counted
    // and logged by the caller) instead of stalling the odds stream behind it.
    public class AlertQueue
    {
        private readonly Channel<CapAlert> _channel;

        public AlertQueue(int capacity, Action<CapAlert> onDropped)
        {
            _channel = Channel.CreateBounded<CapAlert>(
                    new BoundedChannelOptions(Math.Max(1, capacity))
                    {
                        FullMode = BoundedChannelFullMode.DropWrite,
                        SingleReader = true
                    },
                    onDropped);
        }

        public ChannelReader<CapAlert> Reader => _channel.Reader;

        public void Enqueue(CapAlert alert)
        {
            _channel.Writer.TryWrite(alert);
        }
    }
}

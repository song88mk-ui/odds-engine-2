namespace OE.Job.BoostedOddsCap.Alerts
{
    // Caps how many emails one recipient receives in a rolling hour, so a mailbox is never flooded however the
    // market moves. Used by the single delivery thread only.
    public class RecipientRateLimiter
    {
        private readonly int _maxPerHour;
        private readonly Dictionary<string, Queue<DateTime>> _sent = new Dictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);

        public RecipientRateLimiter(int maxPerHour)
        {
            _maxPerHour = maxPerHour;
        }

        // True, and the send is counted, when the recipient is still under the limit.
        public bool TryAcquire(string recipient, DateTime nowUtc)
        {
            if (_maxPerHour <= 0)
                return true;

            if (!_sent.TryGetValue(recipient, out var sentAt))
                _sent[recipient] = sentAt = new Queue<DateTime>();

            var cutoff = nowUtc.AddHours(-1);
            while (sentAt.Count > 0 && sentAt.Peek() <= cutoff)
                sentAt.Dequeue();

            if (sentAt.Count >= _maxPerHour)
                return false;

            sentAt.Enqueue(nowUtc);
            return true;
        }
    }
}

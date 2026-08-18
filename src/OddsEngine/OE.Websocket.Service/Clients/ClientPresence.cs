namespace OE.WebSocket.Service.Clients
{
    public interface IClientPresence
    {
        // True when the client has at least one connected user on the hub.
        // FAILS OPEN: also true when no poll has succeeded yet or the last
        // successful poll is stale - the failure mode must be wasted publishes,
        // never silently starved clients. This also makes the feeder safe to run
        // against a hub that does not expose /v1/activeclients yet.
        bool IsActive(string clientCode);

        void Update(IReadOnlyCollection<string> activeClientCodes);

        void MarkUnavailable();
    }

    // Presence state for the GM-720 presence-aware skip, fed by the Worker's poll
    // of the hub's /v1/activeclients endpoint. Applied only to the high-volume
    // odds-change fan-out; one-off user messages (bet status, wallet, cashout)
    // are never presence-filtered because a missed one does not self-heal.
    public class ClientPresence : IClientPresence
    {
        public const int PollSeconds = 5;

        // A poll result older than this no longer proves anything - fail open.
        private const long StaleAfterMilliseconds = PollSeconds * 6 * 1000L;

        private volatile HashSet<string> _activeCodes = new();
        private long _lastUpdateTick; // Environment.TickCount64 of last successful poll; 0 = none

        public bool IsActive(string clientCode)
        {
            var lastUpdate = Interlocked.Read(ref _lastUpdateTick);

            if (lastUpdate == 0)
                return true;

            if (Environment.TickCount64 - lastUpdate > StaleAfterMilliseconds)
                return true;

            return _activeCodes.Contains(clientCode);
        }

        public void Update(IReadOnlyCollection<string> activeClientCodes)
        {
            _activeCodes = (activeClientCodes ?? Array.Empty<string>()).Select(c => c.ToLower()).ToHashSet();

            Interlocked.Exchange(ref _lastUpdateTick, Environment.TickCount64);
        }

        public void MarkUnavailable()
        {
            Interlocked.Exchange(ref _lastUpdateTick, 0);
        }
    }
}

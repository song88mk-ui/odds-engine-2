using System.Collections.Concurrent;
using OE.Job.BoostedOddsCap.Boosts;

namespace OE.Job.BoostedOddsCap.Evaluation
{
    // Decides when a boost deserves an alert. The v1 job alerted on every odds message while a boost sat within
    // its cap, which under a busy market is a toast and an email per tick. Here a boost alerts once when it
    // crosses into the cap, and is only re-armed after its gap has climbed back above the cap by the hysteresis,
    // so odds jittering around the line do not alert again and again.
    //
    // State is in memory: a restart forgets which boosts already alerted. To avoid re-alerting every boost that
    // is sitting inside its cap, a boost first seen inside the cap during the silent window after startup is
    // recorded without alerting (see StartSilentWindow).
    //
    // Evaluate runs on the single consumer thread; Retain runs on the refresher thread and only removes entries.
    public class CapAlertTracker
    {
        private sealed class BoostState
        {
            public bool IsInsideCap;
            public decimal BoostedOdds;
            public decimal CapPercent;
        }

        private readonly ConcurrentDictionary<string, BoostState> _states = new ConcurrentDictionary<string, BoostState>();
        private readonly decimal _hysteresisPercent;
        private DateTime _silentUntilUtc = DateTime.MinValue;

        public CapAlertTracker(decimal hysteresisPercent)
        {
            _hysteresisPercent = hysteresisPercent;
        }

        public int Count => _states.Count;

        public void StartSilentWindow(DateTime untilUtc)
        {
            _silentUntilUtc = untilUtc;
        }

        public bool ShouldAlert(BoostRule rule, decimal gapPercent, DateTime nowUtc)
        {
            var isInsideCap = gapPercent <= rule.CapPercent;

            // First sight of this boost, or the trader changed its odds or the client's cap: judge it afresh.
            if (!_states.TryGetValue(rule.Key, out var state)
                    || state.BoostedOdds != rule.BoostedOdds || state.CapPercent != rule.CapPercent)
            {
                _states[rule.Key] = new BoostState
                {
                    IsInsideCap = isInsideCap,
                    BoostedOdds = rule.BoostedOdds,
                    CapPercent = rule.CapPercent
                };

                return isInsideCap && nowUtc >= _silentUntilUtc;
            }

            if (!state.IsInsideCap)
            {
                if (!isInsideCap)
                    return false;

                state.IsInsideCap = true;
                return true;
            }

            if (gapPercent > rule.CapPercent + _hysteresisPercent)
                state.IsInsideCap = false;

            return false;
        }

        // Drops the state of boosts that no longer exist, so memory follows the Trading table, not history.
        public void Retain(IEnumerable<string> liveKeys)
        {
            var live = new HashSet<string>(liveKeys);

            foreach (var key in _states.Keys)
            {
                if (!live.Contains(key))
                    _states.TryRemove(key, out _);
            }
        }
    }
}

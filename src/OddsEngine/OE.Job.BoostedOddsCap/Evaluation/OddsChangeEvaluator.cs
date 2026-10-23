using OE.Job.BoostedOddsCap.Boosts;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Job.BoostedOddsCap.Evaluation
{
    // Judges one odds change message against the boosts of its fixture and returns the boosts that have just
    // reached their cap. Pure apart from the tracker, so it is exercised without a broker or a database.
    public class OddsChangeEvaluator
    {
        private static readonly IReadOnlyList<CapAlert> NoAlerts = new CapAlert[0];

        private readonly CapAlertTracker _tracker;
        private readonly ISet<string> _playerPropMarketTemplateIds;

        // Player prop markets are priced differently per client (see ClientOddsCalculator) and the websocket
        // service learns which templates they are from the Platform API per client, which this job does not
        // call. They are configured here instead (Custom:PlayerPropMarketTemplateIds); left empty, a boost on a
        // player prop market is judged against the regular repricing.
        public OddsChangeEvaluator(CapAlertTracker tracker, ISet<string> playerPropMarketTemplateIds)
        {
            _tracker = tracker;
            _playerPropMarketTemplateIds = playerPropMarketTemplateIds ?? new HashSet<string>();
        }

        public IReadOnlyList<CapAlert> Evaluate(OnInternalOddsChangeMessage message, BoostRule[] rules, DateTime nowUtc)
        {
            // boosts are a prematch feature and a live fixture's odds move too fast to be a useful signal
            if (message?.Ma == null || rules == null || rules.Length == 0
                    || message.FiStId != (int)FixtureStatus.Prematch)
                return NoAlerts;

            List<CapAlert> alerts = null;

            foreach (var market in message.Ma)
            {
                // a suspended or closed market still carries its last odds, which are not on offer
                if (market?.Se == null || market.MaStId != (int)MarketStatus.Active)
                    continue;

                var marketSpecifier = BoostText.Specifier(market.MaSp);

                foreach (var rule in rules)
                {
                    if (rule.MarketTemplateId != market.MaTeId || rule.MarketSpecifier != marketSpecifier)
                        continue;

                    // the client is not offered this market, so there are no client odds to compare with
                    if (market.ClCo == null || !market.ClCo.Contains(rule.ClientCode))
                        continue;

                    var selection = FindSelection(market, rule);
                    if (selection == null)
                        continue;

                    if (!ClientOddsCalculator.TryGetClientOdds(market, selection, rule.ClientCode,
                            _playerPropMarketTemplateIds.Contains(market.MaTeId ?? string.Empty), out var currentOdds))
                        continue;

                    if (!CapMath.TryGetGapPercent(rule.BoostedOdds, currentOdds, out var gapPercent))
                        continue;

                    if (!_tracker.ShouldAlert(rule, gapPercent, nowUtc))
                        continue;

                    alerts ??= new List<CapAlert>();
                    alerts.Add(new CapAlert
                    {
                        ClientCode = rule.ClientCode,
                        ProviderFixtureId = rule.ProviderFixtureId,
                        SportId = message.SpId,
                        SportName = message.SpNa,
                        SegmentName = message.SegNa,
                        LeagueName = message.LeNa,
                        FixtureName = message.FiNa,
                        MarketName = market.MaNa,
                        SelectionName = selection.SeNa,
                        BoostedOdds = rule.BoostedOdds,
                        CurrentOdds = currentOdds,
                        CapPercent = rule.CapPercent,
                        GapPercent = gapPercent,
                        DetectedAtUtc = nowUtc
                    });
                }
            }

            return alerts ?? NoAlerts;
        }

        private static InternalSelectionMessage FindSelection(InternalMarketMessage market, BoostRule rule)
        {
            foreach (var selection in market.Se)
            {
                if (selection != null && selection.SeTeId == rule.SelectionTemplateId
                        && BoostText.Specifier(selection.SeSp) == rule.SelectionSpecifier)
                    return selection;
            }

            return null;
        }
    }
}

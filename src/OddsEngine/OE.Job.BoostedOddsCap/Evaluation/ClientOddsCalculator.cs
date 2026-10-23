using System.Globalization;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Job.BoostedOddsCap.Evaluation
{
    // The decimal odds a client is offered for a selection. The published odds change message carries only
    // the master odds (Od): nothing in the pipeline fills the per-client list (ClOd), and OE.Websocket.Service
    // reprices every market per client when it pushes it. This is that repricing, for one selection and the
    // decimal format only, so the cap is judged against the same price the client sees.
    //
    // It follows OE.Websocket.Service SportsbookRepository.GetClientOdds and the margin computed by its caller
    // step for step, including the quirks, on purpose: a "fix" here would make the alert disagree with the
    // client's screen. That copy is private to the websocket service (and OE.UOF.Client has a third one), so
    // any change there has to be mirrored here. See docs/TechnicalDebt.md.
    public static class ClientOddsCalculator
    {
        // False when the selection cannot be priced (suspended or no odds): nothing to compare, no alert.
        public static bool TryGetClientOdds(InternalMarketMessage market, InternalSelectionMessage selection,
            string clientCode, bool isPlayerPropMarket, out decimal clientOdds)
        {
            clientOdds = 0m;

            if (market?.Se == null || selection == null || !selection.IsAc || selection.Od <= 0m)
                return false;

            // GM-810 in the websocket service: a market with no margin for this client is published with its
            // source odds, so that is the price the client sees.
            if (!TryGetClientMargin(market.ClCoMa, clientCode, out var targetMargin))
            {
                clientOdds = selection.Od;
                return true;
            }

            var selections = market.Se;

            var activeSelections = selections.Where(s => s.IsAc && s.Od > 0m).ToList();
            if (activeSelections.Count == 1)
                activeSelections = selections.Where(s => s.Od > 0m).ToList();

            var margin = (Math.Round(activeSelections.Sum(s => 100 / s.Od)) - 100) / 100;

            var activeSelectionCount = activeSelections.Count;
            if (activeSelectionCount == 1)
                activeSelectionCount = selections.Count;

            if (isPlayerPropMarket && selection.SeTeId != "12" && selection.SeTeId != "13")
            {
                clientOdds = Math.Round(selection.Od - (selection.Od * targetMargin), 2);
                return true;
            }

            if (margin == targetMargin)
            {
                clientOdds = selection.Od;
                return true;
            }

            var fairOddsInitialDivideBy = activeSelectionCount - (margin * selection.Od);
            if (fairOddsInitialDivideBy <= 0)
            {
                clientOdds = GetMasterOddsWithAddedMargin(selection.Od);
                return true;
            }

            var fairOdds = (activeSelectionCount * selection.Od) / fairOddsInitialDivideBy;
            if (fairOdds <= 0)
            {
                clientOdds = GetMasterOddsWithAddedMargin(selection.Od);
                return true;
            }

            var finalOddsInitialDivideBy = ((fairOdds * targetMargin) / activeSelectionCount) + 1;
            if (finalOddsInitialDivideBy <= 0)
            {
                clientOdds = GetMasterOddsWithAddedMargin(selection.Od);
                return true;
            }

            var odds = Math.Round(fairOdds / finalOddsInitialDivideBy, 2);

            if (odds < 1.00m)
            {
                clientOdds = GetMasterOddsWithAddedMargin(1m);
                return true;
            }

            if (odds >= selection.Od)
                odds = GetMasterOddsWithAddedMargin(selection.Od);

            clientOdds = odds;
            return true;
        }

        // ClCoMa is "CLIENT:margin|CLIENT:margin", the margin being the last segment of its entry.
        public static bool TryGetClientMargin(string clientMarketConfig, string clientCode, out decimal margin)
        {
            margin = 0m;

            if (string.IsNullOrEmpty(clientMarketConfig))
                return false;

            foreach (var entry in clientMarketConfig.Split('|'))
            {
                var parts = entry.Split(':');

                if (parts.Length < 2 || !string.Equals(parts[0], clientCode, StringComparison.Ordinal))
                    continue;

                if (decimal.TryParse(parts[^1], NumberStyles.Number, CultureInfo.InvariantCulture, out margin))
                    return true;
            }

            margin = 0m;
            return false;
        }

        public static decimal GetMasterOddsWithAddedMargin(decimal odds)
        {
            if (Math.Round(odds, 2) <= 1.01m)
                return odds;

            return Math.Round(100 / ((100 / odds) + 2m), 2);
        }
    }
}

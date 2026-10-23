namespace OE.Job.BoostedOddsCap.Evaluation
{
    public static class CapMath
    {
        // How far the boosted odds sit above the client's current odds, as a percentage of their sum. This is
        // the formula of the v1 OE.BoostedOddsCap job, kept so both read the same Cap % the Boosted Settings
        // page shows. It is zero when the two are equal and negative once the market has passed the boost.
        public static bool TryGetGapPercent(decimal boostedOdds, decimal currentOdds, out decimal gapPercent)
        {
            gapPercent = 0m;

            var sum = boostedOdds + currentOdds;
            if (sum <= 0m)
                return false;

            gapPercent = (boostedOdds - currentOdds) / sum * 100m;
            return true;
        }
    }
}

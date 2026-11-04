namespace OE.Job.BoostedOddsCap.Boosts
{
    // The Trading tables and the odds messages disagree on padding and case (BoostedSettings.ClientCode is
    // trimmed by the v1 job, the engine publishes client codes in upper case, specifiers are null or empty
    // for the same "no specifier"). Everything that is compared goes through these two helpers first.
    public static class BoostText
    {
        public static string Code(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

        public static string Specifier(string value) => (value ?? string.Empty).Trim();

        // bet-genius tells its markets apart by an id of their own, not by a specifier: every market message carries
        // "bg:market:{PrMaId}" as its MaSp (BG.Receiver.Service OddsFeedRepository), and the selection specifier
        // "bg:{PrMaId}:{PrSeId}" carries the same id. Mongo keeps no market specifier, and Backoffice reads the market
        // from Mongo, so a boost on such a market is saved with an empty one.
        public static bool IsBetGeniusMarketSpecifier(string specifier)
        {
            return specifier != null && specifier.StartsWith("bg:market:", StringComparison.Ordinal);
        }
    }
}

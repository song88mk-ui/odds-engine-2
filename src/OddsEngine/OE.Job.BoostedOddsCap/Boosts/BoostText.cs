namespace OE.Job.BoostedOddsCap.Boosts
{
    // The Trading tables and the odds messages disagree on padding and case (BoostedSettings.ClientCode is
    // trimmed by the v1 job, the engine publishes client codes in upper case, specifiers are null or empty
    // for the same "no specifier"). Everything that is compared goes through these two helpers first.
    public static class BoostText
    {
        public static string Code(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

        public static string Specifier(string value) => (value ?? string.Empty).Trim();
    }
}

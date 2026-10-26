namespace OE.Job.BoostedOddsCap.Boosts
{
    // One boosted selection of one client, joined with that client's cap. Immutable: a snapshot is shared
    // with the consumer thread while the refresher builds the next one.
    public sealed class BoostRule
    {
        public string Key { get; init; }
        public string ClientCode { get; init; }
        public string ProviderFixtureId { get; init; }
        public string MarketTemplateId { get; init; }
        public string MarketSpecifier { get; init; }
        public string SelectionTemplateId { get; init; }
        public string SelectionSpecifier { get; init; }
        public decimal BoostedOdds { get; init; }

        // The client's cap in percent (BoostedSettings stores a fraction).
        public decimal CapPercent { get; init; }

        public static string BuildKey(string clientCode, string providerFixtureId, string marketTemplateId,
            string marketSpecifier, string selectionTemplateId, string selectionSpecifier)
        {
            return string.Join("|", clientCode, providerFixtureId, marketTemplateId, marketSpecifier,
                    selectionTemplateId, selectionSpecifier);
        }
    }
}

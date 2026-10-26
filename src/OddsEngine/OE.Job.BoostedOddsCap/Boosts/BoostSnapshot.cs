using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Boosts
{
    // Everything the odds consumer needs, indexed so a message is judged with one dictionary lookup and no
    // I/O: the boost rules by fixture, and the email recipients by client. Built off the consumer thread by
    // the refresher and swapped in whole, so the consumer never sees a half-built index.
    public sealed class BoostSnapshot
    {
        private static readonly BoostRule[] NoRules = new BoostRule[0];
        private static readonly string[] NoRecipients = new string[0];

        private readonly Dictionary<string, BoostRule[]> _rulesByFixture;
        private readonly Dictionary<string, string[]> _recipientsByClient;

        public static readonly BoostSnapshot Empty = new BoostSnapshot(
            new Dictionary<string, BoostRule[]>(), new Dictionary<string, string[]>(), 0, 0, DateTime.MinValue);

        private BoostSnapshot(Dictionary<string, BoostRule[]> rulesByFixture,
            Dictionary<string, string[]> recipientsByClient, int ruleCount, int rowsWithoutCap, DateTime loadedAtUtc)
        {
            _rulesByFixture = rulesByFixture;
            _recipientsByClient = recipientsByClient;
            RuleCount = ruleCount;
            RowsWithoutCap = rowsWithoutCap;
            LoadedAtUtc = loadedAtUtc;
        }

        public int RuleCount { get; }

        // Boost rows skipped because their client has no Boosted Settings row, hence no cap to compare with.
        public int RowsWithoutCap { get; }

        public DateTime LoadedAtUtc { get; }

        public int FixtureCount => _rulesByFixture.Count;

        public IEnumerable<string> RuleKeys => _rulesByFixture.Values.SelectMany(rules => rules.Select(r => r.Key));

        public bool TryGetRules(string providerFixtureId, out BoostRule[] rules)
        {
            if (providerFixtureId != null && _rulesByFixture.TryGetValue(providerFixtureId, out rules))
                return true;

            rules = NoRules;
            return false;
        }

        public string[] RecipientsFor(string clientCode)
        {
            return _recipientsByClient.TryGetValue(BoostText.Code(clientCode), out var recipients) ? recipients : NoRecipients;
        }

        // recipientsByClient is what RecipientRules.Build made of the Backoffice clients; the codes are normalised
        // again here so a caller that built it by hand cannot miss a client on case or padding.
        public static BoostSnapshot Build(IEnumerable<PriceBoostRow> boosts, IEnumerable<BoostedSettingsRow> settings,
            IReadOnlyDictionary<string, string[]> recipientsByClient, DateTime loadedAtUtc)
        {
            var capPercentByClient = new Dictionary<string, decimal>();
            foreach (var row in settings)
            {
                var code = BoostText.Code(row.ClientCode);
                if (code.Length > 0 && row.Cap != null)
                    capPercentByClient.TryAdd(code, row.Cap.Value * 100m);
            }

            var normalisedRecipients = new Dictionary<string, string[]>();
            foreach (var pair in recipientsByClient)
            {
                var code = BoostText.Code(pair.Key);
                if (code.Length > 0 && pair.Value != null && pair.Value.Length > 0)
                    normalisedRecipients.TryAdd(code, pair.Value);
            }

            var seen = new HashSet<string>();
            var rulesByFixture = new Dictionary<string, List<BoostRule>>();
            var ruleCount = 0;
            var rowsWithoutCap = 0;

            foreach (var row in boosts)
            {
                if (string.IsNullOrWhiteSpace(row.ProviderFixtureId) || row.BoostedOdds == null || row.BoostedOdds <= 0m)
                    continue;

                var clientCode = BoostText.Code(row.ClientCode);
                if (clientCode.Length == 0)
                    continue;

                if (!capPercentByClient.TryGetValue(clientCode, out var capPercent))
                {
                    rowsWithoutCap++;
                    continue;
                }

                var fixtureId = row.ProviderFixtureId.Trim();
                var marketSpecifier = BoostText.Specifier(row.MarketSpecifier);
                var selectionSpecifier = BoostText.Specifier(row.SelectionSpecifier);
                var key = BoostRule.BuildKey(clientCode, fixtureId, row.MarketTemplateId, marketSpecifier,
                        row.SelectionTemplateId, selectionSpecifier);

                // the table has no primary key, so an exact duplicate is possible and must not alert twice
                if (!seen.Add(key))
                    continue;

                if (!rulesByFixture.TryGetValue(fixtureId, out var rules))
                    rulesByFixture[fixtureId] = rules = new List<BoostRule>();

                rules.Add(new BoostRule
                {
                    Key = key,
                    ClientCode = clientCode,
                    ProviderFixtureId = fixtureId,
                    MarketTemplateId = row.MarketTemplateId,
                    MarketSpecifier = marketSpecifier,
                    SelectionTemplateId = row.SelectionTemplateId,
                    SelectionSpecifier = selectionSpecifier,
                    BoostedOdds = row.BoostedOdds.Value,
                    CapPercent = capPercent
                });
                ruleCount++;
            }

            return new BoostSnapshot(
                rulesByFixture.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()),
                normalisedRecipients, ruleCount, rowsWithoutCap, loadedAtUtc);
        }
    }
}

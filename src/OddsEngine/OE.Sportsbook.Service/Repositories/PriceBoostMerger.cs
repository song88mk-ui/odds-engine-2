using OE.Shared.Data.Sportsbook.MongoDB.Collections;
using OE.Shared.Messages.OutgoingAdapterMessages;
using System.Globalization;

namespace OE.Sportsbook.Service.Repositories
{
    // The boost state of one selection document after one OnPriceBoost message, and the fixture and
    // market flags derived from it. Pure, so the rules are tested without Mongo.
    //
    // ClBoOd is "CODE:odds|CODE:odds", one entry per boosting client, sorted. The sportsbook site reads
    // exactly this format (Platform.Sportsbook.API GetClientBoostedOdds splits on '|' then ':'), so it
    // is part of the contract with platform-final.
    internal sealed class PriceBoostSelection
    {
        public string ClBoOd { get; init; } = string.Empty;
        public List<ClientBoostedSelectionNameMDB> ClBoSeNa { get; init; } = new();
        public List<ClientBoostedSelectionNameTranslationMDB> ClBoSeNaTr { get; init; } = new();
    }

    internal static class PriceBoostMerger
    {
        private const char ENTRY_SEPARATOR = '|';
        private const char KEY_SEPARATOR = ':';

        // Replaces the message client's boost on the selection: its old entry, name and translations are
        // dropped, and, unless the boost is being removed, the new ones are added. The other clients' are
        // kept. A client is matched on its whole code, not on a part of it: G0 must not touch G01.
        public static PriceBoostSelection Merge(Selection current, OnPriceBoostMessage message)
        {
            var clientCode = message.ClientCode ?? string.Empty;
            var isAdding = message.IsBoosted && message.BoostedOdds > 0;

            var odds = SplitEntries(current?.ClBoOd)
                .Where(entry => !IsClient(entry, clientCode))
                .ToList();

            var names = (current?.ClBoSeNa ?? new List<ClientBoostedSelectionNameMDB>())
                .Where(name => !string.Equals(name.ClCo, clientCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var translations = (current?.ClBoSeNaTr ?? new List<ClientBoostedSelectionNameTranslationMDB>())
                .Where(translation => !IsClient(translation.ClCoLaCo, clientCode))
                .ToList();

            if (isAdding)
            {
                odds.Add($"{clientCode}{KEY_SEPARATOR}{message.BoostedOdds.ToString(CultureInfo.InvariantCulture)}");

                names.AddRange((message.ClientBoostedSelectionNames ?? new List<ClientBoostedSelectionNameMessage>())
                    .Where(name => string.Equals(name.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase))
                    .Select(name => new ClientBoostedSelectionNameMDB { ClCo = name.ClientCode, BoSeNa = name.BoostedSelectionName }));

                translations.AddRange((message.ClientBoostedSelectionNameTranslations ?? new List<ClientBoostedSelectionTranslationMessage>())
                    .Where(translation => string.Equals(translation.ClientCode, clientCode, StringComparison.OrdinalIgnoreCase))
                    .Select(translation => new ClientBoostedSelectionNameTranslationMDB
                    {
                        ClCoLaCo = $"{translation.ClientCode}{KEY_SEPARATOR}{translation.LanguageCode}",
                        BoSeNa = translation.BoostedSelectionName
                    }));
            }

            return new PriceBoostSelection
            {
                ClBoOd = string.Join(ENTRY_SEPARATOR, odds.OrderBy(entry => entry, StringComparer.Ordinal)),
                ClBoSeNa = names,
                ClBoSeNaTr = translations
            };
        }

        // The IsBo / ClBo of a market or a fixture, from the ClBoOd of the selections under it: boosted
        // when any of them still carries a boost, ClBo the "|"-joined codes of the clients that do. Derived
        // rather than taken from the message, so a flag always matches the selections the site will read.
        public static (bool IsBo, string ClBo) Flags(IEnumerable<string> selectionClBoOds)
        {
            var clients = selectionClBoOds
                .SelectMany(SplitEntries)
                .Select(entry => entry.Split(KEY_SEPARATOR)[0])
                .Where(code => code.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToList();

            return (clients.Count > 0, string.Join(ENTRY_SEPARATOR, clients));
        }

        // An entry without a ':' is dropped: the site would throw on it reading the client's odds.
        private static IEnumerable<string> SplitEntries(string clBoOd)
        {
            if (string.IsNullOrWhiteSpace(clBoOd))
                return Enumerable.Empty<string>();

            return clBoOd.Split(ENTRY_SEPARATOR, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(entry => entry.Contains(KEY_SEPARATOR));
        }

        private static bool IsClient(string entry, string clientCode)
        {
            if (string.IsNullOrEmpty(entry))
                return false;

            var separator = entry.IndexOf(KEY_SEPARATOR);

            return separator > 0
                && string.Equals(entry.Substring(0, separator), clientCode, StringComparison.OrdinalIgnoreCase);
        }
    }
}

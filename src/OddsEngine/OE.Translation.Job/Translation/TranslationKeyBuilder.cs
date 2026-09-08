using System.Collections.Generic;
using System.Linq;
using OE.Translation.Job.Messages;

namespace OE.Translation.Job.Translation
{
    // Pure logic for turning an accepted bet placement message into the set of TranslationCache2
    // STRING_ID keys (market / selection template) that must be protected from cleanup.
    // Kept free of EF/RabbitMQ so it can be unit tested.
    //
    // Only template keys are flagged: FixtureId/MarketId/SelectionId don't map onto the
    // MaTe:/SeTe:/Te: key space, and MarketTemplateId/SelectionTemplateId (which do) are the only
    // ids the bet placement message carries that match it. There is no team id on this message, so
    // Te:{teamId} rows remain unprotected by this flag (see bet-genius
    // docs/TranslationTemplateGlossary.md) -- mitigated by the liveness-touch + retention window
    // in the seeding path instead.
    public static class TranslationKeyBuilder
    {
        // STRING_ID prefixes used by the seeding writer (OE.Translation.Service).
        public const string MarketTemplatePrefix = "MaTe";
        public const string SelectionTemplatePrefix = "SeTe";

        public static IReadOnlyList<string> Extract(OnBetPlacementMessage? message)
        {
            var keys = new HashSet<string>();

            if (message?.BetSelections == null)
                return keys.ToList();

            foreach (var item in message.BetSelections)
            {
                var selection = item?.BetSelection;

                if (selection == null)
                    continue;

                if (string.IsNullOrWhiteSpace(selection.MarketTemplateId))
                    continue;

                keys.Add($"{MarketTemplatePrefix}:{selection.MarketTemplateId}");

                if (!string.IsNullOrWhiteSpace(selection.SelectionTemplateId))
                    keys.Add($"{SelectionTemplatePrefix}:{selection.MarketTemplateId}:{selection.SelectionTemplateId}");
            }

            return keys.ToList();
        }
    }
}

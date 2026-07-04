using System.Collections.Generic;

namespace OE.Translation.Job.Messages
{
    // Minimal projection of platform-final's OnBetPlacement message
    // (Platform.Shared.MessageModels.OnBetPlacement). Only the identifiers needed to
    // build the translation String_ID keys are deserialized; everything else is ignored.
    // Consumed from r.bet.placement (every persisted placement, accepted or later rejected),
    // MessageType: "OnBetPlacement". Migrated from bet-genius BG.Shared (2026-07
    // translation-flow redesign).
    public class OnBetPlacementMessage
    {
        public List<BetSelectionMessageExtension> BetSelections { get; set; } = new List<BetSelectionMessageExtension>();
    }

    public class BetSelectionMessageExtension
    {
        public BetSelectionMessage BetSelection { get; set; }
    }

    public class BetSelectionMessage
    {
        public long FixtureId { get; set; }
        public long MarketId { get; set; }
        public long SelectionId { get; set; }

        // Verified present on Platform.Shared.Data.Betting.Entities.BetSelection (MarketTemplateId /
        // SelectionTemplateId), needed to build the TranslationCache2 MaTe:/SeTe: protection keys —
        // entity ids (MarketId/SelectionId) don't map onto that table's template/team key space.
        public string MarketTemplateId { get; set; }
        public string SelectionTemplateId { get; set; }
    }
}

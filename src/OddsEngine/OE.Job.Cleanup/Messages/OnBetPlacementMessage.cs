using System.Collections.Generic;

namespace OE.Job.Cleanup.Messages
{
    // Minimal projection of platform-final's OnBetPlacement message
    // (Platform.Shared.MessageModels.OnBetPlacement). Only the fixture id is needed here —
    // it marks the fixture as having bet history so the cleanup never deletes it.
    // Consumed from r.bet.placement (every persisted placement, accepted or later rejected),
    // MessageType: "OnBetPlacement". Same projection approach as OE.Translation.Job.
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
        // Captured by Betting.API from the live Mongo Market at placement time — same id space
        // as Sportsbook Fixture.FiId.
        public long FixtureId { get; set; }
    }
}

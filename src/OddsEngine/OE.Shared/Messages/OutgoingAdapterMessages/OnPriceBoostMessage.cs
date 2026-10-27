using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    // One client's price boost on one selection, as Backoffice publishes it (r.uof.onpriceboost) and
    // OE.Adapter.Service forwards it, unchanged, to the OE.Sportsbook.Service shard of the fixture
    // (r.oe.adapter.onpriceboost.{NN}). The property names are Backoffice.Shared.Models.OnPriceBoostMessage's
    // JSON contract: renaming one here silently drops that field of every boost.
    //
    // BoostedOdds 0, or IsBoosted false, removes the client's boost from the selection. IsBoosted,
    // ClientBoosted and ClientMarketBoosted are Backoffice's view AFTER the change (whether any client still
    // boosts the fixture, and the "|"-joined codes of the clients that boost the fixture and the market
    // template). OE.Sportsbook.Service does not copy them: it derives the fixture and market flags from the
    // boosts the selections hold, so a flag can never claim a boost the site has nothing to show for.
    public class OnPriceBoostMessage
    {
        public string ClientCode { get; set; }
        public int SportId { get; set; }
        public int SegmentId { get; set; }
        public int LeagueId { get; set; }
        public int FixtureTypeId { get; set; }
        public string ProviderFixtureId { get; set; }
        public string MarketTemplateId { get; set; }
        public string SelectionTemplateId { get; set; }
        public string SelectionSpecifier { get; set; }
        public decimal BoostedOdds { get; set; }
        public bool IsBoosted { get; set; }
        public string ClientBoosted { get; set; }
        public string ClientMarketBoosted { get; set; }
        public List<ClientBoostedSelectionNameMessage> ClientBoostedSelectionNames { get; set; } = new List<ClientBoostedSelectionNameMessage>();
        public List<ClientBoostedSelectionTranslationMessage> ClientBoostedSelectionNameTranslations { get; set; } = new List<ClientBoostedSelectionTranslationMessage>();
    }

    public class ClientBoostedSelectionNameMessage
    {
        public string ClientCode { get; set; }
        public string BoostedSelectionName { get; set; }
    }

    public class ClientBoostedSelectionTranslationMessage
    {
        public string LanguageCode { get; set; }
        public string ClientCode { get; set; }
        public string BoostedSelectionName { get; set; }
    }
}

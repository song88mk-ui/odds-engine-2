using OE.Shared.Models;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnMarketChangeMessage : OnFixtureChangeMessage
    {
        public List<MarketMessage> Ma { get; set; } = new List<MarketMessage>();
    }

    public class MarketMessage
    {
        public string Id => $"{ChTyId}:{PrFiId}:{PrMaId}";
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public string PrMaId { get; set; }
        public long MaId { get; set; }
        public string MaTeId { get; set; }
        public string Li { get; set; }
        public bool IsHa { get; set; } // IsHandicap — needed downstream to recompose selection names from templates + line
        public int MaStId { get; set; }
        public string MaStNa { get; set; }
        public int CaStId { get; set; }
        public string CaStNa { get; set; }
        public string MaSp { get; set; }
        public string MaNa { get; set; }
        public string MaTeNa { get; set; }
        // Market template translations (TranslationCache2 key "MaTe:{MaTeId}"); null/empty ->
        // fall back to MaTeNa. See OnFixtureChangeMessage translation lists.
        public List<TranslationMessage> MaTeTr { get; set; }
        // Vendor market-level CompetitorOne (2026-07 player-markets feature): present = this is a
        // per-player prop market for that competitor; joined against the fixture roster
        // (OnFixtureChangeMessage.Pl) to label the market. Null for regular markets. Mirrors the
        // selection-level CompId precedent below; name shared with bet-genius — never rename.
        public int? CompId { get; set; }
        public int SoOr { get; set; }
        public string ClCo { get; set; } = string.Empty;
        public string ClCoMa { get; set; } = string.Empty;
        public bool InPl { get; set; } = false;
        public List<SelectionMessage> Se { get; set; } = new List<SelectionMessage>();
    }

    public class SelectionMessage
    {
        public string Id => $"{ChTyId}:{PrFiId}:{PrMaId}:{PrSeId}";
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public string PrMaId { get; set; }
        public string PrSeId { get; set; }
        public string MaTeId { get; set; }
        public string SeTeId { get; set; }
        public string SeTeNa { get; set; } // Selection template name — source text for translation lookup
        // Vendor competitor id for competitor-specific selections (e.g. a named player/team
        // outcome). Null for generic templated outcomes (Over/Under, Home/Draw/Away).
        public int? CompId { get; set; }
        // Translation of SeNa (the display name), regardless of source: looked up via
        // "Co:{CompId}" when CompId is not null, else "SeTe:{MaTeId}:{SeTeId}" (mutually
        // exclusive). null/empty -> fall back to SeNa. See OnFixtureChangeMessage translation lists.
        public List<TranslationMessage> SeNaTr { get; set; }
        public string MaSp { get; set; }
        public long SeId { get; set; }
        public decimal Od { get; set; }
        public double Pr { get; set; }
        public double WoPr { get; set; }
        public double LoPr { get; set; }
        public double VoPr { get; set; }
        public double HaWoPr { get; set; }
        public double HaLoPr { get; set; }
        public bool IsAc { get; set; }
        public string SeSp { get; set; }
        public string SeNa { get; set; }
        public int SoOr { get; set; }
        public int SeStId { get; set; }
        public List<ClientOddsMessage> ClOd { get; set; } = new List<ClientOddsMessage>();
    }

    public class ClientOddsMessage
    {
        public string ClCo { get; set; }
        public decimal DeOd { get; set; }
        public decimal MaOd { get; set; }
        public decimal HoOd { get; set; }
        public decimal InOd { get; set; }
        public string SeTeId { get; set; }
        public long SeId { get; set; }
    }
}

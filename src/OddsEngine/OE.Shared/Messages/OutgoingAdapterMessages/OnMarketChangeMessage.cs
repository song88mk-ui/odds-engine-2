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
        public int MaStId { get; set; }
        public string MaStNa { get; set; }
        public int CaStId { get; set; }
        public string CaStNa { get; set; }
        public string MaSp { get; set; }
        public string MaNa { get; set; }
        public int SoOr { get; set; }
        public string ClCo { get; set; }
        public string ClCoMa { get; set; }
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

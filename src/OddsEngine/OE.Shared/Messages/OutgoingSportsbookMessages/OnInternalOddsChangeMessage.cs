using System.Collections.Generic;
using System;
using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Shared.Messages.OutgoingSportsbookMessages
{
    public class OnInternalOddsChangeMessage
    {
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public long FiId { get; set; }
        public string PrFiId { get; set; }
        public string FiNa { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
        public string FiStNa { get; set; }
        public string Sc { get; set; }
        public string FiSp { get; set; }
        public bool IsOfLi { get; set; }
        public int HoReCa { get; set; }
        public int AwReCa { get; set; }
        public int CuPe { get; set; }
        public int CuMi { get; set; }
        public int FiTyId { get; set; }
        public string FiTyNa { get; set; }
        public int ToTi { get; set; }
        public string Fa { get; set; }
        public int SoOr { get; set; }
        public string ClCo { get; set; }
        public string CoId { get; set; }
        public bool IsSu { get; set; }
        public bool? IsBo { get; set; }
        //public List<SportTranslation> SportTranslations { get; set; }
        //public List<SegmentTranslation> SegmentTranslations { get; set; }
        //public List<LeagueTranslation> LeagueTranslations { get; set; }
        //public List<FixtureTranslation> FixtureTranslations { get; set; }
        public List<InternalMarketMessage> Ma { get; set; } = new List<InternalMarketMessage>();
        public string HoId { get; set; }
        public string AwId { get; set; }
        //public List<InternalPeriodScore> PeriodScores { get; set; }
        public int CuSe { get; set; }
        public int CuSer { get; set; }
        public long TiSe { get; set; }
        public long TiRe { get; set; }
    }

    public class InternalMarketMessage
    {
        public string PrFiId { get; set; }
        public long MaId { get; set; }
        public string MaTeId { get; set; }
        public string PrMaId { get; set; }
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
        public bool InPl { get; set; }
        //public List<MarketTranslation> MarketTranslations { get; set; }
        public List<InternalSelectionMessage> Se { get; set; } = new List<InternalSelectionMessage>();
    }

    public class InternalSelectionMessage
    {
        public string PrFiId { get; set; }
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
        public int SeStId { get; set; }
        public List<ClientOddsMessage> ClOd { get; set; }
        //public List<SelectionTranslation> SelectionTranslations { get; set; }
    }

}

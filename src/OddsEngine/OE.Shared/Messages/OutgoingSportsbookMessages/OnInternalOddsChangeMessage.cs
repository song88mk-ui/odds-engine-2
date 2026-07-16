using System.Collections.Generic;
using System;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Models;

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
        public string StUr { get; set; }

        public List<InternalMarketMessage> Ma { get; set; } = new List<InternalMarketMessage>();
        public string HoId { get; set; }
        public string AwId { get; set; }
        //public List<InternalPeriodScore> PeriodScores { get; set; }
        public int CuSe { get; set; }
        public int CuSer { get; set; }
        public long TiSe { get; set; }
        public long TiRe { get; set; }

        // Translation lists — property names intentionally match OnFixtureChangeMessage because
        // OE.Websocket.Service deserializes ON_ODDS_CHANGE as OnMarketChangeMessage. Only set when
        // the translation changed since the last publish (change-detection gate in
        // OE.Sportsbook.Service); null means "unchanged or none" and consumers keep what they have.
        public List<TranslationMessage> SpTr { get; set; }
        public List<TranslationMessage> SegTr { get; set; }
        public List<TranslationMessage> LeTr { get; set; }
        public List<TranslationMessage> HoTr { get; set; }
        public List<TranslationMessage> AwTr { get; set; }

        // Player roster — property name intentionally matches OnFixtureChangeMessage.Pl because
        // OE.Websocket.Service deserializes ON_ODDS_CHANGE as OnMarketChangeMessage. Null means
        // "unchanged or none" and consumers keep the roster they already have.
        public List<PlayerMessage> Pl { get; set; }
    }

    public class InternalMarketMessage
    {
        public string PrFiId { get; set; }
        public long MaId { get; set; }
        public string MaTeId { get; set; }
        public string PrMaId { get; set; }
        public string Li { get; set; }
        public bool IsHa { get; set; } // IsHandicap — needed downstream to recompose translated selection names from templates + line
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
        // See OnInternalOddsChangeMessage translation lists — name matches MarketMessage.MaTeTr.
        public List<TranslationMessage> MaTeTr { get; set; }
        // Per-player prop-market competitor id — name matches MarketMessage.CompId (the websocket
        // deserializes ON_ODDS_CHANGE as OnMarketChangeMessage). See MarketMessage.CompId.
        public int? CompId { get; set; }

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
        // Vendor competitor id for competitor-specific selections — see SelectionMessage.CompId.
        public int? CompId { get; set; }
        // Translation of SeNa — see SelectionMessage.SeNaTr.
        public List<TranslationMessage> SeNaTr { get; set; }
    }

}

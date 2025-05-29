using OE.Shared.Messages.OutgoingReceiverMessages.Base;
using System;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingReceiverMessages
{
    public class OnMarketSetMessage : OutgoingReceiverMessageBase
    {
        public int FiTyId { get; set; }
        public int FiStId { get; set; }
        public string FiSp { get; set; }
        public DateTime FiStDa { get; set; }
        public string HoId { get; set; }
        public string AwId { get; set; }
        public List<MarketMessage> Ma { get; set; } = new List<MarketMessage>();
    }

    public class MarketMessage
    {
        public int MaId { get; set; }
        public string MaNa { get; set; }
        public int MaTyId { get; set; }
        public string MaTyNa { get; set; }
        public bool IsHa { get; set; }
        public bool HaSp { get; set; }
        public decimal? Ha { get; set; }
        public bool InPl { get; set; }
        public int CaSt { get; set; }
        public bool CaEn { get; set; }
        public int MaStId { get; set; }
        public List<SelectionMessage> Se { get; set; }
    }

    public class SelectionMessage
    {
        public int MaId { get; set; }
        public int SeId { get; set; }
        public int SeTeId { get; set; }
        public string SeNa { get; set; }
        public bool IsAc { get; set; }
        public int SeStId { get; set; }
        public decimal Od { get; set; }
    }
}

using OE.Shared.Messages.OutgoingReceiverMessages.Base;
using System;

namespace OE.Shared.Messages.OutgoingReceiverMessages
{
    public class OnFixtureSetMessage : OutgoingReceiverMessageBase
    {
        public int ChTyId { get; set; }
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public string HoId { get; set; }
        public string AwId { get; set; }
        public string FiNa { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
        public string FiSp { get; set; }
        public int FiTyId { get; set; }
        public string ClCo { get; set; } = string.Empty;
        public long TiSe { get; set; }
        public long TiRe { get; set; }
        public long TiDi { get; set; }
        public string Sc { get; set; }
        public bool IsSu { get; set; }
        public bool IsOfLi { get; set; }
    }
}

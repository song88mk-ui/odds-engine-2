using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using System;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnFixtureChangeMessage : OutgoingAdapterMessageBase
    {
        public string ClPr { get; set; }
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public string FiNa { get; set; }
        public DateTime FiStDa { get; set; }
        public int FiStId { get; set; }
        public string FiStNa { get; set; }
        public string HoId { get; set; }
        public string AwId { get; set; }
        public string Sc { get; set; }
        public string FiSp { get; set; }
        public bool IsOfLi { get; set; }
        public int HoReCa { get; set; }
        public int AwReCa { get; set; }
        public int CuPeId { get; set; }
        public string CuPe { get; set; }
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
        public int CuSec { get; set; }
        public int CuSer { get; set; }
    }
}

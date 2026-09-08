using System;
using System.Collections.Generic;

namespace OE.WebSocket.Service.Messages
{
    public class OutgoingBoostedOddsChange
    {
        public OutgoingBoostedOddsChange()
        {
            Gr = new List<string>();
        }
        public List<string> Gr { get; set; }
        public OddsChangeBoostedFixture Pa { get; set; }
    }
    public class OddsChangeBoostedFixture
    {
        public OddsChangeBoostedFixture()
        {
            InMa = new List<OutgoingBoostedMarket>();
            UpMa = new List<OutgoingBoostedMarket>();
        }
        public int SpId { get; set; }
        public string SpNa { get; set; }
        public int SegId { get; set; }
        public string SegNa { get; set; }
        public int LeId { get; set; }
        public string LeNa { get; set; }
        public string FiId { get; set; }
        public string PrFiId { get; set; }
        public string FiNa { get; set; }
        public string HoNa { get; set; }
        public string AwNa { get; set; }
        public DateTime FiStDa { get; set; }
        public bool IsOfLi { get; set; }
        public int FiStId { get; set; }
        public string FiStNa { get; set; }
        public int FiTyId { get; internal set; }
        public string FiTyNa { get; internal set; }
        public string Sc { get; set; }
        public string CuPe { get; set; }
        public int CuMi { get; set; }
        public int SoOr { get; set; }
        public int MaCo { get; set; }
        public List<OutgoingBoostedMarket> InMa { get; set; }
        public List<OutgoingBoostedMarket> UpMa { get; set; }
    }
    public class OutgoingBoostedMarket
    {
        public OutgoingBoostedMarket()
        {
            Se = new List<OutgoingBoostedSelection>();
        }
        public string MaTeId { get; set; }
        public string MaId { get; set; }
        public string Li { get; set; }
        public int MaStId { get; set; }
        public string MaStNa { get; set; }
        public string MaNa { get; set; }
        public int SoOr { get; set; }
        public bool IsIn { get; set; }
        public List<OutgoingBoostedSelection> Se { get; set; }
    }
    public class OutgoingBoostedSelection
    {
        public string SeTeId { get; set; }
        public string SeId { get; set; }
        public string SeNa { get; set; }
        public string DeOd { get; set; }
        public string BoOd { get; set; }
        public bool IsAc { get; set; }
        public int SoOr { get; set; }
    }
}

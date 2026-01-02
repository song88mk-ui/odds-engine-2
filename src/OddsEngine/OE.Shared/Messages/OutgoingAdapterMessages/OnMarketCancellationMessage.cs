using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnMarketCancellationMessage : OutgoingAdapterMessageBase
    {
        public OnMarketCancellationMessage()
        {

            Ma = new List<MarketBetCancellationMessage>();
        }
        public int SpId { get; set; }
        public List<MarketBetCancellationMessage> Ma { get; set; }
        public long TiSe { get; set; }
        public long TiRe { get; set; }
        public string Raw { get; set; }
    }
    public class MarketBetCancellationMessage
    {
        public string MaId { get; set; }
        public string MaTeId { get; set; } //TBR
        public string MaSp { get; set; } //TBR
        public string VoRe { get; set; }
        public long StTi { get; set; }
        public long EnTi { get; set; }
    }
}

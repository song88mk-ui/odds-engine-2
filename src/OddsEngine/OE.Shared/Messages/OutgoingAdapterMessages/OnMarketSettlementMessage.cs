using MongoDB.Bson.Serialization.Serializers;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using System;
using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    public class OnMarketSettlementMessage : OutgoingAdapterMessageBase
    {
        public List<MarketResult> Ma { get; set; }
    }

    public class MarketResult
    {
        public string Id => $"{ChTyId}:{PrFiId}:{PrMaId}";
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public string PrMaId { get; set; }
        public string MaSp { get; set; }
        public string MaTeId { get; set; }
        public int SpId { get; set; }
        public long MaId { get; set; }
        public int MaStId { get; set; } //MarketStatusId
        public string MaStNa { get; set; } //MarketStatusName
        public List<SelectionResult> Se { get; set; }
        public long TiSe { get; set; } //Time message sent
        public long TiRe { get; set; } //Time message receivec
    }

    public class SelectionResult
    {
        public string Id => $"{ChTyId}:{PrFiId}:{PrMaId}:{PrSeId}";
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public string PrMaId { get; set; }
        public string PrSeId { get; set; }
        public string SeTeId { get; set; }
        public long SeId { get; set; }
        public int SeStId { get; set; } // Selection Status Id
        public string SeStNa { get; set; } // Selection Status Id
    }
}

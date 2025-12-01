namespace OE.Shared.Messages.OutgoingAdapterMessages.Base
{
    public class OutgoingAdapterMessageBase
    {
        public string Id => $"{ChTyId}:{PrFiId}";
        public int ChTyId { get; set; }
        public string PrFiId { get; set; }
        public long FiId { get; set; }
        public string FiSp { get; set; }
        public long TiSe { get; set; }
        public long TiRe { get; set; }
    }
}

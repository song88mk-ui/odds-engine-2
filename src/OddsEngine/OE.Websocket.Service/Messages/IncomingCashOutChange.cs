namespace OE.WebSocket.Service.Messages
{
    public class IncomingCashOutChange
    {
        public string ClientId { get; set; }
        public string UserId { get; set; }
        public string BetId { get; set; }
        public CashOutStatus CashOutStatus { get; set; }
        public decimal CashOutStake { get; set; }
    }
    public enum CashOutStatus
    {
        Closed = -2,
        Unavailable = -1,
        Available = 1
    }
}

namespace OE.WebSocket.Service.Messages
{
    public class IncomingPayableChange
    {
        public string ClientId { get; set; }
        public string UserId { get; set; }
        public decimal Payable { get; set; }
    }
}

namespace OE.WebSocket.Service.Messages
{
    public class IncomingWalletChange
    {
        public string ClientId { get; set; }
        public string UserId { get; set; }
        public decimal Balance { get; set; }
    }
}

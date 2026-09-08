using OE.Shared.Enums;

namespace OE.WebSocket.Service.Messages
{
    public class IncomingBetStatus
    {
        public string ClientId { get; set; }
        public string UserId { get; set; }
        public string BetId { get; set; }
        public BetStatus BetStatus { get; set; }
        public string Message { get; set; }
    }
}

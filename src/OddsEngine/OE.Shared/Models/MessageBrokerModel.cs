namespace OE.Shared.Models
{
    public class MessageBrokerModel
    {
        public string Environment { get; set; }
        public string Host { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string VirtualHost { get; set; }
        public string Exchange { get; set; }
        public string Queue { get; set; }
        public string Consumer { get; set; }
        public string RoutingKey { get; set; }
        public string Port { get; set; }
    }
}
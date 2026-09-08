namespace OE.Job.KeepAlive.Messages
{
    public class OnHeartbeatMessage
    {
        public bool IsHealthy { get; set; }
        public long Timestamp { get; set; }
    }
}

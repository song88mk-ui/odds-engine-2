namespace OE.WebSocket.Service.Messages
{
    // Deliberately a bare signal, no flag values - see WebPropsRepository.cs (backoffice-final)
    // for why: platform-web re-fetches and compares on receipt instead of applying pushed values,
    // to avoid flicker from a burst of rapid saves replaying stale intermediate states.
    public class OutgoingWebPropsChange
    {
        public bool Ch { get; set; }
    }
}

using System.Text.Json.Serialization;
namespace OE.WebSocket.Service.Messages
{
    // BO-108: backoffice-final's OnWebPropsChangeMessage serializes this field as
    // "ClientCode" (System.Text.Json default naming, no camelCase policy), even though the
    // value is actually the client's Platform GUID, not the SBC client code - see
    // WebPropsRepository.cs (backoffice-final) for why. JsonPropertyName keeps that
    // wire-compatible binding while naming the property ClientId here, matching the other
    // Incoming* classes (IncomingBetStatus, IncomingCashOutChange, ...) and the ResolveClient
    // fallback it feeds.
    public class IncomingWebPropsChange
    {
        [JsonPropertyName("ClientCode")]
        public string ClientId { get; set; }
    }
}

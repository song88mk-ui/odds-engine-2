using OE.WebSocket.Service.Messages;
using System.Text.Json;
namespace OE.Websocket.Service.Tests
{
    // BO-108: Worker.cs deserializes every incoming message with default (case-sensitive,
    // exact-name) JsonSerializerOptions, and backoffice-final's OnWebPropsChangeMessage
    // serializes its GUID field as "ClientCode" on the wire, not "ClientId" like the other
    // Incoming* payload classes in this project. This pins the [JsonPropertyName] binding
    // that lets the C# property stay named ClientId (matching those other classes) while
    // still binding the actual wire key, so a change to either name fails loudly here
    // instead of silently dropping the fallback-by-GUID resolution again.
    public class IncomingWebPropsChangeTests
    {
        [Fact]
        public void Deserialize_BackofficeFinalWireShape_BindsClientId()
        {
            var payload = "{\"ClientCode\":\"087DFD95-7412-4AD4-924D-9498C4434790\"}";

            var result = JsonSerializer.Deserialize<IncomingWebPropsChange>(payload);

            Assert.Equal("087DFD95-7412-4AD4-924D-9498C4434790", result.ClientId);
        }
    }
}

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OE.Shared.Models.Provider.BetGenius
{
    public class BGEvents
    {
        [JsonPropertyName("data")]
        public List<BGEvent> Data { get; set; }
    }
}

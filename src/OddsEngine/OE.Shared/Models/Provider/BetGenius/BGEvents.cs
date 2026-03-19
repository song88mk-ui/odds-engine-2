using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OE.Shared.Models.Provider.BetGenius
{
    public class BGEvents
    {
        [JsonPropertyName("data")]
        public List<BGEvent> Data { get; set; }

        [JsonPropertyName("paging")]
        public BGPaging Paging { get; set; }
    }

    public class BGPaging
    {
        [JsonPropertyName("cursors")]
        public BGCursors Cursors { get; set; }

        [JsonPropertyName("previous")]
        public string Previous { get; set; }

        [JsonPropertyName("next")]
        public string Next { get; set; }
    }

    public class BGCursors
    {
        [JsonPropertyName("after")]
        public string After { get; set; }

        [JsonPropertyName("before")]
        public string Before { get; set; }
    }
}

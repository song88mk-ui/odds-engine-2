namespace OE.WebSocket.Service.Clients
{
    // One entry per sportsbook client (brand). Owns the client-scoped Platform API
    // HttpClient (x-client-id header) and the in-memory state that was previously
    // per-process when the service ran one deployment per client.
    public class ClientContext
    {
        public required string ClientId { get; init; }

        // Lowercase, matches the hub group naming ({event}:{clientCode}:...).
        public required string ClientCode { get; init; }

        public required HttpClient HttpClient { get; init; }

        // SportId -> main-market template ids, loaded per client from the Platform API.
        public List<Tuple<int, List<string>>> MainMarketTemplates { get; } = new();

        // Set once the client's template/sort-order data has been loaded. Odds
        // publishing skips clients that are not ready yet, so a freshly discovered
        // client never receives payloads built from empty templates.
        public volatile bool IsReady;
    }
}

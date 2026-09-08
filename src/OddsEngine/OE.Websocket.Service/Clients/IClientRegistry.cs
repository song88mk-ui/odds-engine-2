namespace OE.WebSocket.Service.Clients
{
    public interface IClientRegistry
    {
        // Snapshot of the clients this instance currently serves.
        IReadOnlyList<ClientContext> Clients { get; }

        // Resolves the owning client from a client-suffixed routing key
        // (e.g. r.bet.betstatus.gsa -> gsa). Returns null when the last segment
        // is not a known client code (e.g. the shared r.oe.sb.engine.* keys).
        ClientContext ResolveByRoutingKey(string routingKey);

        // Resolves by the ClientId GUID carried in bet/cashout/wallet/payable payloads.
        ClientContext ResolveByClientId(string clientId);

        // Reloads the client list from its source and returns the newly added
        // clients so the caller can run their startup loads.
        Task<IReadOnlyList<ClientContext>> RefreshAsync();
    }
}

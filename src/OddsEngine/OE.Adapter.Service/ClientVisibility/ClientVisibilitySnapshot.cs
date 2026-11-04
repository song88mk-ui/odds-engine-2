namespace OE.Adapter.Service.ClientVisibility
{
    // One league as Backoffice's Config page and the sportsbook league lookup identify it.
    public readonly record struct LeagueKey(int SportId, int SegmentId, int LeagueId);

    // A client that Backoffice saved as restricted for a league.
    public sealed class LeagueRestriction
    {
        public string ClientCode { get; init; }
        public LeagueKey League { get; init; }
    }

    // Which clients may see which league, worked out once per load so that stamping a message is a dictionary lookup.
    //
    // A league is visible to every client unless the client has a restriction for it (BO-139), so the clients of a
    // league are all the clients minus its restricted ones. Only leagues that have a restriction are held, every
    // other league shares the one list of all clients. A restricted client that is not in the client list is ignored,
    // it has nothing to be taken out of. The list is the "|"-joined, ordered, upper case client codes of the ClCo
    // column; it is empty only when every client is restricted.
    public sealed class ClientVisibilitySnapshot
    {
        private readonly string _allClients;
        private readonly Dictionary<LeagueKey, string> _restrictedLeagues;

        private ClientVisibilitySnapshot(string allClients, int clientCount, Dictionary<LeagueKey, string> restrictedLeagues)
        {
            _allClients = allClients;
            ClientCount = clientCount;
            _restrictedLeagues = restrictedLeagues;
        }

        public int ClientCount { get; }

        public int RestrictedLeagueCount => _restrictedLeagues.Count;

        public static ClientVisibilitySnapshot Build(IEnumerable<string> clientCodes, IEnumerable<LeagueRestriction> restrictions)
        {
            var all = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var clientCode in clientCodes ?? Enumerable.Empty<string>())
            {
                var code = Code(clientCode);
                if (code.Length > 0)
                    all.Add(code);
            }

            var restrictedByLeague = new Dictionary<LeagueKey, HashSet<string>>();
            foreach (var restriction in restrictions ?? Enumerable.Empty<LeagueRestriction>())
            {
                var code = Code(restriction?.ClientCode);
                if (code.Length == 0 || !all.Contains(code))
                    continue;

                if (!restrictedByLeague.TryGetValue(restriction.League, out var restricted))
                    restrictedByLeague[restriction.League] = restricted = new HashSet<string>(StringComparer.Ordinal);

                restricted.Add(code);
            }

            var restrictedLeagues = new Dictionary<LeagueKey, string>();
            foreach (var pair in restrictedByLeague)
                restrictedLeagues[pair.Key] = string.Join("|", all.Where(code => !pair.Value.Contains(code)));

            return new ClientVisibilitySnapshot(string.Join("|", all), all.Count, restrictedLeagues);
        }

        public string ClientCodesFor(LeagueKey league)
        {
            return _restrictedLeagues.TryGetValue(league, out var clientCodes) ? clientCodes : _allClients;
        }

        // The leagues whose list is not the one it was, among the leagues that have a restriction in either snapshot.
        // A client added to or removed from the client list changes the list of every league, which is not reported
        // here: the stored fixtures take the new client with their next change, like any other stamped value.
        public static IReadOnlyList<LeagueKey> ChangedLeagues(ClientVisibilitySnapshot previous, ClientVisibilitySnapshot next)
        {
            return previous._restrictedLeagues.Keys
                .Union(next._restrictedLeagues.Keys)
                .Where(league => !string.Equals(previous.ClientCodesFor(league), next.ClientCodesFor(league), StringComparison.Ordinal))
                .OrderBy(league => league.SportId).ThenBy(league => league.SegmentId).ThenBy(league => league.LeagueId)
                .ToList();
        }

        private static string Code(string clientCode) => (clientCode ?? string.Empty).Trim().ToUpperInvariant();
    }
}

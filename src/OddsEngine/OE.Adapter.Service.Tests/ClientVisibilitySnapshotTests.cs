using OE.Adapter.Service.ClientVisibility;

namespace OE.Adapter.Service.Tests
{
    // Which clients may see a league: every client Backoffice's Config page lists, minus the ones it restricted for that
    // league (BO-139). The list is the format of the ClCo column, "|"-joined and ordered, so the site's and the cap
    // job's string matching reads it.
    public class ClientVisibilitySnapshotTests
    {
        private static readonly LeagueKey League3 = new(1, 2, 3);
        private static readonly LeagueKey League4 = new(1, 2, 4);

        private static LeagueRestriction Restrict(string? clientCode, LeagueKey league) => new() { ClientCode = clientCode!, League = league };

        private static ClientVisibilitySnapshot Build(string?[] clientCodes, params LeagueRestriction[] restrictions)
            => ClientVisibilitySnapshot.Build(clientCodes!, restrictions);

        [Fact]
        public void ALeagueWithoutARestriction_IsVisibleToEveryClient_InOrder()
        {
            var snapshot = Build(new[] { "TSO", "G01", "S88" });

            Assert.Equal("G01|S88|TSO", snapshot.ClientCodesFor(League3));
        }

        [Fact]
        public void ARestrictedClient_IsTakenOutOfThatLeagueOnly()
        {
            var snapshot = Build(new[] { "G01", "S88", "TSO" }, Restrict("TSO", League3));

            Assert.Equal("G01|S88", snapshot.ClientCodesFor(League3));
            Assert.Equal("G01|S88|TSO", snapshot.ClientCodesFor(League4));
            Assert.Equal("G01|S88|TSO", snapshot.ClientCodesFor(new LeagueKey(9, 2, 3)));
        }

        [Fact]
        public void SeveralRestrictionsOfOneLeague_AreAllTakenOut()
        {
            var snapshot = Build(new[] { "G01", "S88", "TSO" }, Restrict("TSO", League3), Restrict("G01", League3));

            Assert.Equal("S88", snapshot.ClientCodesFor(League3));
        }

        [Fact]
        public void EveryClientRestricted_GivesAnEmptyList_NotNull()
        {
            var snapshot = Build(new[] { "G01", "TSO" }, Restrict("G01", League3), Restrict("TSO", League3));

            Assert.Equal(string.Empty, snapshot.ClientCodesFor(League3));
        }

        [Fact]
        public void ARestrictionOfAClientThatIsNotInTheClientList_IsIgnored()
        {
            var snapshot = Build(new[] { "G01", "TSO" }, Restrict("GONE", League3));

            Assert.Equal("G01|TSO", snapshot.ClientCodesFor(League3));
            Assert.Equal(0, snapshot.RestrictedLeagueCount);
        }

        [Fact]
        public void CodesAreTrimmedUpperCasedAndDeduplicated_BlankOnesDropped()
        {
            var snapshot = Build(new[] { " g01 ", "G01", "", null, "tso" }, Restrict("g01", League3));

            Assert.Equal("TSO", snapshot.ClientCodesFor(League3));
            Assert.Equal("G01|TSO", snapshot.ClientCodesFor(League4));
            Assert.Equal(2, snapshot.ClientCount);
        }

        [Fact]
        public void ARestrictionWithoutAClientCode_IsIgnored()
        {
            var snapshot = Build(new[] { "G01" }, Restrict(null, League3), Restrict("  ", League3));

            Assert.Equal("G01", snapshot.ClientCodesFor(League3));
        }

        [Fact]
        public void ChangedLeagues_ReportsAddedRemovedAndChangedRestrictions_InLeagueOrder()
        {
            var clients = new[] { "G01", "S88", "TSO" };
            var previous = Build(clients, Restrict("TSO", League3), Restrict("S88", League4));
            var next = Build(clients, Restrict("TSO", League3), Restrict("G01", League3), Restrict("S88", new LeagueKey(1, 2, 5)));

            // league 3 lost another client, league 4 is open again, league 5 is newly restricted
            Assert.Equal(new[] { League3, League4, new LeagueKey(1, 2, 5) }, ClientVisibilitySnapshot.ChangedLeagues(previous, next));
        }

        [Fact]
        public void ChangedLeagues_WhenNothingChanged_ReportsNothing()
        {
            var clients = new[] { "G01", "TSO" };

            Assert.Empty(ClientVisibilitySnapshot.ChangedLeagues(Build(clients, Restrict("TSO", League3)), Build(clients, Restrict("TSO", League3))));
        }

        [Fact]
        public void ChangedLeagues_ANewClient_IsReportedOnlyForLeaguesThatHaveARestriction()
        {
            var previous = Build(new[] { "G01", "TSO" }, Restrict("TSO", League3));
            var next = Build(new[] { "G01", "S88", "TSO" }, Restrict("TSO", League3));

            // league 4 has no restriction: its stored fixtures take the new client with their next change
            Assert.Equal(new[] { League3 }, ClientVisibilitySnapshot.ChangedLeagues(previous, next));
        }
    }
}

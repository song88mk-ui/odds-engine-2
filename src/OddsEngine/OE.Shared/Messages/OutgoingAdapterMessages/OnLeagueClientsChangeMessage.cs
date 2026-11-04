namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    // The clients allowed to see one league, as OE.Adapter.Service now works them out, sent to the one
    // OE.Sportsbook.Service shard that does league-wide writes (r.oe.adapter.onleagueclientschange.10). The shard
    // stamps ClientCodes onto the ClCo of every stored fixture and market of the league, so a saved restriction
    // reaches the documents without waiting for the next odds change of each fixture.
    //
    // ClientCodes is "|"-joined client codes, the format of the ClCo column. An empty string is a real value: no
    // client may see the league.
    public class OnLeagueClientsChangeMessage
    {
        public int SportId { get; set; }
        public int SegmentId { get; set; }
        public int LeagueId { get; set; }
        public string ClientCodes { get; set; }
    }
}

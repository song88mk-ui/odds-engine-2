namespace OE.Shared.Data.Trading.Entities
{
    // Backoffice's saved league setting of one client (Config page). A row says the league is assigned to the client,
    // and IsActive 0 means it is restricted: hidden for that client. A league without a row is visible. Read only
    // here: backoffice-final owns and writes Trading.dbo.ClientLeagueConfig (Backoffice.Shared SQLEntities Config.cs),
    // and only the columns the adapter needs are mapped.
    //
    // The properties mirror the real columns, which matters because EF reads them as they are: every column is NULL-able in
    // the V1 baseline (BO-140's V21 makes the key columns NOT NULL where it has been applied), and IsActive is a smallint,
    // not a bit, so a bool would throw on the first row (BO-151). What a NULL means is decided by the reader, not here.
    public class ClientLeagueConfig
    {
        public string ClientCode { get; set; }
        public int? SportId { get; set; }
        public int? SegmentId { get; set; }
        public int? LeagueId { get; set; }
        public short? IsActive { get; set; }
    }
}

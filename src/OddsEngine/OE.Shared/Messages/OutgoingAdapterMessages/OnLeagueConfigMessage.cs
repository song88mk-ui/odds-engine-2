using System;

namespace OE.Shared.Messages.OutgoingAdapterMessages
{
    // A league setting saved in Backoffice's Config page (r.uof.onleagueconfig). The property names are
    // Backoffice.Shared.OutgoingEngineMessages.OnLeagueConfigMessage's JSON contract.
    //
    // It only says which league changed: OE.Adapter.Service reads the saved settings from Trading.dbo.ClientLeagueConfig
    // again, so IsActive and the client are informational. Backoffice sends one message per league of a save, with the
    // client of the first row, not one per client.
    public class OnLeagueConfigMessage
    {
        public Guid ClientId { get; set; }
        public string ClientCode { get; set; }
        public int SportId { get; set; }
        public int SegmentId { get; set; }
        public int LeagueId { get; set; }
        public bool IsActive { get; set; }
    }
}

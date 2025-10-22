using System;

namespace OE.Shared.Data.Trading.Entities
{
    public class ClientOddsFeedLiveFixture
    {
        public Guid ClientId { get; set; }
        public int SportId { get; set; }
        public string FixtureSpecifier { get; set; }
        public DateTime FixtureStartDate { get; set; }
    }
}

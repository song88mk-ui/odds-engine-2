using System;

namespace OE.Shared.Data.Trading.Entities
{
    public class ClientOddsFeedConfiguration
    {
        public Guid ClientId { get; set; }
        public int SportId { get; set; }
        public bool PrematchOdds { get; set; }
        public bool LiveOdds { get; set; }
        public int? MaxFixtures { get; set; }
    }
}

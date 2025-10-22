namespace OE.Shared.Models
{
    public class ClientConfigurationModel
    {
        public int SportId { get; set; }
        public bool PrematchOdds { get; set; }
        public bool LiveOdds { get; set; }
        public int? MaxFixtures { get; set; }
    }
}


namespace OE.Shared.Models
{
    public class ClientOdds
    {
        public string ClientCode { get; set; }
        public decimal DecimalOdds { get; set; }
        public decimal MalaysianOdds { get; set; }
        public decimal HongkongOdds { get; set; }
        public decimal IndonesianOdds { get; set; }
        public string SelectionTemplateId { get; set; }
        public long SelectionId { get; set; }
    }
}

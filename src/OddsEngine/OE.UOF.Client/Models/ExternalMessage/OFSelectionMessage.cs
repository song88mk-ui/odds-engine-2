
namespace OE.UOF.Client.Models
{
    public class OFSelectionMessage
    {
        public OFSelectionMessage()
        {
            SelectionNameTranslations = new List<OFSelectionTranslation>();
        }

        public string SelectionTemplateId { get; set; }
        public string SelectionName { get; set; }
        public decimal Odds { get; set; }
        public double Probability { get; set; }
        public double VoidProbability { get; set; }
        public double WonProbability { get; set; }
        public double LostProbability { get; set; }
        public double HalfWonProbability { get; set; }
        public double HalfLostProbability { get; set; }
        public bool IsActive { get; set; }
        public string SelectionSpecifier { get; set; }
        public List<OFSelectionTranslation> SelectionNameTranslations { get; set; }
    }
}

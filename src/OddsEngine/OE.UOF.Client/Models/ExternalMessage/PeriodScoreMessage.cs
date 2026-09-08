namespace OE.UOF.Client.Models
{
    public class PeriodScoreMessage
    {
        public int Home { get; set; }
        public int Away { get; set; }
        public string MatchStatusCode { get; set; }
        public string PeriodType { get; set; }
    }
}

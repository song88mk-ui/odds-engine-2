namespace OE.Job.BoostedOddsCap.Evaluation
{
    // A boost whose current odds have come within its cap. Carries everything the toast and the email print, so
    // delivery never has to look anything up.
    public sealed class CapAlert
    {
        public string ClientCode { get; init; }
        public string ProviderFixtureId { get; init; }
        public int SportId { get; init; }
        public string SportName { get; init; }
        public string SegmentName { get; init; }
        public string LeagueName { get; init; }
        public string FixtureName { get; init; }
        public string MarketName { get; init; }
        public string SelectionName { get; init; }
        public decimal BoostedOdds { get; init; }
        public decimal CurrentOdds { get; init; }
        public decimal CapPercent { get; init; }
        public decimal GapPercent { get; init; }
        public DateTime DetectedAtUtc { get; init; }
    }
}

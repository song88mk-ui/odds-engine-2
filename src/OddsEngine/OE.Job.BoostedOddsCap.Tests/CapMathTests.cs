using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Tests
{
    public class CapMathTests
    {
        [Fact]
        public void TryGetGapPercent_IsTheDifferenceOverTheSumOfTheTwoOdds()
        {
            Assert.True(CapMath.TryGetGapPercent(2.10m, 2.00m, out var gap));

            // (2.10 - 2.00) / (2.10 + 2.00) * 100
            Assert.Equal(2.4390m, Math.Round(gap, 4));
        }

        [Fact]
        public void TryGetGapPercent_IsZeroWhenTheOddsAreEqual()
        {
            Assert.True(CapMath.TryGetGapPercent(2.00m, 2.00m, out var gap));
            Assert.Equal(0m, gap);
        }

        [Fact]
        public void TryGetGapPercent_IsNegativeOncetheMarketHasPassedTheBoost()
        {
            Assert.True(CapMath.TryGetGapPercent(2.00m, 2.10m, out var gap));
            Assert.Equal(-2.4390m, Math.Round(gap, 4));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 1)]
        public void TryGetGapPercent_RefusesAnUnusableSum(double boosted, double current)
        {
            Assert.False(CapMath.TryGetGapPercent((decimal)boosted, (decimal)current, out var gap));
            Assert.Equal(0m, gap);
        }
    }
}

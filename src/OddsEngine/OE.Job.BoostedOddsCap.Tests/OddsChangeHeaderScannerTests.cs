using System.Text;
using System.Text.Json;
using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Tests
{
    // The scanner decides, from a few tokens, whether a message is worth deserialising, so a miss here is a
    // silently dropped alert. These pin what it reads, what it must ignore, and that it still agrees with the
    // wire format of the real message class.
    public class OddsChangeHeaderScannerTests
    {
        private static bool Scan(string json, out string providerFixtureId, out int fixtureStatusId)
        {
            return OddsChangeHeaderScanner.TryRead(Encoding.UTF8.GetBytes(json), out providerFixtureId, out fixtureStatusId);
        }

        [Fact]
        public void TryRead_ReadsTheFixtureIdAndStatusOfTheRealMessage()
        {
            var message = TestMessages.OddsChange("sr:match:777", 0, TestMessages.EvenMarket(2.00m));
            var json = JsonSerializer.SerializeToUtf8Bytes(message);

            var found = OddsChangeHeaderScanner.TryRead(json, out var fixtureId, out var statusId);

            Assert.True(found);
            Assert.Equal("sr:match:777", fixtureId);
            Assert.Equal(0, statusId);
        }

        [Fact]
        public void TryRead_ReadsTheStatusOfALiveFixture()
        {
            var message = TestMessages.OddsChange("sr:match:777", 1);
            var json = JsonSerializer.SerializeToUtf8Bytes(message);

            Assert.True(OddsChangeHeaderScanner.TryRead(json, out _, out var statusId));
            Assert.Equal(1, statusId);
        }

        [Fact]
        public void TryRead_DoesNotDependOnThePropertyOrder()
        {
            Assert.True(Scan("{\"FiStId\":0,\"SpNa\":\"Soccer\",\"PrFiId\":\"top\"}", out var fixtureId, out var statusId));
            Assert.Equal("top", fixtureId);
            Assert.Equal(0, statusId);
        }

        [Fact]
        public void TryRead_IgnoresPropertiesOfTheSameNameInsideNestedValues()
        {
            // every market and selection carries its own PrFiId
            var json = "{\"Ma\":[{\"PrFiId\":\"nested\",\"Se\":[{\"PrFiId\":\"deeper\"}]}],\"PrFiId\":\"top\",\"FiStId\":0}";

            Assert.True(Scan(json, out var fixtureId, out _));
            Assert.Equal("top", fixtureId);
        }

        [Fact]
        public void TryRead_ReturnsTheIdWithNoStatusWhenTheMessageHasNone()
        {
            Assert.True(Scan("{\"PrFiId\":\"only\"}", out var fixtureId, out var statusId));
            Assert.Equal("only", fixtureId);
            Assert.Equal(-1, statusId);
        }

        [Theory]
        [InlineData("{\"FiStId\":0}")]
        [InlineData("{\"PrFiId\":null,\"FiStId\":0}")]
        [InlineData("{\"PrFiId\":123,\"FiStId\":0}")]
        [InlineData("[]")]
        [InlineData("\"text\"")]
        [InlineData("{}")]
        public void TryRead_ReturnsFalseWhenThereIsNoFixtureId(string json)
        {
            Assert.False(Scan(json, out var fixtureId, out _));
            Assert.Null(fixtureId);
        }

        [Fact]
        public void TryRead_StopsBeforeTheMarketsOnceItHasBothValues()
        {
            // a truncated body proves the scan did not need to read past the two properties it wanted
            Assert.True(Scan("{\"PrFiId\":\"early\",\"FiStId\":0,\"Ma\":[{\"MaId\":", out var fixtureId, out var statusId));
            Assert.Equal("early", fixtureId);
            Assert.Equal(0, statusId);
        }
    }
}

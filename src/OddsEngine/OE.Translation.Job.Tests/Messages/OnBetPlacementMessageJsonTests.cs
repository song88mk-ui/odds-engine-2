using System.Linq;
using System.Text.Json;
using OE.Translation.Job.Messages;
using OE.Translation.Job.Translation;

namespace OE.Translation.Job.Tests.Messages
{
    // Guards the wire contract: platform-final publishes OnBetPlacement with System.Text.Json
    // defaults (PascalCase). The worker deserializes case-insensitively. These tests pin both
    // the casing tolerance and the end-to-end payload -> keys mapping.
    public class OnBetPlacementMessageJsonTests
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true
        };

        // Shape as published by platform-final (PascalCase, BetSelection nested under each item).
        private const string PascalCasePayload = @"
        {
            ""Bet"": { ""BetId"": ""3f1d"" },
            ""BetSelections"": [
                {
                    ""BetSelection"": {
                        ""FixtureId"": 111,
                        ""MarketId"": 222,
                        ""SelectionId"": 333,
                        ""MarketTemplateId"": ""20"",
                        ""SelectionTemplateId"": ""30"",
                        ""SelectionName"": ""Over""
                    },
                    ""MarketSelectionCount"": 2
                }
            ]
        }";

        [Fact]
        public void Deserializes_PascalCasePayload_FromPlatformFinal()
        {
            var message = JsonSerializer.Deserialize<OnBetPlacementMessage>(PascalCasePayload, Options);

            Assert.NotNull(message);
            var selection = Assert.Single(message!.BetSelections).BetSelection;
            Assert.Equal(111, selection.FixtureId);
            Assert.Equal(222, selection.MarketId);
            Assert.Equal(333, selection.SelectionId);
            Assert.Equal("20", selection.MarketTemplateId);
            Assert.Equal("30", selection.SelectionTemplateId);
        }

        [Fact]
        public void Deserialize_ThenExtract_ProducesExpectedKeys()
        {
            var message = JsonSerializer.Deserialize<OnBetPlacementMessage>(PascalCasePayload, Options);

            var keys = TranslationKeyBuilder.Extract(message!);

            Assert.Equal(
                new[] { "MaTe:20", "SeTe:20:30" }.OrderBy(x => x),
                keys.OrderBy(x => x));
        }

        [Fact]
        public void Deserializes_CamelCasePayload_ViaCaseInsensitiveOption()
        {
            const string camel = @"{ ""betSelections"": [ { ""betSelection"": { ""fixtureId"": 5, ""marketId"": 6, ""selectionId"": 7, ""marketTemplateId"": ""8"", ""selectionTemplateId"": ""9"" } } ] }";

            var message = JsonSerializer.Deserialize<OnBetPlacementMessage>(camel, Options);

            var keys = TranslationKeyBuilder.Extract(message!);

            Assert.Equal(new[] { "MaTe:8", "SeTe:8:9" }.OrderBy(x => x), keys.OrderBy(x => x));
        }

        [Fact]
        public void Extract_IsEmpty_WhenBetSelectionsMissing()
        {
            const string payload = @"{ ""Bet"": { ""BetId"": ""abc"" } }";

            var message = JsonSerializer.Deserialize<OnBetPlacementMessage>(payload, Options);

            Assert.Empty(TranslationKeyBuilder.Extract(message!));
        }
    }
}

using System.Linq;
using OE.Translation.Job.Messages;
using OE.Translation.Job.Translation;

namespace OE.Translation.Job.Tests.Translation
{
    public class TranslationKeyBuilderTests
    {
        private static OnBetPlacementMessage MessageWith(params (string maTe, string seTe)[] selections)
        {
            return new OnBetPlacementMessage
            {
                BetSelections = selections
                    .Select(s => new BetSelectionMessageExtension
                    {
                        BetSelection = new BetSelectionMessage
                        {
                            MarketTemplateId = s.maTe,
                            SelectionTemplateId = s.seTe
                        }
                    })
                    .ToList()
            };
        }

        [Fact]
        public void Extract_BuildsPrefixedKeys_ForMarketAndSelectionTemplate()
        {
            var message = MessageWith(("20", "30"));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.Contains("MaTe:20", keys);
            Assert.Contains("SeTe:20:30", keys);
            Assert.Equal(2, keys.Count);
        }

        [Fact]
        public void Extract_Deduplicates_SharedMarketTemplateAcrossSelections()
        {
            // Two selections on the same market template, different selection templates.
            var message = MessageWith(("20", "30"), ("20", "31"));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.Equal(
                new[] { "MaTe:20", "SeTe:20:30", "SeTe:20:31" }.OrderBy(x => x),
                keys.OrderBy(x => x));
        }

        [Fact]
        public void Extract_SkipsSelectionTemplate_WhenBlank()
        {
            var message = MessageWith(("20", ""));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.Equal(new[] { "MaTe:20" }, keys);
        }

        [Fact]
        public void Extract_SkipsEntry_WhenMarketTemplateBlank()
        {
            var message = MessageWith(("", "30"));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.Empty(keys);
        }

        [Fact]
        public void Extract_ReturnsEmpty_WhenMessageIsNull()
        {
            var keys = TranslationKeyBuilder.Extract(null!);

            Assert.Empty(keys);
        }

        [Fact]
        public void Extract_ReturnsEmpty_WhenBetSelectionsIsNull()
        {
            var keys = TranslationKeyBuilder.Extract(new OnBetPlacementMessage { BetSelections = null! });

            Assert.Empty(keys);
        }

        [Fact]
        public void Extract_SkipsNullSelectionEntries()
        {
            var message = new OnBetPlacementMessage
            {
                BetSelections = new()
                {
                    new BetSelectionMessageExtension { BetSelection = null! },
                    new BetSelectionMessageExtension
                    {
                        BetSelection = new BetSelectionMessage { MarketTemplateId = "20", SelectionTemplateId = "30" }
                    }
                }
            };

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.Equal(new[] { "MaTe:20", "SeTe:20:30" }.OrderBy(x => x), keys.OrderBy(x => x));
        }
    }
}

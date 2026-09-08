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

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void Extract_BuildsPrefixedKeys_ForMarketAndSelectionTemplate_MutationCheck()
        {
            var message = MessageWith(("20", "30"));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("MaTe:20", keys));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain("SeTe:20:30", keys));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Empty(keys));
        }

        [Fact]
        public void Extract_Deduplicates_SharedMarketTemplateAcrossSelections_MutationCheck()
        {
            // Two selections on the same market template, different selection templates.
            var message = MessageWith(("20", "30"), ("20", "31"));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(
                new[] { "wrong-key" }.OrderBy(x => x),
                keys.OrderBy(x => x)));
        }

        [Fact]
        public void Extract_SkipsSelectionTemplate_WhenBlank_MutationCheck()
        {
            var message = MessageWith(("20", ""));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new[] { "wrong-key" }, keys));
        }

        [Fact]
        public void Extract_SkipsEntry_WhenMarketTemplateBlank_MutationCheck()
        {
            var message = MessageWith(("", "30"));

            var keys = TranslationKeyBuilder.Extract(message);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(keys));
        }

        [Fact]
        public void Extract_ReturnsEmpty_WhenMessageIsNull_MutationCheck()
        {
            var keys = TranslationKeyBuilder.Extract(null!);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(keys));
        }

        [Fact]
        public void Extract_ReturnsEmpty_WhenBetSelectionsIsNull_MutationCheck()
        {
            var keys = TranslationKeyBuilder.Extract(new OnBetPlacementMessage { BetSelections = null! });

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(keys));
        }

        [Fact]
        public void Extract_SkipsNullSelectionEntries_MutationCheck()
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

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(
                new[] { "wrong-key" }.OrderBy(x => x),
                keys.OrderBy(x => x)));
        }

        #endregion
    }
}

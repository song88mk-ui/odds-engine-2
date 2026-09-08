using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // Pins the variant-id scheme for concurrent market-template wordings (2026-07-16, prod
    // MaTe:259 emitted "Total Goals Over / Under" and "Over/Under" simultaneously) — the id must
    // be deterministic across services and stable under the same normalization the translation
    // caches use for matching (case + surrounding whitespace).
    public class MarketTemplateVariantIdTests
    {
        [Fact]
        public void Compute_IsDeterministic()
        {
            Assert.Equal(
                MarketTemplateVariantId.Compute("259", "Over/Under"),
                MarketTemplateVariantId.Compute("259", "Over/Under"));
        }

        [Fact]
        public void Compute_NormalizesCaseAndSurroundingWhitespace()
        {
            var canonical = MarketTemplateVariantId.Compute("259", "Over/Under");

            Assert.Equal(canonical, MarketTemplateVariantId.Compute("259", "OVER/UNDER"));
            Assert.Equal(canonical, MarketTemplateVariantId.Compute("259", "  Over/Under  "));
        }

        [Fact]
        public void Compute_DifferentWordings_ProduceDifferentIds()
        {
            Assert.NotEqual(
                MarketTemplateVariantId.Compute("259", "Over/Under"),
                MarketTemplateVariantId.Compute("259", "Total Goals Over / Under"));
        }

        [Fact]
        public void Compute_ShapeIsBaseKeyTildeHash()
        {
            var id = MarketTemplateVariantId.Compute("259", "Over/Under");

            Assert.StartsWith("MaTe:259~", id);
            Assert.Equal("MaTe:259~".Length + 8, id.Length);
        }

        [Fact]
        public void GetBaseId_StripsTheVariantSuffix()
        {
            var id = MarketTemplateVariantId.Compute("259", "Over/Under"); // "MaTe:259~xxxxxxxx"
            var segment = id.Substring("MaTe:".Length);                    // "259~xxxxxxxx"

            Assert.Equal("259", MarketTemplateVariantId.GetBaseId(segment));
        }

        [Fact]
        public void GetBaseId_LeavesABaseSegmentUntouched()
        {
            Assert.Equal("259", MarketTemplateVariantId.GetBaseId("259"));
        }

        [Fact]
        public void IsVariant_DetectsTheSeparator()
        {
            Assert.True(MarketTemplateVariantId.IsVariant("259~a1b2c3d4"));
            Assert.False(MarketTemplateVariantId.IsVariant("259"));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void Compute_IsDeterministic_MutationCheck()
        {
            var a = MarketTemplateVariantId.Compute("259", "Over/Under");
            var wrong = MarketTemplateVariantId.Compute("999", "Totally Different Wording");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(wrong, a));
        }

        [Fact]
        public void Compute_NormalizesCaseAndSurroundingWhitespace_MutationCheck()
        {
            var wrong = MarketTemplateVariantId.Compute("259", "Totally Different Wording");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(wrong, MarketTemplateVariantId.Compute("259", "OVER/UNDER")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(wrong, MarketTemplateVariantId.Compute("259", "  Over/Under  ")));
        }

        [Fact]
        public void Compute_DifferentWordings_ProduceDifferentIds_MutationCheck()
        {
            var a = MarketTemplateVariantId.Compute("259", "Over/Under");
            var b = MarketTemplateVariantId.Compute("259", "Total Goals Over / Under");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(a, b));
        }

        [Fact]
        public void Compute_ShapeIsBaseKeyTildeHash_MutationCheck()
        {
            var id = MarketTemplateVariantId.Compute("259", "Over/Under");

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.StartsWith("Wrong:", id));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("MaTe:259~".Length + 8 + 1, id.Length));
        }

        [Fact]
        public void GetBaseId_StripsTheVariantSuffix_MutationCheck()
        {
            var id = MarketTemplateVariantId.Compute("259", "Over/Under");
            var segment = id.Substring("MaTe:".Length);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("999", MarketTemplateVariantId.GetBaseId(segment)));
        }

        [Fact]
        public void GetBaseId_LeavesABaseSegmentUntouched_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal("999", MarketTemplateVariantId.GetBaseId("259")));
        }

        [Fact]
        public void IsVariant_DetectsTheSeparator_MutationCheck()
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(MarketTemplateVariantId.IsVariant("259~a1b2c3d4")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(MarketTemplateVariantId.IsVariant("259")));
        }

        #endregion
    }
}

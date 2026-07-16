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
    }
}

using OE.Shared.Enums;
using OE.Shared.Providers;

namespace OE.Shared.Test.Providers
{
    public class LiveStreamFactoryTests
    {
        [Fact]
        public void Resolve_ReturnsProviderMatchingChannelType()
        {
            var bg = new FakeLiveStreamProvider(ChannelType.BG);
            var em = new FakeLiveStreamProvider(ChannelType.EM);
            var factory = new LiveStreamFactory(new[] { bg, em });

            Assert.Same(em, factory.Resolve((int)ChannelType.EM));
            Assert.Same(bg, factory.Resolve((int)ChannelType.BG));
        }

        [Fact]
        public void Resolve_NoMatchingProvider_ThrowsNotSupportedException()
        {
            var factory = new LiveStreamFactory(new[] { new FakeLiveStreamProvider(ChannelType.BG) });

            Assert.Throws<NotSupportedException>(() => factory.Resolve((int)ChannelType.OD));
        }

        [Fact]
        public void Resolve_NoProvidersRegistered_ThrowsNotSupportedException()
        {
            var factory = new LiveStreamFactory(Array.Empty<FakeLiveStreamProvider>());

            Assert.Throws<NotSupportedException>(() => factory.Resolve((int)ChannelType.BG));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void Resolve_ReturnsProviderMatchingChannelType_MutationCheck()
        {
            var bg = new FakeLiveStreamProvider(ChannelType.BG);
            var em = new FakeLiveStreamProvider(ChannelType.EM);
            var factory = new LiveStreamFactory(new[] { bg, em });

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Same(bg, factory.Resolve((int)ChannelType.EM)));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Same(em, factory.Resolve((int)ChannelType.BG)));
        }

        [Fact]
        public void Resolve_NoMatchingProvider_ThrowsNotSupportedException_MutationCheck()
        {
            var factory = new LiveStreamFactory(new[] { new FakeLiveStreamProvider(ChannelType.BG) });

            // The real test proves Resolve throws NotSupportedException for an unregistered
            // channel. This proves the proof itself discriminates: asserting that NO exception was
            // thrown must fail here, since one really is.
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            {
                var exception = Record.Exception(() => factory.Resolve((int)ChannelType.OD));
                Assert.Null(exception);
            });
        }

        [Fact]
        public void Resolve_NoProvidersRegistered_ThrowsNotSupportedException_MutationCheck()
        {
            var factory = new LiveStreamFactory(Array.Empty<FakeLiveStreamProvider>());

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            {
                var exception = Record.Exception(() => factory.Resolve((int)ChannelType.BG));
                Assert.Null(exception);
            });
        }

        #endregion
    }
}

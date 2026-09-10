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
    }
}

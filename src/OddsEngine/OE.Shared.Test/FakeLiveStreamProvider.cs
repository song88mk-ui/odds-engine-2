using OE.Shared.Enums;
using OE.Shared.Providers;

namespace OE.Shared.Test
{
    // Minimal ILiveStreamProvider stand-in for LiveStreamFactory resolution tests — Resolve() never
    // calls into the streaming methods, so they're left unimplemented on purpose.
    public class FakeLiveStreamProvider : ILiveStreamProvider
    {
        public ChannelType ChannelType { get; }

        public FakeLiveStreamProvider(ChannelType channelType)
        {
            ChannelType = channelType;
        }

        public Task<(bool, string)> GetLiveStream(string fixtureId) => throw new NotImplementedException();

        public Task<List<string>> GetLiveStreams() => throw new NotImplementedException();
    }
}

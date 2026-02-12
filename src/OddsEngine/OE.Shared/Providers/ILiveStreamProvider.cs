using OE.Shared.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OE.Shared.Providers
{
    public interface ILiveStreamProvider
    {
        ChannelType ChannelType { get; }

        Task<(bool, string)> GetLiveStream(string fixtureId);
        Task<List<string>> GetLiveStreams();
    }
}

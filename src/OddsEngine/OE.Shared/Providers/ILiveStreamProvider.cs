using OE.Shared.Enums;
using System.Threading.Tasks;

namespace OE.Shared.Providers
{
    public interface ILiveStreamProvider
    {
        ChannelType ChannelType { get; }

        Task<(bool, string)> GetLiveStream(string fixtureId);
    }
}

namespace OE.Shared.Providers
{
    public interface ILiveStreamFactory
    {
        ILiveStreamProvider Resolve(int channelType);
    }
}

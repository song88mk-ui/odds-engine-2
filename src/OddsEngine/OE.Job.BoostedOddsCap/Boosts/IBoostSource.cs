namespace OE.Job.BoostedOddsCap.Boosts
{
    public interface IBoostSource
    {
        Task<BoostSnapshot> LoadAsync(CancellationToken cancellationToken);
    }
}

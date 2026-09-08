using System.Threading;
using System.Threading.Tasks;

namespace OE.Job.Cleanup.Repository
{
    public interface IBetFixtureCache
    {
        // True once the initial preload from the Betting SQL database has completed.
        // Cleanup must never delete anything while this is false.
        bool IsLoaded { get; }
        int Count { get; }
        Task Preload(CancellationToken cancellationToken);
        void Add(long fixtureId);
        bool Contains(long fixtureId);
    }
}

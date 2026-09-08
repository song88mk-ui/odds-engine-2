using System.Threading;
using System.Threading.Tasks;

namespace OE.Job.Cleanup.Repository
{
    public interface ISportsbookRepository
    {
        Task Cleanup(CancellationToken cancellationToken);
    }
}

using System.Threading.Tasks;

namespace OE.Job.LiveStream.Repository
{
    public interface ISportsbookRepository
    {
        Task ProcessLiveStream();
    }
}

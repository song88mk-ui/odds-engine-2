using OE.Shared.Utilities;

namespace OE.Job.Settlement.Repository
{
    public interface ISportsbookRepository
    {
        Task ProcessSettlement(IMessageBroker messageBroker);
    }
}

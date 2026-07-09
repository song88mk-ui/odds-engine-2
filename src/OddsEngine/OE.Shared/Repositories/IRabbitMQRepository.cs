using OE.Shared.Models;
using OE.Shared.Models.Responses;
using System.Threading.Tasks;

namespace OE.Shared.Repositories
{
    public interface IRabbitMQRepository
    {
        BaseResponseDTO<MessageResponseDTO> PublishViaHttp(MessageRequestDTO request);

        Task<BaseResponseDTO<MessageResponseDTO>> PublishViaHttpAsync(MessageRequestDTO request);
    }
}

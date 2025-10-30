using OE.Shared.Models;
using OE.Shared.Models.Responses;

namespace OE.Shared.Repositories
{
    public interface IRabbitMQRepository
    {
        BaseResponseDTO<MessageResponseDTO> PublishViaHttp(MessageRequestDTO request);
    }
}

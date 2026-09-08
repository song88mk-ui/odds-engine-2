using OE.Shared.Models.Provider.BetGenius;
using OE.Shared.Models.Responses;
using System.Threading.Tasks;

namespace OE.Shared.Repositories
{
    public interface IBetGeniusRepository
    {
        Task<BaseResponseDTO<BGAuth>> GetBetGeniusAuthorizationToken();
        Task<BaseResponseDTO<BGEvent>> GetBetGeniusFixture(string token, string fixtureId);
        Task<BaseResponseDTO<BGEvents>> GetBetGeniusFixtures(string token);
    }
}

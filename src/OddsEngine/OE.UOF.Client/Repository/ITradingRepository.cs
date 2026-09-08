using OE.Shared.Models;

namespace OE.UOF.Client.Repository
{
    public interface ITradingRepository
    {
        List<ClientConfigurationModel> GetClientConfigurations();
        List<ClientLiveFixtureModel> GetClientLiveFixtures();
    }
}

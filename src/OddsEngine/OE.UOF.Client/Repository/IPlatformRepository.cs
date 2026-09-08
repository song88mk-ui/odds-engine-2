using OE.UOF.Client.Models;

namespace OE.UOF.Client.Repository
{
    public interface IPlatformRepository
    {
        void ProcessMarketTemplatePlayerProps();
        List<MarketTemplatePlayerPropsResponseDTO> GetMarketTemplatePlayerProps();
    }
}

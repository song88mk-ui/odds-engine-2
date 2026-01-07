using Microsoft.AspNetCore.SignalR.Client;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.WebSocket.Service.Messages;
namespace OE.WebSocket.Service.Repository
{
    public interface ISportsbookRepository
    {
        Task ProcessOddsChangeForMainMarkets(OnMarketChangeMessage message, HubConnection hubConnection);
        Task ProcessOddsChangeForAllMarkets(OnMarketChangeMessage message, HubConnection hubConnection);
        //TODO: uncomment
        //Task ProcessBetStopFixture(OnInternalBetStopFixtureMessage onBetStop, HubConnection hubConnection);
        Task ProcessBetStopGlobal(OnBetStopGlobalMessage onBetStopGlobal, HubConnection hubConnection);
        Task ProcessBetStopFixture(OnBetStopFixtureMessage onBetStopFixtureMessage, HubConnection hubConnection);
        Task ProcessBetStatus(IncomingBetStatus onBetStatus, HubConnection hubConnection);
        Task ProcessCashOutChange(IncomingCashOutChange onCashOutChange, HubConnection hubConnection);
        Task ProcessWalletChange(IncomingWalletChange onWalletChange, HubConnection hubConnection);
        Task ProcessTopFixtures(OnMarketChangeMessage message);
        Task ProcessTopLeagues(OnMarketChangeMessage message);
        Task ProcessMarketCount(OnMarketChangeMessage message);
        Task ProcessFavorites(IncomingVisitFavorites message);
        Task ProcessRemoveFavorites(IncomingVisitFavorites message);
        Task ProcessBoostedFixtures(IncomingVisitBoostedFixtures message);
        Task ProcessPayableChange(IncomingPayableChange message, HubConnection hubConnection);
        Task ProcessMainMarketTemplates();
        Task ProcessMarketSortOrder();
        Task ProcessFixtureSortOrder();
        Task ProcessBetBuilderMarketTemplates();
        Task ProcessMarketTemplatePlayerProps();
    }
}

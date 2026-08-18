using Microsoft.AspNetCore.SignalR.Client;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.WebSocket.Service.Clients;
using OE.WebSocket.Service.Messages;
namespace OE.WebSocket.Service.Repository
{
    public interface ISportsbookRepository
    {
        Task ProcessOddsChangeForMainMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection);
        Task ProcessOddsChangeForAllMarkets(ClientContext client, OnMarketChangeMessage message, HubConnection hubConnection);
        Task ProcessBetStopGlobal(ClientContext client, OnBetStopGlobalMessage onBetStopGlobal, HubConnection hubConnection);
        Task ProcessBetStopFixture(ClientContext client, OnBetStopFixtureMessage onBetStopFixtureMessage, HubConnection hubConnection);
        Task ProcessBetStatus(ClientContext client, IncomingBetStatus onBetStatus, HubConnection hubConnection);
        Task ProcessCashOutChange(ClientContext client, IncomingCashOutChange onCashOutChange, HubConnection hubConnection);
        Task ProcessWalletChange(ClientContext client, IncomingWalletChange onWalletChange, HubConnection hubConnection);
        Task ProcessTopFixtures(ClientContext client, OnMarketChangeMessage message);
        Task ProcessTopLeagues(ClientContext client, OnMarketChangeMessage message);
        Task ProcessMarketCount(ClientContext client, OnMarketChangeMessage message);
        Task ProcessFavorites(ClientContext client, IncomingVisitFavorites message);
        Task ProcessRemoveFavorites(ClientContext client, IncomingVisitFavorites message);
        Task ProcessBoostedFixtures(ClientContext client, IncomingVisitBoostedFixtures message);
        Task ProcessPayableChange(ClientContext client, IncomingPayableChange message, HubConnection hubConnection);
        Task ProcessMainMarketTemplates(ClientContext client);
        Task ProcessMarketSortOrder(ClientContext client);
        Task ProcessFixtureSortOrder(ClientContext client);
        Task ProcessBetBuilderMarketTemplates(ClientContext client);
        Task ProcessMarketTemplatePlayerProps(ClientContext client);
    }
}

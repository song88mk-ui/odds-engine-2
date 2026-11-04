using Microsoft.Extensions.Logging;
using OE.Adapter.Service.ClientVisibility;
using OE.Adapter.Service.Telemetry;
using OE.Shared.Constants;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;
using System.Diagnostics;
using System.Text.Json;

namespace OE.Adapter.Service.Repositories
{
    public class TradingRepository : ITradingRepository
    {
        #region Fields

        private readonly IHelper _helper;
        private readonly ILogger<TradingRepository> _logger;
        private readonly ITranslationCache _translationCache;
        private readonly IClientVisibility _clientVisibility;
        private readonly ILeagueRefreshRequests _leagueRefreshRequests;
        private const ChannelType _bgProvider = ChannelType.BG;
        private readonly ActivitySource _activitySource;

        #endregion

        #region Ctor

        public TradingRepository(IHelper helper, ILogger<TradingRepository> logger, ITranslationCache translationCache,
            IClientVisibility clientVisibility, ILeagueRefreshRequests leagueRefreshRequests, Instrumentation instrumentation)
        {
            _helper = helper;
            _logger = logger;
            _translationCache = translationCache;
            _clientVisibility = clientVisibility;
            _leagueRefreshRequests = leagueRefreshRequests;
            _activitySource = instrumentation.ActivitySource;
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            EnrichFixtureTranslations(message);
            StampClientCodes(message);

            PublishMessage(messageBroker, message, MessageType.ON_FIXTURE_CHANGE, correlationId);
        }

        public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            EnrichFixtureTranslations(message);
            EnrichMarketTranslations(message);
            StampClientCodes(message);

            PublishMessage(messageBroker, message, MessageType.ON_MARKET_CHANGE, correlationId);
        }

        public void ProcessOnMarketSettlementMessage(IMessageBroker messageBroker, OnMarketSettlementMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_MARKET_SETTLEMENT, correlationId);
        }

        public void ProcessOnBetSettlementMessage(IMessageBroker messageBroker, OnBetSettlementMessage message, string correlationId)
        {
            try
            {
                using (var activity = _activitySource.StartActivity("publish.message"))
                {
                    var instanceName = _helper.GetInstanceName(message.ProviderFixtureId);
                    activity?.SetTag("sb.engine.service.id", instanceName);
                    messageBroker.Publish(message, MessageType.ON_BET_SETTLEMENT, $"r.oe.adapter.{MessageType.ON_BET_SETTLEMENT.ToLower()}.{instanceName}", correlationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{MessageType.ON_BET_SETTLEMENT} {JsonSerializer.Serialize(message)}");
                throw;
            }
        }

        public void ProcessOnBetStopGlobalMessage(IMessageBroker messageBroker, OnBetStopGlobalMessage message, string correlationId)
        {
            using (var activity = _activitySource.StartActivity("publish.message"))
            {
                var instanceName = "10";
                activity?.SetTag("sb.engine.service.id", instanceName);

                messageBroker.Publish(message, MessageType.ON_BET_STOP_GLOBAL, $"r.oe.adapter.{MessageType.ON_BET_STOP_GLOBAL.ToLower()}.{instanceName}", correlationId);
            }
        }

        public void ProcessMarketCancellation(IMessageBroker messageBroker, OnMarketCancellationMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_MARKET_CANCEL, correlationId);
        }
        public void ProcessOnBetStopFixture(IMessageBroker messageBroker, OnBetStopFixtureMessage message, string correlationId)
        {
            PublishMessage(messageBroker, message, MessageType.ON_BET_STOP_FIXTURE, correlationId);
        }

        // Backoffice's price boost (r.uof.onpriceboost) is forwarded to the shard that owns the fixture, like
        // the settlement and bet-stop messages, so one consumer applies a fixture's boosts, odds and status
        // changes in order. The message is forwarded as it is: the shard stamps it onto Mongo.
        public void ProcessOnPriceBoostMessage(IMessageBroker messageBroker, OnPriceBoostMessage message, string correlationId)
        {
            try
            {
                using (var activity = _activitySource.StartActivity("publish.message"))
                {
                    var instanceName = _helper.GetInstanceName(message.ProviderFixtureId);
                    activity?.SetTag("sb.engine.service.id", instanceName);
                    messageBroker.Publish(message, MessageType.ON_PRICE_BOOST, $"r.oe.adapter.{MessageType.ON_PRICE_BOOST.ToLower()}.{instanceName}", correlationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{MessageType.ON_PRICE_BOOST} {JsonSerializer.Serialize(message)}");
                throw;
            }
        }

        // Backoffice saved a league setting (r.uof.onleagueconfig). The message only names the league: the adapter reads
        // the saved settings again and sends the league's clients to the shard (see ClientVisibilityRefresher).
        public void ProcessOnLeagueConfigMessage(IMessageBroker messageBroker, OnLeagueConfigMessage message, string correlationId)
        {
            if (message == null)
                return;

            _leagueRefreshRequests.Request(message.SportId, message.SegmentId, message.LeagueId);
        }

        // The provider sends ClCo empty (bet-genius), and nothing downstream fills it: the site, the websocket and the
        // boosted odds cap job all read it as the clients allowed to see the league. So the adapter stamps it, onto the
        // fixture and onto every market, from the league's saved settings: every client except the ones Backoffice
        // restricted for it. Before the first load, or when the lists are not configured, the message is left as it came.
        private void StampClientCodes(OnFixtureChangeMessage message)
        {
            var clientCodes = _clientVisibility.ClientCodesFor(message.SpId, message.SegId, message.LeId);
            if (clientCodes == null)
                return;

            message.ClCo = clientCodes;

            if (message is OnMarketChangeMessage marketMessage && marketMessage.Ma != null)
            {
                foreach (var market in marketMessage.Ma)
                {
                    if (market != null)
                        market.ClCo = clientCodes;
                }
            }
        }

        // Cache-apply only (2026-07 translation-flow redesign): the enrichment snapshot follows
        // the change. Persistence + translation-only odds pushes are OE.Translation.Service's job —
        // its queue binds the same r.oe.ontranslationchange key on the topic exchange, so no
        // forwarding is needed here.
        public void ProcessOnTranslationChangeMessage(IMessageBroker messageBroker, OnTranslationChangeMessage message, string correlationId)
        {
            if (message?.Tr == null || message.Tr.Count == 0)
                return;

            _translationCache.Apply(message.Tr);
        }

        #endregion

        #region Utilities

        // Attaches translations from the preloaded TranslationCache2 snapshot. A missing key leaves
        // the *Tr property null — downstream consumers fall back to the English *Na source.
        private void EnrichFixtureTranslations(OnFixtureChangeMessage message)
        {
            message.SpTr = _translationCache.GetTranslations($"SpId:{message.SpId}", message.SpNa);
            message.SegTr = _translationCache.GetTranslations($"SegId:{message.SegId}", message.SegNa);
            message.LeTr = _translationCache.GetTranslations($"LeId:{message.LeId}", message.LeNa);
            message.HoTr = _translationCache.GetTranslations($"Te:{message.HoId}", message.HoNa);
            message.AwTr = _translationCache.GetTranslations($"Te:{message.AwId}", message.AwNa);
        }

        private void EnrichMarketTranslations(OnMarketChangeMessage message)
        {
            if (message.Ma == null)
                return;

            foreach (var market in message.Ma)
            {
                if (market == null)
                    continue;

                var maTeTr = _translationCache.GetTranslations($"MaTe:{market.MaTeId}", market.MaTeNa);

                // Per-player prop market (GM-528): the translated name is COMPOSED from the
                // player's Pl:{CompId} translation and the template translation — interpolated
                // through the MaTeP:{MaTeId} placeholder row when its translation kept the
                // {player} marker, joined otherwise (see PlayerMarketNameComposer). When inputs
                // are missing, MaTeTr stays null and consumers fall back to the English MaNa,
                // which the producer already sets to the player-inclusive name.
                market.MaTeTr = market.CompId.HasValue
                    ? PlayerMarketNameComposer.Compose(
                        _translationCache.GetTranslations($"Pl:{market.CompId.Value}", null),
                        maTeTr,
                        _translationCache.GetTranslations($"MaTeP:{market.MaTeId}", null))
                    : maTeTr;

                if (market.Se == null)
                    continue;

                foreach (var selection in market.Se)
                {
                    if (selection == null)
                        continue;

                    selection.SeNaTr = GetSelectionTranslations(message, market, selection);
                }
            }
        }

        // Which TranslationCache2 key backs this selection's SeNaTr — see
        // OE.Shared.Utilities.SelectionTranslationKeyResolver for the full Co:/SeTe:/Te: decision
        // (shared with OE.Translation.Service.Repositories.MongoTranslationRepository, which
        // persists the same field to Mongo).
        private List<TranslationMessage> GetSelectionTranslations(OnFixtureChangeMessage message, MarketMessage market, SelectionMessage selection)
        {
            var key = SelectionTranslationKeyResolver.Resolve(
                selection.CompId, selection.SeNa, selection.SeTeNa, selection.SeTeId, market.MaTeId, market.IsHa,
                message.HoId, message.HoNa, message.AwId, message.AwNa);

            var translations = _translationCache.GetTranslations(key.StringId, key.SourceText);

            // Positional token templates ("Home or Draw") share one SeTe: row across fixtures, so
            // the raw translation reads generic (主队胜或平局) while the English SeNa carries the
            // fixture's own teams — swap the translated tokens for the fixture's translated team
            // names. Every SeNaTr producer must apply this identically (see
            // SelectionTranslationComposer) or REST and websocket text diverge.
            if (translations != null && SelectionTranslationComposer.RequiresTeamTokenComposition(key.StringId, selection.SeTeNa))
            {
                translations = SelectionTranslationComposer.ComposeTeamTokens(
                    translations,
                    selection.SeTeNa,
                    _translationCache.GetTranslations($"Te:{message.HoId}", message.HoNa),
                    _translationCache.GetTranslations($"Te:{message.AwId}", message.AwNa));
            }

            return translations;
        }

        private static FixtureStatus GetFixtureStatus(int fixtureStatusId)
        {
            return (FixtureStatus)fixtureStatusId;
        }

        private static FixtureType GetFixtureType(int fixtureTypeId)
        {
            return (FixtureType)fixtureTypeId;
        }

        private static MarketStatus GetMarketStatus(int marketStatusId)
        {
            return (MarketStatus)marketStatusId;
        }

        private static CashoutStatus GetCashoutStatus(int cashoutStatusId)
        {
            return (CashoutStatus)cashoutStatusId;
        }

        private void PublishMessage<T>(IMessageBroker messageBroker, T payload, string messageType, string correlationId) where T : OutgoingAdapterMessageBase
        {
            try
            {
                using (var activity = _activitySource.StartActivity("publish.message"))
                {
                    var instanceName = _helper.GetInstanceName(payload.PrFiId);
                    activity?.SetTag("sb.engine.service.id", instanceName);

                    messageBroker.Publish(payload, messageType, $"r.oe.adapter.{messageType.ToLower()}.{instanceName}", correlationId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{messageType} {JsonSerializer.Serialize<T>(payload)}");
                throw;
            }
        }

        #endregion
    }
}

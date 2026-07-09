using Microsoft.Extensions.Logging;
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
        private const ChannelType _bgProvider = ChannelType.BG;
        private readonly ActivitySource _activitySource;

        #endregion

        #region Ctor

        public TradingRepository(IHelper helper, ILogger<TradingRepository> logger, ITranslationCache translationCache, Instrumentation instrumentation)
        {
            _helper = helper;
            _logger = logger;
            _translationCache = translationCache;
            _activitySource = instrumentation.ActivitySource;
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureChangeMessage(IMessageBroker messageBroker, OnFixtureChangeMessage message, string correlationId)
        {
            EnrichFixtureTranslations(message);

            PublishMessage(messageBroker, message, MessageType.ON_FIXTURE_CHANGE, correlationId);
        }

        public void ProcessOnMarketChangeMessage(IMessageBroker messageBroker, OnMarketChangeMessage message, string correlationId)
        {
            EnrichFixtureTranslations(message);
            EnrichMarketTranslations(message);

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
                _logger.LogError($"{MessageType.ON_BET_SETTLEMENT} {JsonSerializer.Serialize(message)} {ex.Message}");
                _logger.LogError($"{MessageType.ON_BET_SETTLEMENT} {ex.StackTrace}");
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

                market.MaTeTr = _translationCache.GetTranslations($"MaTe:{market.MaTeId}", market.MaTeNa);

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

        // Co:/SeTe: are mutually exclusive lookup keys for the same SeNaTr result, matching the
        // seeding branch in OE.Translation.Service.OnProcessMarketSetUpdate.
        //
        // A handicap-market selection whose CompId is one of the fixture's own two participants
        // (e.g. Asian Handicap by team) reuses the Home/Away team-name translation already seeded
        // under Te:{HoId}/Te:{AwId} (EnrichFixtureTranslations, above) instead of a separate
        // Co:{CompId} lookup keyed off SeNa. SeNa for these selections has the handicap line baked
        // in (e.g. "Karpaty Lviv(-0.25)") -- caching that whole string under Co: defeated the cache
        // on every line change, since GetTranslations treats any text change as a rename. Te:'s
        // source text is just the team name, which is stable, and downstream (SelectionNameComposer)
        // already recomposes the line from Market.Li/IsHa on top of the translated name.
        //
        // Any other CompId (non-handicap markets such as Correct Score, where SeNa/SeTeNa fuse the
        // name with a volatile per-selection outcome like "Karpaty Lviv 1-0", or a named individual
        // competitor that isn't a fixture participant) is left on the original Co:{CompId} lookup,
        // unchanged -- there's no verified stable source text to key off yet for those cases.
        private List<TranslationMessage> GetSelectionTranslations(OnFixtureChangeMessage message, MarketMessage market, SelectionMessage selection)
        {
            if (selection.CompId == null)
                return _translationCache.GetTranslations($"SeTe:{market.MaTeId}:{selection.SeTeId}", selection.SeTeNa);

            if (market.IsHa)
            {
                var compIdText = selection.CompId.Value.ToString();

                if (compIdText == message.HoId)
                    return message.HoTr;

                if (compIdText == message.AwId)
                    return message.AwTr;
            }

            return _translationCache.GetTranslations($"Co:{selection.CompId}", selection.SeNa);
        }

        private FixtureStatus GetFixtureStatus(int fixtureStatusId)
        {
            return (FixtureStatus)fixtureStatusId;
        }

        private FixtureType GetFixtureType(int fixtureTypeId)
        {
            return (FixtureType)fixtureTypeId;
        }

        private MarketStatus GetMarketStatus(int marketStatusId)
        {
            return (MarketStatus)marketStatusId;
        }

        private CashoutStatus GetCashoutStatus(int cashoutStatusId)
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
                _logger.LogError($"{messageType} {JsonSerializer.Serialize<T>(payload)} {ex.Message}" );
                _logger.LogError($"{messageType} {ex.StackTrace}");
                throw;
            }
        }

        #endregion
    }
}

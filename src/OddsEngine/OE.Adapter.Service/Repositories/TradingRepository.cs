using OE.Shared.Constants;
using OE.Shared.Enums;
using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingAdapterMessages.Base;
using OE.Shared.Messages.OutgoingReceiverMessages;
using OE.Shared.Utilities;

using AdapterMarketMessage = OE.Shared.Messages.OutgoingAdapterMessages.MarketMessage;
using AdapterSelectionMessage = OE.Shared.Messages.OutgoingAdapterMessages.SelectionMessage;
using ReceiverMarketMessage = OE.Shared.Messages.OutgoingReceiverMessages.MarketMessage;
using ReceiverSelectionMessage = OE.Shared.Messages.OutgoingReceiverMessages.SelectionMessage;

namespace OE.Adapter.Service.Repositories
{
    public class TradingRepository : ITradingRepository
    {
        #region Fields

        private readonly IHelper _helper;

        #endregion

        #region Ctor

        public TradingRepository(IHelper helper)
        {
            _helper = helper;
        }

        #endregion

        #region Methods

        public void ProcessOnFixtureSetMessage(IMessageBroker messageBroker, OnFixtureSetMessage message, string correlationId)
        {
            var payload = new OnFixtureChangeMessage
            {
                ChTyId = message.ChTyId,
                PrFiId = message.FiId.ToString(),
                SpId = message.SpId,
                SpNa = message.SpNa,
                SegId = message.SegId,
                SegNa = message.SegNa,
                LeId = message.LeId,
                LeNa = message.LeNa,
                FiId = 0,
                FiNa = message.FiNa,
                FiStDa = message.FiStDa,
                FiStId = message.FiStId,
                FiStNa = GetFixtureStatus(message.FiStId).ToString(),
                FiTyId = message.FiTyId,
                FiTyNa = GetFixtureType(message.FiTyId).ToString(),
                HoId = message.HoId,
                AwId = message.AwId,
                Sc = message.Sc,
                FiSp = message.FiSp,
                IsOfLi = message.IsOfLi,
                HoReCa = 0,
                AwReCa = 0,
                CuPeId = 0,
                CuPe = "",
                CuMi = 0,
                ToTi = 0,
                Fa = FavoriteSelection.None.ToString(),
                SoOr = 0,
                ClCo = "",
                CoId = correlationId,
                IsSu = message.IsSu,
                IsBo = false
            };

            PublishMessage(messageBroker, payload, MessageType.ON_FIXTURE_CHANGE, correlationId);
        }

        public void ProcessOnMarketSetMessage(IMessageBroker messageBroker, OnMarketSetMessage message, string correlationId)
        {
            var providerFixtureId = message.FiId.ToString();

            var payload = new OnMarketChangeMessage()
            {
                PrFiId = providerFixtureId,
                Ma = PrepareMarkets(message, providerFixtureId),
            };

            PublishMessage(messageBroker, payload, MessageType.ON_MARKET_CHANGE, correlationId);
        }

        public void ProcessOnResultSetMessage(IMessageBroker messageBroker, OnResultSetMessage message, string correlationId)
        {
            var providerFixtureId = message.FiId.ToString();

            var payload = new OnSettlementChangeMessage()
            {
                PrFiId = providerFixtureId,
                ChTyId = message.ChTyId,
                Ma = message.Ma.Select(s => new ResultMarket()
                {
                    MaId = s.MaId,
                    MaStId = s.MaStId,
                    Se = s.Se.Select(se => new ResultSelection()
                    {
                        MaId = se.MaId,
                        SeId = se.SeId,
                        SeStId = se.SeStId
                    }).ToList()
                }).ToList()
            };

            PublishMessage(messageBroker, payload, MessageType.ON_MARKET_SETTLEMENT, correlationId);
        }

        #endregion

        #region Utilities

        private List<AdapterMarketMessage> PrepareMarkets(OnMarketSetMessage message, string providerFixtureId)
        {
            var result = new List<AdapterMarketMessage>();

            foreach (var ma in message.Ma)
            {
                var marketTemplateId = ma.MaTyId.ToString();

                var market = new AdapterMarketMessage
                {
                    PrFiId = providerFixtureId,
                    MaId = 0,
                    MaTeId = marketTemplateId,
                    PrMaId = ma.MaId.ToString(),
                    Li = ma.Ha.HasValue ? ma.Ha.ToString() : "",
                    MaStId = ma.MaStId,
                    MaStNa = GetMarketStatus(ma.MaStId).ToString(),
                    CaStId = ma.CaSt,
                    CaStNa = GetCashoutStatus(ma.CaSt).ToString(),
                    MaSp = "",
                    MaNa = ma.MaNa,
                    SoOr = ma.MaTyId,
                    ClCo = "",
                    ClCoMa = "",
                    Se = PrepareSelections(ma, providerFixtureId, marketTemplateId)
                };

                result.Add(market);
            }

            return result;
        }

        private List<AdapterSelectionMessage> PrepareSelections(ReceiverMarketMessage message, string providerFixtureId, string marketTemplateId)
        {
            return message.Se.Select(se => new AdapterSelectionMessage
            {
                PrFiId = providerFixtureId,
                MaTeId = marketTemplateId,
                SeTeId = se.SeTeId.ToString(),
                MaSp = "",
                SeId = 0,
                Od = se.Od,
                Pr = 0,
                WoPr = 0,
                LoPr = 0,
                VoPr = 0,
                HaWoPr = 0,
                HaLoPr = 0,
                IsAc = se.IsAc,
                SeSp = "",
                SeNa = se.SeNa,
                SoOr = se.SeTeId
            }).ToList();
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
            var instanceName = _helper.GetInstanceName(payload.PrFiId);

            messageBroker.Publish(payload, messageType, $"r.oe.adapter.{messageType.ToLower()}.{instanceName}", correlationId);
        }

        #endregion
    }
}

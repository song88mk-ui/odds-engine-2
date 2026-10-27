namespace OE.Shared.Constants
{
    public static class MessageType
    {
        public const string ON_BG_PROCESS_MESSAGE = "OnProcessMessage";
        public const string ON_BG_FIXTURE_CLOSE = "OnFixtureClose";
        public const string ON_BG_MARKET_CLOSE = "OnMarketClose";
        public const string ON_BG_ON_MATCH_SUMMARY = "OnMatchSummary";
        public const string ON_BG_OTHER_MESSAGE = "OnOtherMessage";

        public const string ON_FIXTURE_CHANGE = "OnFixtureChange";
        public const string ON_MARKET_CHANGE = "OnMarketChange";
        public const string ON_MARKET_SETTLEMENT = "OnMarketSettlement";
        public const string ON_MARKET_CANCEL = "OnMarketCancel";
        public const string ON_BET_STOP_FIXTURE = "OnBetStopFixture";
        public const string ON_BET_STOP_GLOBAL = "OnBetStopGlobal";
        public const string ON_HEART_BEAT = "OnHeartBeat";
        public const string ON_DYNAMIC_TRANSLATION_CHANGE = "OnDynamicTranslationChange";
        // Payload-carrying successor to ON_DYNAMIC_TRANSLATION_CHANGE (which is purge-only):
        // carries changed TranslationCache2 rows from Platform.CacheSync.Service to the
        // OE.Adapter.Service instances (see OnTranslationChangeMessage).
        public const string ON_TRANSLATION_CHANGE = "OnTranslationChange";
        // A price boost saved, changed or removed in Backoffice (r.uof.onpriceboost). OE.Adapter.Service
        // forwards it to the OE.Sportsbook.Service shard of the fixture, which stamps the boost onto
        // the fixture, market and selection documents (see OnPriceBoostMessage).
        public const string ON_PRICE_BOOST = "OnPriceBoost";

        // Internal
        public const string ON_ODDS_CHANGE = "OnOddsChange";
        public const string ON_BET_SETTLEMENT = "OnBetSettlement";
        public const string ON_BET_CANCELLATION = "OnBetCancellation";

    }
}

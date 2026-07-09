using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;
using OE.Shared.Utilities;

namespace OE.Translation.Service.Repositories
{
    // Owns ALL translation writes into the shared sportsbook MongoDB (2026-07 translation-flow
    // redesign — moved out of OE.Sportsbook.Service):
    //   * live path: stamps the per-language *NaTr arrays onto documents as raw BG.Receiver
    //     messages flow (translations resolved from the preloaded TranslationCache2 snapshot;
    //     update-only — OE.Sportsbook.Service's upsert creates the documents), and
    //   * change path: applies ON_TRANSLATION_CHANGE deltas across all affected documents and
    //     emits a translation-only ON_ODDS_CHANGE per affected OPEN fixture.
    public interface IMongoTranslationRepository
    {
        void WriteFixtureTranslations(OnFixtureChangeMessage message);

        void WriteMarketTranslations(OnMarketChangeMessage message);

        void ProcessTranslationChange(IMessageBroker messageBroker, OnTranslationChangeMessage message, string correlationId);

        // One-time migration support (Custom:Translation:SeedActiveSnapshot): every currently-active
        // sport/segment/league/team/market-template/selection-template name, keyed exactly like the
        // live seeding path (SpId:/SegId:/LeId:/Te:/MaTe:/SeTe:), for entities the message-driven
        // seeding never saw (e.g. CacheSeed was off when they last changed).
        List<(string Id, string Text)> ReadActiveSnapshot();

        // Mongo-directed counterpart to ReadActiveSnapshot/SeedActiveSnapshotAsync
        // (Custom:Translation:BackfillActiveSnapshot): the live path only stamps *NaTr when a
        // message happens to arrive for that specific entity (WriteFixtureTranslations/
        // WriteMarketTranslations), so an entity whose TranslationCache2 row was translated while
        // it was quiet (no message since) can sit un-backfilled indefinitely. Scans every
        // currently-active Fixture/Market/Selection once at startup and fills in only the *NaTr
        // fields still missing that the preloaded cache already has a translation for.
        void BackfillActiveSnapshot();
    }
}

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
    }
}

using OE.Shared.Messages.OutgoingAdapterMessages;

namespace OE.Translation.Service.Repositories
{
    // SQL source-side seeding (ported from bet-genius BG.Translation.Service, 2026-07
    // translation-flow redesign): harvests English names from the raw BG.Receiver stream into the
    // flat TranslationCache2 (source columns only — the external translation pipeline owns the
    // TARGET_TEXT_* columns; empty target = untranslated worklist).
    public interface ITranslationRepository
    {
        Task OnProcessFixtureSetUpdate(OnFixtureChangeMessage message);

        Task OnProcessMarketSetUpdate(OnMarketChangeMessage message);

        // One-time migration (Custom:Translation:SeedActiveSnapshot): insert-only seeding for
        // entities from IMongoTranslationRepository.ReadActiveSnapshot that have no TranslationCache2
        // row at all yet. Never touches a row that already exists, even if its SOURCE_TEXT differs
        // from the live value — that update-on-change path belongs to the message-driven seeding
        // above, not this startup sweep.
        Task SeedActiveSnapshotAsync(List<(string id, string value)> items);
    }
}

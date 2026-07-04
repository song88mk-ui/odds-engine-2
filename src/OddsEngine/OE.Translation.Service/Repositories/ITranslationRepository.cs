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
    }
}

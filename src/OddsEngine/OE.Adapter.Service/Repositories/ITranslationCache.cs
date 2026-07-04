using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Adapter.Service.Repositories
{
    // In-memory snapshot of the flat TranslationCache2 table (Translation DB), keyed by STRING_ID
    // ("SpId:{id}" | "SegId:{id}" | "LeId:{id}" | "Te:{teamId}" | "MaTe:{MaTeId}" |
    // "SeTe:{MaTeId}:{SeTeId}"). Preloaded once at startup, then kept current via
    // ON_TRANSLATION_CHANGE messages (Platform.CacheSync.Service) — no per-message SQL reads.
    public interface ITranslationCache
    {
        // Loads the whole TranslationCache2 table into memory. Called once before the message
        // broker starts consuming; throws on failure so the caller can retry/crash.
        void Preload();

        // Returns the translations for a STRING_ID, or null when none exist — callers assign the
        // result directly to the message *Tr properties (null means "no translation").
        List<TranslationMessage> GetTranslations(string stringId);

        // Applies an ON_TRANSLATION_CHANGE payload on top of the preloaded snapshot. An empty
        // translated text removes that language entry.
        void Apply(List<TranslationChangeItem> items);
    }
}

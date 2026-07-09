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
        // result directly to the message *Tr properties (null means "no translation"). currentText
        // is the freshly-arrived English source for this STRING_ID: when it doesn't match what was
        // cached at preload (a rename, or the STRING_ID was never cached at all), this falls back to
        // looking up currentText itself in the content-addressed index, reusing an existing
        // translation for identical text under a different STRING_ID instead of returning null.
        List<TranslationMessage> GetTranslations(string stringId, string currentText);

        // Applies an ON_TRANSLATION_CHANGE payload on top of the preloaded snapshot. An empty
        // translated text removes that language entry.
        void Apply(List<TranslationChangeItem> items);
    }
}

using OE.Shared.Messages.OutgoingAdapterMessages;
using OE.Shared.Messages.OutgoingSportsbookMessages;

namespace OE.Translation.Service.Repositories
{
    // In-memory snapshot of the flat TranslationCache2 table (same design as
    // OE.Adapter.Service's cache — kept as a per-service copy on purpose; promote to OE.Shared
    // only with a coordinated package bump).
    public interface ITranslationCache
    {
        void Preload();

        // currentText is the freshly-arrived English source for stringId: when it doesn't match
        // what was cached at preload (a rename, or the STRING_ID was never cached at all), this
        // falls back to a content-addressed lookup by currentText itself, reusing an existing
        // translation for identical text under a different STRING_ID instead of returning null.
        List<TranslationMessage>? GetTranslations(string stringId, string currentText);

        void Apply(List<TranslationChangeItem> items);
    }
}

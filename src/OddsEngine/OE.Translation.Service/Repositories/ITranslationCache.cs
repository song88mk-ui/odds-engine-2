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

        List<TranslationMessage>? GetTranslations(string stringId);

        void Apply(List<TranslationChangeItem> items);
    }
}

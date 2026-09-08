using System.Collections.Generic;

namespace OE.Shared.Messages.OutgoingSportsbookMessages
{
    // Payload-carrying successor to ON_DYNAMIC_TRANSLATION_CHANGE (which is purge-only, see
    // OnDeleteCacheMessage). Flow: external translation script updates TranslationCache2, then
    // calls Platform.API POST v1/cachepreload -> Platform.CacheSync.Service publishes this to the
    // OE.Adapter.Service instances (r.oe.adapter.ontranslationchange.{NN}) so each adapter updates
    // its in-memory translation cache without re-reading SQL. OE.Adapter forwards it to
    // OE.Sportsbook.Service, which persists the values to MongoDB and emits translation-only
    // ON_ODDS_CHANGE payloads for affected fixtures.
    public class OnTranslationChangeMessage
    {
        public List<TranslationChangeItem> Tr { get; set; } = new List<TranslationChangeItem>();
    }

    public class TranslationChangeItem
    {
        // TranslationCache2.STRING_ID — glossary key formats:
        // "SpId:{id}" | "SegId:{id}" | "LeId:{id}" | "Te:{teamId}" | "MaTe:{MaTeId}" |
        // "MaTe:{MaTeId}~{wordingHash}" (per-wording variant, see MarketTemplateVariantId) |
        // "SeTe:{MaTeId}:{SeTeId}"
        public string StId { get; set; }
        public string LaCo { get; set; } // target language code, e.g. "ZH" | "ES" | "FR"
        public string Tx { get; set; }   // translated text; empty string clears the translation

        // The row's SOURCE_TEXT (additive, 2026-07-16; may be null from older senders). Lets the
        // in-memory caches keep their content-addressed index current without a restart, and lets
        // the Mongo change path scope a MaTe wording-variant update to exactly the markets whose
        // MaNa carries that wording. Consumers must tolerate null.
        public string SrcTx { get; set; }
    }
}

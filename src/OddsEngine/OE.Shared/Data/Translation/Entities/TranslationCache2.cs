using System;

namespace OE.Shared.Data.Translation.Entities
{
    // Flat translation store owned by the bet-genius side (BG.Shared defines the DDL): one row per
    // STRING_ID with a fixed column per target language. BG.Translation.Service seeds the source
    // columns; the external translation pipeline fills TARGET_TEXT_*. Read-only from odds-engine-2:
    // OE.Adapter.Service preloads the whole table into its in-memory translation cache at startup
    // (2026-07 translation-flow redesign). Empty target text means "not translated yet".
    public class TranslationCache2
    {
        public long Id { get; set; }

        public string StringId { get; set; }

        public string SourceLanguage { get; set; }

        public string SourceText { get; set; }

        public string TargetTextZh { get; set; }

        public string TargetTextEs { get; set; }

        public string TargetTextFr { get; set; }

        public bool HasBetPlacement { get; set; }

        public DateTime? CreatedAt { get; set; }

        public int? GlossaryVersion { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}

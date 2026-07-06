using System;

namespace OE.Shared.Data.Translation.Entities
{
    // Read-only mapping of the EXTERNAL TranslationCache table (owned by the external translation
    // pipeline — no DDL for it lives in this repo; schema carried over from the legacy bet-genius
    // mapping). PK (SOURCE_HASH, SOURCE_LANGUAGE, TARGET_LANGUAGE); SOURCE_HASH is
    // CONVERT(VARCHAR(64), HASHBYTES('SHA2_256', SOURCE_TEXT), 2) — SHA-256 over the NVARCHAR
    // (UTF-16LE) source text as uppercase hex. Queried only by OE.Translation.Service's temporary
    // Custom:Translation:CacheBackfill bridge; never written from this codebase. Named
    // LegacyTranslationCache (table: TranslationCache) so it can't be confused with the services'
    // in-memory TranslationCache classes.
    public class LegacyTranslationCache
    {
        public string SourceHash { get; set; }

        public string SourceLanguage { get; set; }

        public string TargetLanguage { get; set; }

        public string? SourceText { get; set; }

        public string? TargetText { get; set; }

        public DateTime? CreatedAt { get; set; }

        public int? GlossaryVersion { get; set; }

        public DateTime? UpdatedAt { get; set; }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace OE.Shared.Utilities
{
    // The feed emits MULTIPLE concurrent wordings for one market template id (verified in prod
    // 2026-07-16: brand-new MaTe 259 markets arrive as both "Total Goals Over / Under" and
    // "Over/Under" at the same time — different sports share the template id). TranslationCache2
    // keys one row per STRING_ID, so a single "MaTe:{id}" row can only ever match one of the
    // wordings — the seeder used to rename+wipe that row back and forth, churning DeepL and
    // leaving the losing wording untranslated.
    //
    // Instead, each additional wording gets its own VARIANT row: "MaTe:{id}~{hash}", where the
    // hash is derived from the case/whitespace-normalized wording so every service computes the
    // same id without coordination. The in-memory caches serve variants through their
    // content-addressed (SOURCE_TEXT -> translations) index, so lookups never need to construct
    // a variant id — only the seeder (to insert) and the Mongo change path (to strip the suffix
    // and scope by wording) use this helper.
    //
    // '~' is safe as a separator: no existing STRING_ID format contains it, and the change
    // path's prefix parsing ("MaTe" + first ':') is unaffected.
    public static class MarketTemplateVariantId
    {
        private const char Separator = '~';

        // "MaTe:{maTeId}~{hash8}" for the given wording. Deterministic and normalization-stable:
        // case and surrounding whitespace do not produce different variants.
        public static string Compute(string maTeId, string wording)
        {
            return $"MaTe:{maTeId}{Separator}{Hash8(wording)}";
        }

        // Strips the "~{hash}" suffix from a MaTe id segment (the part AFTER the "MaTe:" prefix).
        // Returns the base id unchanged when there is no variant suffix.
        public static string GetBaseId(string idSegment)
        {
            if (string.IsNullOrEmpty(idSegment))
                return idSegment;

            var separator = idSegment.IndexOf(Separator);

            return separator < 0 ? idSegment : idSegment.Substring(0, separator);
        }

        public static bool IsVariant(string idSegment)
        {
            return !string.IsNullOrEmpty(idSegment) && idSegment.IndexOf(Separator) >= 0;
        }

        private static string Hash8(string wording)
        {
            var normalized = (wording ?? string.Empty).Trim().ToLowerInvariant();

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));

                var sb = new StringBuilder(8);
                for (var i = 0; i < 4; i++)
                    sb.Append(bytes[i].ToString("x2"));

                return sb.ToString();
            }
        }
    }
}

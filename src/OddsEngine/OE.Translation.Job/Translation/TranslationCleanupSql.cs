using System;
using System.Collections.Generic;
using System.Linq;

namespace OE.Translation.Job.Translation
{
    // Pure builders for the parameterized SQL used by TranslationCleanupRepository.
    // Extracted so the (safety-critical) SQL shape and parameterization can be unit tested
    // without a database. All data values are passed as {n} placeholders -> EF Core
    // parameterizes them; only the trusted integer batch size is inlined.
    //
    // TranslationCache2 is the single translation store (the retired SportsbookDynamic /
    // SportsbookGlossary tables are gone): one row per STRING_ID with HAS_BET_PLACEMENT and
    // UPDATED_AT/CREATED_AT carrying the protection and retention signals directly.
    public static class TranslationCleanupSql
    {
        // SQL Server hard parameter ceiling is 2100; chunk flag updates well below it.
        public const int FlagChunkSize = 500;

        /// <summary>
        /// UPDATE that flags a chunk of STRING_IDs as referenced by a placed bet.
        /// Parameters are the STRING_ID values (positional {0..n-1}).
        /// </summary>
        public static (string Sql, object[] Parameters) BuildFlagUpdate(IReadOnlyCollection<string> stringIds)
        {
            if (stringIds == null || stringIds.Count == 0)
                return (string.Empty, Array.Empty<object>());

            var placeholders = string.Join(",", stringIds.Select((_, i) => $"{{{i}}}"));

            var sql =
                "UPDATE TranslationCache2 " +
                "SET HAS_BET_PLACEMENT = 1 " +
                $"WHERE STRING_ID IN ({placeholders}) AND HAS_BET_PLACEMENT = 0";

            return (sql, stringIds.Cast<object>().ToArray());
        }

        /// <summary>
        /// Batched DELETE of stale, unflagged TranslationCache2 rows. Rows the feed still emits
        /// are protected by the seeding path's liveness touch (UPDATED_AT); rows referenced by a
        /// placed bet by HAS_BET_PLACEMENT = 1. Parameters are the LIKE patterns (one per prefix)
        /// followed by the cutoff timestamp. batchSize is a trusted integer and is inlined into
        /// TOP(...).
        /// </summary>
        public static (string Sql, object[] Parameters) BuildDelete(
            IReadOnlyCollection<string> prefixes,
            int batchSize,
            DateTime cutoffUtc)
        {
            var likePatterns = (prefixes ?? Array.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => (object)(p + "%"))
                .ToList();

            if (likePatterns.Count == 0 || batchSize <= 0)
                return (string.Empty, Array.Empty<object>());

            var likeClause = string.Join(
                " OR ",
                likePatterns.Select((_, i) => $"STRING_ID LIKE {{{i}}}"));

            var cutoffIndex = likePatterns.Count;

            var sql =
                $"DELETE TOP ({batchSize}) FROM TranslationCache2 " +
                "WHERE HAS_BET_PLACEMENT = 0 " +
                $"AND ({likeClause}) " +
                $"AND COALESCE(UPDATED_AT, CREATED_AT) < {{{cutoffIndex}}}";

            var parameters = new List<object>(likePatterns) { cutoffUtc }.ToArray();

            return (sql, parameters);
        }
    }
}

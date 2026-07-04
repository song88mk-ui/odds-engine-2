using System;
using System.Linq;
using OE.Translation.Job.Translation;

namespace OE.Translation.Job.Tests.Translation
{
    // TranslationCache2 is the single translation store (2026-07 translation-flow redesign) —
    // both builders target it directly.
    public class TranslationCleanupSqlTests
    {
        // ---- BuildFlagUpdate ----

        [Fact]
        public void BuildFlagUpdate_ParameterizesEachId_AndGuardsAlreadyFlagged()
        {
            var ids = new[] { "MaTe:1", "SeTe:1:2", "MaTe:3" };

            var (sql, parameters) = TranslationCleanupSql.BuildFlagUpdate(ids);

            Assert.Contains("UPDATE TranslationCache2", sql);
            Assert.Contains("SET HAS_BET_PLACEMENT = 1", sql);
            Assert.Contains("AND HAS_BET_PLACEMENT = 0", sql); // don't re-touch already-flagged rows
            Assert.Contains("STRING_ID IN ({0},{1},{2})", sql);
            Assert.Equal(ids, parameters.Cast<string>());
        }

        [Fact]
        public void BuildFlagUpdate_ReturnsEmpty_ForNoIds()
        {
            var (sql, parameters) = TranslationCleanupSql.BuildFlagUpdate(Array.Empty<string>());

            Assert.Equal(string.Empty, sql);
            Assert.Empty(parameters);
        }

        // ---- BuildDelete ----

        [Fact]
        public void BuildDelete_TargetsTranslationCache2_ExcludesFlagged_AndParameterizesData()
        {
            var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var (sql, parameters) = TranslationCleanupSql.BuildDelete(
                new[] { "MaTe:", "SeTe:", "Te:" }, batchSize: 2000, cutoffUtc: cutoff);

            Assert.Contains("DELETE TOP (2000) FROM TranslationCache2", sql);
            Assert.Contains("WHERE HAS_BET_PLACEMENT = 0", sql); // flagged rows are preserved
            Assert.Contains("STRING_ID LIKE {0} OR STRING_ID LIKE {1} OR STRING_ID LIKE {2}", sql);
            Assert.Contains("COALESCE(UPDATED_AT, CREATED_AT) < {3}", sql);

            // Patterns get a % suffix; cutoff is the final parameter.
            Assert.Equal(new object[] { "MaTe:%", "SeTe:%", "Te:%", cutoff }, parameters);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        public void BuildDelete_ReturnsEmpty_ForNonPositiveBatchSize(int batchSize)
        {
            var (sql, parameters) = TranslationCleanupSql.BuildDelete(
                new[] { "MaTe:" }, batchSize, DateTime.UtcNow);

            Assert.Equal(string.Empty, sql);
            Assert.Empty(parameters);
        }

        [Fact]
        public void BuildDelete_ReturnsEmpty_WhenNoUsablePrefixes()
        {
            var (sql, parameters) = TranslationCleanupSql.BuildDelete(
                new[] { "", "  " }, batchSize: 100, cutoffUtc: DateTime.UtcNow);

            Assert.Equal(string.Empty, sql);
            Assert.Empty(parameters);
        }
    }
}

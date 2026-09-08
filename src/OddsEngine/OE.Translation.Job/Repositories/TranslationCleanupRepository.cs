using OE.Shared.Data.Translation;
using OE.Translation.Job.Translation;
using Microsoft.EntityFrameworkCore;

namespace OE.Translation.Job.Repositories
{
    public class TranslationCleanupRepository : ITranslationCleanupRepository
    {
        private readonly TranslationContext _context;
        private readonly ILogger<TranslationCleanupRepository> _logger;

        public TranslationCleanupRepository(
            TranslationContext context,
            ILogger<TranslationCleanupRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> FlagBetPlacementAsync(
            IReadOnlyCollection<string> stringIds,
            CancellationToken cancellationToken)
        {
            if (stringIds == null || stringIds.Count == 0)
                return 0;

            var distinct = stringIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();

            if (distinct.Count == 0)
                return 0;

            var affected = 0;

            // Chunk so the IN (...) parameter list never approaches the SQL Server limit.
            foreach (var chunk in Chunk(distinct, TranslationCleanupSql.FlagChunkSize))
            {
                var (sql, parameters) = TranslationCleanupSql.BuildFlagUpdate(chunk);

                affected += await _context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
            }

            return affected;
        }

        public Task<int> CleanupAsync(
            IReadOnlyCollection<string> prefixes,
            int retentionDays,
            int batchSize,
            CancellationToken cancellationToken)
        {
            if (retentionDays <= 0 || batchSize <= 0)
                return Task.FromResult(0);

            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var (sql, parameters) = TranslationCleanupSql.BuildDelete(prefixes, batchSize, cutoff);

            return RunBatchedDeleteAsync(sql, parameters, batchSize, cancellationToken);
        }

        private async Task<int> RunBatchedDeleteAsync(
            string sql,
            object[] parameters,
            int batchSize,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(sql))
                return 0;

            var totalDeleted = 0;
            var safetyIterationCap = 10000; // backstop against an unexpected infinite loop

            for (var i = 0; i < safetyIterationCap; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // DELETE TOP (batch) keeps each transaction small and avoids lock escalation.
                var deleted = await _context.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);

                totalDeleted += deleted;

                if (deleted < batchSize)
                    break;
            }

            return totalDeleted;
        }

        private static IEnumerable<List<string>> Chunk(List<string> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
                yield return source.GetRange(i, Math.Min(size, source.Count - i));
        }
    }
}

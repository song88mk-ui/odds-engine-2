namespace OE.Translation.Job.Repositories
{
    public interface ITranslationCleanupRepository
    {
        /// <summary>
        /// Marks every TranslationCache2 row whose STRING_ID is in <paramref name="stringIds"/>
        /// as referenced by a placed bet, so the cleanup job will never remove it. No-op for ids
        /// already flagged.
        /// </summary>
        Task<int> FlagBetPlacementAsync(IReadOnlyCollection<string> stringIds, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes stale, unflagged TranslationCache2 rows in batches. Rows with
        /// HAS_BET_PLACEMENT = 1 are excluded. Returns the total number of rows removed.
        /// </summary>
        Task<int> CleanupAsync(
            IReadOnlyCollection<string> prefixes,
            int retentionDays,
            int batchSize,
            CancellationToken cancellationToken);
    }
}

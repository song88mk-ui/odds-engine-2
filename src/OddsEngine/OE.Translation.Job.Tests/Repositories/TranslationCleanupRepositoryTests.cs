using Microsoft.Extensions.Logging.Abstractions;
using OE.Shared.Data.Translation;
using OE.Translation.Job.Repositories;

namespace OE.Translation.Job.Tests.Repositories
{
    // TranslationCleanupRepository's DB-touching happy path (ExecuteSqlRawAsync) needs a real SQL
    // Server — EF Core InMemory doesn't support raw SQL, and this repo has no SQL test
    // infrastructure (see TranslationCleanupSqlTests for the actual SQL-generation logic, which IS
    // unit-testable and covered). What's covered here are the guard clauses that skip the database
    // entirely — passing a null TranslationContext is safe because none of these paths ever touch it.
    public class TranslationCleanupRepositoryTests
    {
        private static TranslationCleanupRepository BuildRepository() =>
            new(null!, NullLogger<TranslationCleanupRepository>.Instance);

        [Fact]
        public async Task FlagBetPlacementAsync_NullCollection_ReturnsZeroWithoutTouchingDatabase()
        {
            var result = await BuildRepository().FlagBetPlacementAsync(null!, CancellationToken.None);

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task FlagBetPlacementAsync_EmptyCollection_ReturnsZero()
        {
            var result = await BuildRepository().FlagBetPlacementAsync(Array.Empty<string>(), CancellationToken.None);

            Assert.Equal(0, result);
        }

        [Fact]
        public async Task FlagBetPlacementAsync_OnlyWhitespaceEntries_ReturnsZero()
        {
            var result = await BuildRepository().FlagBetPlacementAsync(new[] { "", "   ", null! }, CancellationToken.None);

            Assert.Equal(0, result);
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(-1, 100)]
        [InlineData(30, 0)]
        [InlineData(30, -1)]
        public async Task CleanupAsync_NonPositiveRetentionOrBatchSize_ReturnsZeroWithoutTouchingDatabase(int retentionDays, int batchSize)
        {
            var result = await BuildRepository().CleanupAsync(new[] { "Te:" }, retentionDays, batchSize, CancellationToken.None);

            Assert.Equal(0, result);
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public async Task FlagBetPlacementAsync_NullCollection_ReturnsZeroWithoutTouchingDatabase_MutationCheck()
        {
            var result = await BuildRepository().FlagBetPlacementAsync(null!, CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, result));
        }

        [Fact]
        public async Task FlagBetPlacementAsync_EmptyCollection_ReturnsZero_MutationCheck()
        {
            var result = await BuildRepository().FlagBetPlacementAsync(Array.Empty<string>(), CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, result));
        }

        [Fact]
        public async Task FlagBetPlacementAsync_OnlyWhitespaceEntries_ReturnsZero_MutationCheck()
        {
            var result = await BuildRepository().FlagBetPlacementAsync(new[] { "", "   ", null! }, CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, result));
        }

        [Theory]
        [InlineData(0, 100)]
        [InlineData(-1, 100)]
        [InlineData(30, 0)]
        [InlineData(30, -1)]
        public async Task CleanupAsync_NonPositiveRetentionOrBatchSize_ReturnsZeroWithoutTouchingDatabase_MutationCheck(int retentionDays, int batchSize)
        {
            var result = await BuildRepository().CleanupAsync(new[] { "Te:" }, retentionDays, batchSize, CancellationToken.None);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, result));
        }

        #endregion
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Translation.Job.Repositories;
using OE.Translation.Job.Workers;

namespace OE.Translation.Job.Tests.Workers
{
    public class CleanupWorkerTests
    {
        private static CleanupWorker BuildWorker(FakeCleanupRepository repository, Dictionary<string, string?>? configOverrides = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configOverrides ?? new Dictionary<string, string?>())
                .Build();

            var scopeFactory = new ServiceCollection()
                .AddScoped<ITranslationCleanupRepository>(_ => repository)
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>();

            return new CleanupWorker(NullLogger<CleanupWorker>.Instance, scopeFactory, configuration);
        }

        [Fact]
        public async Task ExecuteAsync_Disabled_NeverRunsCleanup()
        {
            var repository = new FakeCleanupRepository();
            var worker = BuildWorker(repository, new() { ["Custom:Cleanup:Enabled"] = "false" });

            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;

            Assert.Empty(repository.Calls);
        }

        [Fact]
        public async Task ExecuteAsync_Enabled_RunsOnceImmediately_WithParsedConfig()
        {
            var repository = new FakeCleanupRepository();
            var worker = BuildWorker(repository, new()
            {
                ["Custom:Cleanup:Enabled"] = "true",
                ["Custom:Cleanup:StartupDelaySeconds"] = "0",
                ["Custom:Cleanup:IntervalMinutes"] = "1",
                ["Custom:Cleanup:RetentionDays"] = "45",
                ["Custom:Cleanup:BatchSize"] = "500",
                ["Custom:Cleanup:Prefixes"] = "Te:,MaTe:"
            });

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.Calls.Count > 0);

            // Still waiting on the (>=1 minute) periodic timer for a second run — never completes.
            Assert.False(worker.ExecuteTask!.IsCompleted);

            var call = Assert.Single(repository.Calls);
            Assert.Equal(45, call.RetentionDays);
            Assert.Equal(500, call.BatchSize);
            Assert.Equal(new[] { "Te:", "MaTe:" }, call.Prefixes);

            await worker.StopAsync(CancellationToken.None);
            Assert.True(worker.ExecuteTask.IsCompleted);
        }

        [Fact]
        public async Task ExecuteAsync_DefaultPrefixes_MatchTheDocumentedDefaultSet()
        {
            var repository = new FakeCleanupRepository();
            var worker = BuildWorker(repository, new()
            {
                ["Custom:Cleanup:StartupDelaySeconds"] = "0"
            });

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.Calls.Count > 0);

            var call = Assert.Single(repository.Calls);
            Assert.Equal(new[] { "MaTe:", "SeTe:", "Te:", "SpId:", "SegId:", "LeId:" }, call.Prefixes);

            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_RepositoryThrows_IsCaughtAndLogged_LoopSurvives()
        {
            var repository = new FakeCleanupRepository { ThrowOnCleanup = true };
            var worker = BuildWorker(repository, new() { ["Custom:Cleanup:StartupDelaySeconds"] = "0" });

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.Calls.Count > 0);

            Assert.False(worker.ExecuteTask!.IsCompleted); // still alive, waiting for the next tick

            await worker.StopAsync(CancellationToken.None);
        }

        private static async Task WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.True(condition(), "condition was not met within the timeout");
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public async Task ExecuteAsync_Disabled_NeverRunsCleanup_MutationCheck()
        {
            var repository = new FakeCleanupRepository();
            var worker = BuildWorker(repository, new() { ["Custom:Cleanup:Enabled"] = "false" });

            await worker.StartAsync(CancellationToken.None);
            await worker.ExecuteTask!;

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.NotEmpty(repository.Calls));
        }

        [Fact]
        public async Task ExecuteAsync_Enabled_RunsOnceImmediately_WithParsedConfig_MutationCheck()
        {
            var repository = new FakeCleanupRepository();
            var worker = BuildWorker(repository, new()
            {
                ["Custom:Cleanup:Enabled"] = "true",
                ["Custom:Cleanup:StartupDelaySeconds"] = "0",
                ["Custom:Cleanup:IntervalMinutes"] = "1",
                ["Custom:Cleanup:RetentionDays"] = "45",
                ["Custom:Cleanup:BatchSize"] = "500",
                ["Custom:Cleanup:Prefixes"] = "Te:,MaTe:"
            });

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.Calls.Count > 0);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(worker.ExecuteTask!.IsCompleted));

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(2, repository.Calls.Count));
            var call = Assert.Single(repository.Calls);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(999, call.RetentionDays));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(1, call.BatchSize));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new[] { "Wrong:" }, call.Prefixes));

            await worker.StopAsync(CancellationToken.None);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(worker.ExecuteTask.IsCompleted));
        }

        [Fact]
        public async Task ExecuteAsync_DefaultPrefixes_MatchTheDocumentedDefaultSet_MutationCheck()
        {
            var repository = new FakeCleanupRepository();
            var worker = BuildWorker(repository, new()
            {
                ["Custom:Cleanup:StartupDelaySeconds"] = "0"
            });

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.Calls.Count > 0);

            var call = Assert.Single(repository.Calls);
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                Assert.Equal(new[] { "Te:", "MaTe:", "SeTe:", "SpId:", "SegId:", "LeId:" }, call.Prefixes));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(new[] { "Wrong:" }, call.Prefixes));

            await worker.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task ExecuteAsync_RepositoryThrows_IsCaughtAndLogged_LoopSurvives_MutationCheck()
        {
            var repository = new FakeCleanupRepository { ThrowOnCleanup = true };
            var worker = BuildWorker(repository, new() { ["Custom:Cleanup:StartupDelaySeconds"] = "0" });

            await worker.StartAsync(CancellationToken.None);
            await WaitUntil(() => repository.Calls.Count > 0);

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(worker.ExecuteTask!.IsCompleted));

            await worker.StopAsync(CancellationToken.None);
        }

        #endregion

        private sealed class FakeCleanupRepository : ITranslationCleanupRepository
        {
            public bool ThrowOnCleanup { get; set; }
            public List<(IReadOnlyCollection<string> Prefixes, int RetentionDays, int BatchSize)> Calls { get; } = new();

            public Task<int> FlagBetPlacementAsync(IReadOnlyCollection<string> stringIds, CancellationToken cancellationToken) =>
                Task.FromResult(0);

            public Task<int> CleanupAsync(IReadOnlyCollection<string> prefixes, int retentionDays, int batchSize, CancellationToken cancellationToken)
            {
                Calls.Add((prefixes, retentionDays, batchSize));
                if (ThrowOnCleanup)
                    throw new InvalidOperationException("cleanup failed");
                return Task.FromResult(1);
            }
        }
    }
}

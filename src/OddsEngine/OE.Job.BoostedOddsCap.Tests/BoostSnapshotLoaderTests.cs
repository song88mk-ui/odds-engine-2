using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Tests
{
    // The boosts and caps are what the job exists for and the recipients only serve the optional email, so a failed
    // recipients read must never cost the job its boost index (and with it the toast), while a failed boosts read
    // must still fail the load so the previous snapshot stays in use.
    public class BoostSnapshotLoaderTests
    {
        private sealed class FakeTradingReader : ITradingBoostReader
        {
            public bool Throws;

            public Task<TradingBoostData> ReadAsync(CancellationToken cancellationToken)
            {
                if (Throws)
                    throw new InvalidOperationException("Trading is down");

                return Task.FromResult(new TradingBoostData
                {
                    Boosts = new[]
                    {
                        new PriceBoostRow
                        {
                            ClientCode = "S88", ProviderFixtureId = TestMessages.FixtureId, MarketTemplateId = "1",
                            SelectionTemplateId = "1", BoostedOdds = 2.10m
                        }
                    },
                    Settings = new[] { new BoostedSettingsRow { ClientCode = "S88", Cap = 0.05m } }
                });
            }
        }

        private sealed class FakeRecipientReader : IRecipientReader
        {
            public List<ClientRecipientRow> Rows = new List<ClientRecipientRow>();
            public bool Throws;
            public bool ThrowsCancelled;

            public Task<IReadOnlyList<ClientRecipientRow>> ReadAsync(CancellationToken cancellationToken)
            {
                if (ThrowsCancelled)
                    throw new OperationCanceledException(cancellationToken);

                if (Throws)
                    throw new InvalidOperationException("Backoffice is down");

                return Task.FromResult<IReadOnlyList<ClientRecipientRow>>(Rows.ToList());
            }
        }

        private static ClientRecipientRow Client(string brand, string email, bool notify = true)
            => new ClientRecipientRow { Brand = brand, Email = email, IsEmailNotification = notify };

        private readonly FakeTradingReader _trading = new FakeTradingReader();
        private readonly FakeRecipientReader _recipients = new FakeRecipientReader();

        private BoostSnapshotLoader NewLoader(IRecipientReader recipients = null)
            => new BoostSnapshotLoader(_trading, recipients ?? _recipients, NullLogger<BoostSnapshotLoader>.Instance);

        [Fact]
        public async Task LoadsTheBoostsAndTheRecipientsTogether()
        {
            _recipients.Rows.Add(Client("S88", "ops@example.com"));

            var snapshot = await NewLoader().LoadAsync(CancellationToken.None);

            Assert.Equal(1, snapshot.RuleCount);
            Assert.Equal(new[] { "ops@example.com" }, snapshot.RecipientsFor("S88"));
        }

        [Fact]
        public async Task StillLoadsTheBoostsWhenTheRecipientsCannotBeRead()
        {
            _recipients.Throws = true;

            var snapshot = await NewLoader().LoadAsync(CancellationToken.None);

            Assert.Equal(1, snapshot.RuleCount);
            Assert.Empty(snapshot.RecipientsFor("S88"));
        }

        [Fact]
        public async Task KeepsThePreviousRecipientsWhenTheNextReadFails()
        {
            var loader = NewLoader();
            _recipients.Rows.Add(Client("S88", "ops@example.com"));
            await loader.LoadAsync(CancellationToken.None);

            _recipients.Throws = true;
            var snapshot = await loader.LoadAsync(CancellationToken.None);

            Assert.Equal(new[] { "ops@example.com" }, snapshot.RecipientsFor("S88"));
        }

        [Fact]
        public async Task ReplacesTheRecipientsOnceTheReadWorksAgain()
        {
            var loader = NewLoader();
            _recipients.Rows.Add(Client("S88", "old@example.com"));
            await loader.LoadAsync(CancellationToken.None);

            _recipients.Throws = true;
            await loader.LoadAsync(CancellationToken.None);

            _recipients.Throws = false;
            _recipients.Rows.Clear();
            _recipients.Rows.Add(Client("S88", "new@example.com"));
            var recovered = await loader.LoadAsync(CancellationToken.None);

            Assert.Equal(new[] { "new@example.com" }, recovered.RecipientsFor("S88"));
        }

        [Fact]
        public async Task StopsEmailingAClientWhoseSwitchWasTurnedOff()
        {
            var loader = NewLoader();
            _recipients.Rows.Add(Client("S88", "ops@example.com"));
            await loader.LoadAsync(CancellationToken.None);

            _recipients.Rows.Clear();   // the read only returns clients with the switch on
            var snapshot = await loader.LoadAsync(CancellationToken.None);

            Assert.Empty(snapshot.RecipientsFor("S88"));
        }

        [Fact]
        public async Task FailsTheLoadWhenTheBoostsCannotBeRead()
        {
            _trading.Throws = true;

            await Assert.ThrowsAsync<InvalidOperationException>(() => NewLoader().LoadAsync(CancellationToken.None));
        }

        [Fact]
        public async Task DoesNotSwallowACancellation()
        {
            _recipients.ThrowsCancelled = true;
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewLoader().LoadAsync(cancellation.Token));
        }

        [Fact]
        public async Task EmailOffMeansNobodyIsRead()
        {
            var snapshot = await NewLoader(new NoRecipientReader()).LoadAsync(CancellationToken.None);

            Assert.Equal(1, snapshot.RuleCount);
            Assert.Empty(snapshot.RecipientsFor("S88"));
        }

        [Fact]
        public async Task AMissingBackofficeConnectionStringLeavesTheBoostsWorking()
        {
            var snapshot = await NewLoader(new SqlRecipientReader(string.Empty, 30)).LoadAsync(CancellationToken.None);

            Assert.Equal(1, snapshot.RuleCount);
            Assert.Empty(snapshot.RecipientsFor("S88"));
        }

        [Fact]
        public async Task TheSqlReaderSaysWhatIsMissing()
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => new SqlRecipientReader("  ", 30).ReadAsync(CancellationToken.None));

            Assert.Contains("ConnectionStrings:Backoffice", failure.Message);
        }
    }
}

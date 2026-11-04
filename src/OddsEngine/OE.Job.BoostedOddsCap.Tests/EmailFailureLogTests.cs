using System.Net.Mail;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Alerts;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Data;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;

namespace OE.Job.BoostedOddsCap.Tests
{
    // A mail server that drops the connection says nothing about itself (DEV, 2026-11-04: "The connection was closed" while
    // the sender was being given), so the failure line has to say which server and settings were used. Names and flags only:
    // the user name and the password never reach the log.
    public class EmailFailureLogTests
    {
        private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

        private sealed class ThrowingSender : IAlertEmailSender
        {
            private readonly Exception _exception;

            public ThrowingSender(Exception exception) => _exception = exception;

            public Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken)
                => throw _exception;
        }

        private sealed class NoPublisher : INotificationPublisher
        {
            public void Publish(string payloadJson)
            {
            }
        }

        private sealed class CapturingLogger<T> : ILogger<T>
        {
            public readonly List<(LogLevel Level, string Message, Dictionary<string, object?> Values)> Entries = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = (state as IEnumerable<KeyValuePair<string, object?>>)?.ToDictionary(p => p.Key, p => p.Value)
                    ?? new Dictionary<string, object?>();

                Entries.Add((logLevel, formatter(state, exception), values));
            }
        }

        private static CapAlert Alert() => new CapAlert
        {
            ClientCode = "S88",
            ProviderFixtureId = TestMessages.FixtureId,
            SportId = 1,
            SportName = "Soccer",
            SegmentName = "International",
            LeagueName = "World Cup",
            FixtureName = "Team A vs. Team B",
            MarketName = "Match Winner",
            SelectionName = "Home",
            BoostedOdds = 2.10m,
            CurrentOdds = 2.00m,
            CapPercent = 5m,
            GapPercent = 2.44m,
            DetectedAtUtc = Now
        };

        private static (string Message, Dictionary<string, object?> Values) Failure(EmailOptions email, Exception exception)
        {
            var holder = new BoostSnapshotHolder();
            holder.Set(BoostSnapshot.Build(new PriceBoostRow[0], new BoostedSettingsRow[0], RecipientRules.Build(new ClientRecipientRow[0]), Now));

            var logger = new CapturingLogger<AlertDispatcher>();
            var dispatcher = new AlertDispatcher(new ToastFactory(new NotificationOptions()), new NoPublisher(), new ThrowingSender(exception),
                    email, new RecipientRateLimiter(email.MaxPerRecipientPerHour), holder, new Instrumentation(), logger);

            dispatcher.DispatchAsync(new[] { Alert() }, Now, CancellationToken.None).GetAwaiter().GetResult();

            var entry = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
            return (entry.Message, entry.Values);
        }

        private static EmailOptions Email(string username, string password) => new EmailOptions
        {
            Enabled = true,
            SmtpHost = "mail.example.net",
            SmtpPort = 2587,
            SmtpEnableSsl = true,
            SmtpUsername = username,
            SmtpPassword = password,
            OverrideRecipient = "qa@example.com"
        };

        [Fact]
        public void AFailedSendSaysWhichServerAndSettingsItUsed()
        {
            var email = Email("smtp-user-name", "smtp-pass-word");

            var (message, values) = Failure(email, new SmtpException(SmtpStatusCode.GeneralFailure, "Failure sending mail."));

            Assert.Equal("mail.example.net", values["SmtpHost"]);
            Assert.Equal(2587, values["SmtpPort"]);
            Assert.Equal(true, values["SmtpEnableSsl"]);
            Assert.Equal(true, values["HasLogin"]);
            Assert.Equal(email.From, values["From"]);
            Assert.Equal(SmtpStatusCode.GeneralFailure, values["SmtpStatus"]);
            Assert.Contains("mail.example.net:2587", message);
        }

        [Fact]
        public void TheLoginAndThePasswordNeverReachTheLog()
        {
            var (message, values) = Failure(Email("smtp-user-name", "smtp-pass-word"), new SmtpException("Failure sending mail."));

            Assert.DoesNotContain("smtp-user-name", message);
            Assert.DoesNotContain("smtp-pass-word", message);
            Assert.All(values.Values, value =>
            {
                Assert.DoesNotContain("smtp-user-name", value?.ToString() ?? string.Empty);
                Assert.DoesNotContain("smtp-pass-word", value?.ToString() ?? string.Empty);
            });
        }

        [Fact]
        public void WithoutAUserNameTheLogSaysThereWasNoLogin()
        {
            var (_, values) = Failure(Email(string.Empty, string.Empty), new SmtpException("Failure sending mail."));

            Assert.Equal(false, values["HasLogin"]);
        }

        [Fact]
        public void AFailureThatIsNotAnSmtpOneHasNoSmtpStatus()
        {
            var (_, values) = Failure(Email("smtp-user-name", "smtp-pass-word"), new InvalidOperationException("boom"));

            Assert.Null(values["SmtpStatus"]);
            Assert.Equal("mail.example.net", values["SmtpHost"]);
        }
    }
}

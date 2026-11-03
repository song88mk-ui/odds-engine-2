using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Alerts;

namespace OE.Job.BoostedOddsCap.Tests
{
    // The internal BCC copy of every client email: which addresses come out of Email:Bcc, who ends up on the message,
    // and that a typo in the list is reported instead of lost.
    public class SmtpAlertEmailSenderTests
    {
        private const string Client = "client@example.com";

        private static EmailOptions Options(string? bcc, string? overrideRecipient = null, bool enabled = true)
        {
            var values = new Dictionary<string, string?>
            {
                ["Email:Enabled"] = enabled.ToString(),
                ["Email:Smtp:Host"] = "smtp.example.com"
            };

            if (bcc != null)
                values["Email:Bcc"] = bcc;

            if (overrideRecipient != null)
                values["Email:OverrideRecipient"] = overrideRecipient;

            return EmailOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        }

        private static string[] Addresses(MailAddressCollection addresses) => addresses.Select(a => a.Address).ToArray();

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

        // --- Email:Bcc ---------------------------------------------------------------------------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Bcc_IsEmptyWhenTheSettingIsMissingOrBlank(string? setting)
        {
            var options = Options(setting);

            Assert.Empty(options.Bcc);
            Assert.Empty(options.BccRejected);
        }

        [Fact]
        public void Bcc_ReadsEveryAddressOfACommaSeparatedList_AndToleratesSpaces()
        {
            var options = Options("support@song88.com,  ops@example.com ,qa@example.com");

            Assert.Equal(new[] { "support@song88.com", "ops@example.com", "qa@example.com" }, options.Bcc);
            Assert.Empty(options.BccRejected);
        }

        [Fact]
        public void Bcc_AcceptsSemicolonsToo_AndDropsDuplicatesIgnoringCase()
        {
            var options = Options("support@song88.com; SUPPORT@song88.com, ops@example.com");

            Assert.Equal(new[] { "support@song88.com", "ops@example.com" }, options.Bcc);
        }

        [Fact]
        public void Bcc_DropsInvalidEntriesAndKeepsThemToBeReported()
        {
            var options = Options("support@song88.com, not-an-address, , ops@example.com");

            Assert.Equal(new[] { "support@song88.com", "ops@example.com" }, options.Bcc);
            Assert.Equal(new[] { "not-an-address" }, options.BccRejected);
        }

        // --- the message -------------------------------------------------------------------------------

        [Fact]
        public void BuildMessage_AddsTheInternalCopyAsBccOnly()
        {
            using var message = SmtpAlertEmailSender.BuildMessage(Options("support@song88.com, ops@example.com"), Client, "Subject", "<p>Body</p>");

            Assert.Equal(new[] { Client }, Addresses(message.To));
            Assert.Empty(message.CC);
            Assert.Equal(new[] { "support@song88.com", "ops@example.com" }, Addresses(message.Bcc));
        }

        [Fact]
        public void BuildMessage_WithNoBcc_SendsToTheClientOnly()
        {
            using var message = SmtpAlertEmailSender.BuildMessage(Options(null), Client, "Subject", "<p>Body</p>");

            Assert.Equal(new[] { Client }, Addresses(message.To));
            Assert.Empty(message.CC);
            Assert.Empty(message.Bcc);
        }

        [Fact]
        public void BuildMessage_WithAnOverrideRecipient_HasNoInternalCopy()
        {
            // the dispatcher passes the override as the recipient, so a test environment writes to one test address only
            using var message = SmtpAlertEmailSender.BuildMessage(Options("support@song88.com", overrideRecipient: "qa@example.com"),
                    "qa@example.com", "Subject", "<p>Body</p>");

            Assert.Equal(new[] { "qa@example.com" }, Addresses(message.To));
            Assert.Empty(message.Bcc);
        }

        [Fact]
        public void BuildMessage_WithABlankOverrideRecipient_StillAddsTheInternalCopy()
        {
            using var message = SmtpAlertEmailSender.BuildMessage(Options("support@song88.com", overrideRecipient: "  "), Client, "Subject", "<p>Body</p>");

            Assert.Equal(new[] { "support@song88.com" }, Addresses(message.Bcc));
        }

        [Fact]
        public void BuildMessage_ARecipientThatIsAnInternalAddress_IsNotCopiedTwice()
        {
            using var message = SmtpAlertEmailSender.BuildMessage(Options("support@song88.com, ops@example.com"), "Support@Song88.com", "Subject", "<p>Body</p>");

            Assert.Equal(new[] { "Support@Song88.com" }, Addresses(message.To));
            Assert.Equal(new[] { "ops@example.com" }, Addresses(message.Bcc));
        }

        [Fact]
        public void BuildMessage_KeepsTheSenderSubjectAndBody()
        {
            using var message = SmtpAlertEmailSender.BuildMessage(Options("support@song88.com"), Client, "Song88 | Price Boost Threshold Crossed", "<p>Body</p>");

            Assert.Equal("noreply@song88.com", message.From!.Address);
            Assert.Equal("Notifications", message.From.DisplayName);
            Assert.Equal("Song88 | Price Boost Threshold Crossed", message.Subject);
            Assert.Equal("<p>Body</p>", message.Body);
            Assert.True(message.IsBodyHtml);
        }

        // --- the warning about a typo ------------------------------------------------------------------

        [Fact]
        public void Sender_ReportsTheIgnoredBccEntriesOnce_WithoutPrintingThem()
        {
            var logger = new CapturingLogger<SmtpAlertEmailSender>();

            _ = new SmtpAlertEmailSender(Options("support@song88.com, typo-address"), logger);

            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Equal(1, entry.Values["RejectedCount"]);
            Assert.Equal(1, entry.Values["BccCount"]);
            Assert.DoesNotContain("typo-address", entry.Message);
        }

        [Fact]
        public void Sender_StaysQuietWhenEveryBccEntryIsValid()
        {
            var logger = new CapturingLogger<SmtpAlertEmailSender>();

            _ = new SmtpAlertEmailSender(Options("support@song88.com, ops@example.com"), logger);

            Assert.Empty(logger.Entries);
        }

        [Fact]
        public void Sender_StaysQuietWhenEmailIsOff()
        {
            var logger = new CapturingLogger<SmtpAlertEmailSender>();

            _ = new SmtpAlertEmailSender(Options("support@song88.com, typo-address", enabled: false), logger);

            Assert.Empty(logger.Entries);
        }
    }
}

using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using OE.Job.BoostedOddsCap.Alerts;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Data;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;
using OE.Shared.Test;

namespace OE.Job.BoostedOddsCap.Tests
{
    // The delivery half: what the toast says and who it is addressed to, what the digest email contains, how often a
    // recipient can be emailed, and that no failure in either channel escapes to the odds consumer.
    public class AlertDeliveryTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

        private static CapAlert Alert(string clientCode = "S88", string selectionName = "Home")
        {
            return new CapAlert
            {
                ClientCode = clientCode,
                ProviderFixtureId = TestMessages.FixtureId,
                SportId = 1,
                SportName = "Soccer",
                SegmentName = "International",
                LeagueName = "World Cup",
                FixtureName = "Team A vs. Team B",
                MarketName = "Match Winner",
                SelectionName = selectionName,
                BoostedOdds = 2.10m,
                CurrentOdds = 2.00m,
                CapPercent = 5m,
                GapPercent = 2.44m,
                DetectedAtUtc = Now
            };
        }

        private sealed class FakePublisher : INotificationPublisher
        {
            public readonly List<string> Payloads = new List<string>();
            public bool Throws;

            public void Publish(string payloadJson)
            {
                if (Throws)
                    throw new InvalidOperationException("broker down");

                Payloads.Add(payloadJson);
            }
        }

        private sealed class FakeEmailSender : IAlertEmailSender
        {
            public readonly List<(string To, string Subject, string Body)> Sent = new List<(string, string, string)>();
            public readonly HashSet<string> FailFor = new HashSet<string>();

            public Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken)
            {
                if (FailFor.Contains(toAddress))
                    throw new InvalidOperationException("mail server refused");

                Sent.Add((toAddress, subject, htmlBody));
                return Task.CompletedTask;
            }
        }

        private sealed class Harness
        {
            public readonly FakePublisher Publisher = new FakePublisher();
            public readonly FakeEmailSender Sender = new FakeEmailSender();
            public AlertDispatcher Dispatcher;

            public Harness(EmailOptions email, params ClientRow[] clients)
            {
                var holder = new BoostSnapshotHolder();
                holder.Set(BoostSnapshot.Build(new PriceBoostRow[0], new BoostedSettingsRow[0], clients, Now));

                Dispatcher = new AlertDispatcher(new ToastFactory(new NotificationOptions()), Publisher, Sender, email,
                        new RecipientRateLimiter(email.MaxPerRecipientPerHour), holder, new Instrumentation(),
                        NullLogger<AlertDispatcher>.Instance);
            }

            public Task Dispatch(params CapAlert[] alerts) => Dispatcher.DispatchAsync(alerts, Now, CancellationToken.None);
        }

        private static EmailOptions EmailOn(string overrideRecipient = "", int maxPerHour = 10, string smtpHost = "smtp.example.com")
            => new EmailOptions { Enabled = true, SmtpHost = smtpHost, OverrideRecipient = overrideRecipient, MaxPerRecipientPerHour = maxPerHour };

        private static ClientRow Client(string code, string email) => new ClientRow { ClientCode = code, Email = email };

        // --- toast -----------------------------------------------------------------------------------

        [Fact]
        public void Toast_IsAddressedToEveryUserWithTheCurrentManageBoostPage()
        {
            var toast = Assert.Single(new ToastFactory(new NotificationOptions()).Create(new[] { Alert() }));

            Assert.Null(toast.UserId);
            Assert.Equal("Price Boost", toast.Page);
            Assert.Equal("/hk/odds-engine/manage-boost", toast.PageRedirect);
            Assert.Equal("SBC.OE.PriceBoost.Manage", toast.ClaimType);
            Assert.Equal(1, toast.Type);
            Assert.Equal("S88", toast.ClientCode);
            Assert.Equal(1, toast.SportsId);
        }

        [Fact]
        public void Toast_IsTheJsonShapeBackofficeReads()
        {
            var toast = Assert.Single(new ToastFactory(new NotificationOptions()).Create(new[] { Alert() }));

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(toast));
            var root = json.RootElement;

            Assert.Equal(JsonValueKind.Null, root.GetProperty("UserId").ValueKind);
            Assert.Equal("Price Boost", root.GetProperty("Page").GetString());
            Assert.Equal("S88", root.GetProperty("ClientCode").GetString());
            Assert.True(Guid.TryParse(root.GetProperty("Id").GetString(), out _));
            foreach (var name in new[] { "SportsId", "Details", "PageRedirect", "ClaimType", "Type" })
                Assert.True(root.TryGetProperty(name, out _), name);
        }

        [Fact]
        public void Toast_SaysWhichBoostReachedItsCap()
        {
            var toast = Assert.Single(new ToastFactory(new NotificationOptions()).Create(new[] { Alert() }));

            Assert.Equal(
                "The boosted odds (<b>2.10</b>) for Soccer - International - World Cup - Match Winner - Home of <b>S88</b> "
                + "with CAP (<b>5.00%</b>) for the Current Odds <b>2.00</b> has been reached.",
                toast.Details);
        }

        [Fact]
        public void Toast_EncodesNamesFromTheFeedBecauseItIsRenderedAsMarkup()
        {
            var toast = Assert.Single(new ToastFactory(new NotificationOptions())
                    .Create(new[] { Alert(selectionName: "<script>alert(1)</script>") }));

            Assert.DoesNotContain("<script>", toast.Details);
            Assert.Contains("&lt;script&gt;", toast.Details);
        }

        [Fact]
        public void Toast_CollapsesALargeBatchForOneClientIntoASummary()
        {
            var alerts = Enumerable.Range(0, 6).Select(i => Alert(selectionName: $"Sel {i}")).ToList();

            var toast = Assert.Single(new ToastFactory(new NotificationOptions()).Create(alerts));

            Assert.Null(toast.SportsId);
            Assert.Contains("<b>6</b>", toast.Details);
            Assert.Contains("S88", toast.Details);
        }

        [Fact]
        public void Toast_KeepsABatchAtTheThresholdAsIndividualToasts()
        {
            var alerts = Enumerable.Range(0, 5).Select(i => Alert(selectionName: $"Sel {i}")).ToList();

            Assert.Equal(5, new ToastFactory(new NotificationOptions()).Create(alerts).Count);
        }

        [Fact]
        public void Toast_CountsEachClientSeparatelyAgainstTheThreshold()
        {
            var alerts = Enumerable.Range(0, 6).Select(i => Alert("S88", $"Sel {i}")).Append(Alert("TSO")).ToList();

            var toasts = new ToastFactory(new NotificationOptions()).Create(alerts);

            Assert.Equal(2, toasts.Count);
            Assert.Contains(toasts, t => t.ClientCode == "TSO" && t.SportsId == 1);
        }

        // --- email digest ----------------------------------------------------------------------------

        [Fact]
        public void Digest_SubjectCountsTheAlertsWhenThereAreSeveral()
        {
            var options = new EmailOptions();

            Assert.Equal("Song88 | Price Boost Threshold Crossed", EmailDigest.BuildSubject(options, 1));
            Assert.Equal("Song88 | Price Boost Threshold Crossed (3)", EmailDigest.BuildSubject(options, 3));
        }

        [Fact]
        public void Digest_ListsEveryAlertAndEncodesTheFeedsText()
        {
            var body = EmailDigest.BuildBody(new EmailOptions(),
                    new[] { Alert(selectionName: "<img src=x>"), Alert("TSO", "Away") });

            Assert.Contains("<td>TSO</td>", body);
            Assert.Contains("Match Winner - Away", body);
            Assert.Contains("2.10", body);
            Assert.Contains("2.00", body);
            Assert.DoesNotContain("<img src=x>", body);
            Assert.Contains("&lt;img src=x&gt;", body);
        }

        [Fact]
        public void Digest_LinksToManageBoostOnlyWhenTheBackofficeAddressIsConfigured()
        {
            Assert.DoesNotContain("manage-boost", EmailDigest.BuildBody(new EmailOptions(), new[] { Alert() }));

            var body = EmailDigest.BuildBody(new EmailOptions { BackofficeUrl = "https://bo.example.com/" }, new[] { Alert() });
            Assert.Contains("href=\"https://bo.example.com/hk/odds-engine/manage-boost\"", body);
        }

        // --- rate limit and queue --------------------------------------------------------------------

        [Fact]
        public void RateLimiter_AllowsTheConfiguredNumberPerRollingHour()
        {
            var limiter = new RecipientRateLimiter(2);

            Assert.True(limiter.TryAcquire("a@example.com", Now));
            Assert.True(limiter.TryAcquire("a@example.com", Now.AddMinutes(10)));
            Assert.False(limiter.TryAcquire("a@example.com", Now.AddMinutes(20)));

            // the first send has aged out of the hour
            Assert.True(limiter.TryAcquire("a@example.com", Now.AddMinutes(61)));
        }

        [Fact]
        public void RateLimiter_CountsEachRecipientSeparatelyAndIgnoresCase()
        {
            var limiter = new RecipientRateLimiter(1);

            Assert.True(limiter.TryAcquire("a@example.com", Now));
            Assert.False(limiter.TryAcquire("A@EXAMPLE.COM", Now));
            Assert.True(limiter.TryAcquire("b@example.com", Now));
        }

        [Fact]
        public void RateLimiter_IsOffWhenTheLimitIsZero()
        {
            var limiter = new RecipientRateLimiter(0);

            for (var i = 0; i < 50; i++)
                Assert.True(limiter.TryAcquire("a@example.com", Now));
        }

        [Fact]
        public void Queue_DropsTheNewestAlertWhenFullAndSaysSo()
        {
            var dropped = new List<CapAlert>();
            var queue = new AlertQueue(2, dropped.Add);
            var first = Alert(selectionName: "first");
            var second = Alert(selectionName: "second");
            var third = Alert(selectionName: "third");

            queue.Enqueue(first);
            queue.Enqueue(second);
            queue.Enqueue(third);

            Assert.Equal(new[] { third }, dropped);
            Assert.True(queue.Reader.TryRead(out var read1));
            Assert.True(queue.Reader.TryRead(out var read2));
            Assert.False(queue.Reader.TryRead(out _));
            Assert.Equal(new[] { first, second }, new[] { read1, read2 });
        }

        // --- publishing ------------------------------------------------------------------------------

        [Fact]
        public void BrokerPublisher_SendsTheNotificationBackofficeConsumesOnItsOwnChannel()
        {
            var broker = new FakeMessageBroker();
            var publisher = new BrokerNotificationPublisher(broker, new NotificationOptions());

            publisher.Publish("{\"Page\":\"Price Boost\"}");

            var sent = Assert.Single(broker.CPublished);
            Assert.Equal("{\"Page\":\"Price Boost\"}", sent.Payload);
            Assert.Equal("OnNewNotification", sent.MessageType);
            Assert.Equal("r.oe.bm.engine.notifications", sent.RoutingKey);
            Assert.True(Guid.TryParse(sent.CorrelationId, out _));

            // not the shared channel the consumer acknowledges on, which is not safe from a second thread
            Assert.Empty(broker.Published);
        }

        // --- dispatcher ------------------------------------------------------------------------------

        [Fact]
        public async Task Dispatcher_PublishesOneToastPerAlertAndNoEmailWhenEmailIsOff()
        {
            var harness = new Harness(new EmailOptions(), Client("S88", "ops@example.com"));

            await harness.Dispatch(Alert(), Alert(selectionName: "Away"));

            Assert.Equal(2, harness.Publisher.Payloads.Count);
            Assert.Empty(harness.Sender.Sent);
        }

        [Fact]
        public async Task Dispatcher_SendsOneDigestPerRecipient()
        {
            var harness = new Harness(EmailOn(), Client("S88", "ops@example.com"));

            await harness.Dispatch(Alert(), Alert(selectionName: "Away"));

            var mail = Assert.Single(harness.Sender.Sent);
            Assert.Equal("ops@example.com", mail.To);
            Assert.Equal("Song88 | Price Boost Threshold Crossed (2)", mail.Subject);
            Assert.Contains("Match Winner - Home", mail.Body);
            Assert.Contains("Match Winner - Away", mail.Body);
        }

        [Fact]
        public async Task Dispatcher_GivesARecipientOfSeveralClientsOneDigestWithAllTheirAlerts()
        {
            var harness = new Harness(EmailOn(), Client("S88", "ops@example.com"), Client("TSO", "ops@example.com"));

            await harness.Dispatch(Alert("S88"), Alert("TSO"));

            var mail = Assert.Single(harness.Sender.Sent);
            Assert.Contains("<td>S88</td>", mail.Body);
            Assert.Contains("<td>TSO</td>", mail.Body);
        }

        [Fact]
        public async Task Dispatcher_SendsToTheOverrideAddressInsteadOfTheClient()
        {
            var harness = new Harness(EmailOn(overrideRecipient: "qa@example.com"), Client("S88", "client@example.com"));

            await harness.Dispatch(Alert());

            Assert.Equal("qa@example.com", Assert.Single(harness.Sender.Sent).To);
        }

        [Fact]
        public async Task Dispatcher_SkipsAClientWithNoEmailAddress()
        {
            var harness = new Harness(EmailOn(), Client("S88", ""));

            await harness.Dispatch(Alert());

            Assert.Single(harness.Publisher.Payloads);
            Assert.Empty(harness.Sender.Sent);
        }

        [Fact]
        public async Task Dispatcher_WithholdsEmailOverTheHourlyLimitButStillPublishesTheToast()
        {
            var harness = new Harness(EmailOn(maxPerHour: 1), Client("S88", "ops@example.com"));

            await harness.Dispatch(Alert());
            await harness.Dispatch(Alert(selectionName: "Away"));

            Assert.Single(harness.Sender.Sent);
            Assert.Equal(2, harness.Publisher.Payloads.Count);
        }

        [Fact]
        public async Task Dispatcher_DoesNotSendWhenEmailIsOnButNoSmtpHostIsConfigured()
        {
            var harness = new Harness(EmailOn(smtpHost: ""), Client("S88", "ops@example.com"));

            await harness.Dispatch(Alert());

            Assert.Empty(harness.Sender.Sent);
            Assert.Single(harness.Publisher.Payloads);
        }

        [Fact]
        public async Task Dispatcher_KeepsEmailingTheOthersWhenOneRecipientFails()
        {
            var harness = new Harness(EmailOn(), Client("S88", "bad@example.com"), Client("TSO", "good@example.com"));
            harness.Sender.FailFor.Add("bad@example.com");

            await harness.Dispatch(Alert("S88"), Alert("TSO"));

            Assert.Equal("good@example.com", Assert.Single(harness.Sender.Sent).To);
        }

        [Fact]
        public async Task Dispatcher_StillEmailsWhenTheToastCannotBePublished()
        {
            var harness = new Harness(EmailOn(), Client("S88", "ops@example.com"));
            harness.Publisher.Throws = true;

            await harness.Dispatch(Alert());

            Assert.Single(harness.Sender.Sent);
        }

        [Fact]
        public async Task Dispatcher_DoesNothingForAnEmptyBatch()
        {
            var harness = new Harness(EmailOn(), Client("S88", "ops@example.com"));

            await harness.Dispatch();

            Assert.Empty(harness.Publisher.Payloads);
            Assert.Empty(harness.Sender.Sent);
        }

        [Theory]
        [InlineData("ops@example.com", "o***@example.com")]
        [InlineData("a@b.co", "a***@b.co")]
        [InlineData("no-at-sign", "***")]
        [InlineData("@example.com", "***")]
        [InlineData(null, "***")]
        public void MaskAddress_KeepsTheDomainAndTheFirstCharacter(string address, string expected)
        {
            Assert.Equal(expected, AlertDispatcher.MaskAddress(address));
        }
    }
}

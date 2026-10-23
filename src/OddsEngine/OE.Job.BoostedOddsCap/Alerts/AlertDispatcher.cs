using System.Text.Json;
using Microsoft.Extensions.Logging;
using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Evaluation;
using OE.Job.BoostedOddsCap.Telemetry;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // Turns a batch of alerts into the back-office toast and, when email is on, one digest per recipient. A
    // failure in either channel is logged and counted and never stops the other, and never reaches the odds
    // consumer.
    public class AlertDispatcher
    {
        private readonly ToastFactory _toastFactory;
        private readonly INotificationPublisher _publisher;
        private readonly IAlertEmailSender _emailSender;
        private readonly EmailOptions _emailOptions;
        private readonly RecipientRateLimiter _rateLimiter;
        private readonly BoostSnapshotHolder _snapshotHolder;
        private readonly Instrumentation _instrumentation;
        private readonly ILogger<AlertDispatcher> _logger;
        private bool _warnedAboutMissingSmtpHost;

        public AlertDispatcher(ToastFactory toastFactory, INotificationPublisher publisher, IAlertEmailSender emailSender,
            EmailOptions emailOptions, RecipientRateLimiter rateLimiter, BoostSnapshotHolder snapshotHolder,
            Instrumentation instrumentation, ILogger<AlertDispatcher> logger)
        {
            _toastFactory = toastFactory;
            _publisher = publisher;
            _emailSender = emailSender;
            _emailOptions = emailOptions;
            _rateLimiter = rateLimiter;
            _snapshotHolder = snapshotHolder;
            _instrumentation = instrumentation;
            _logger = logger;
        }

        public async Task DispatchAsync(IReadOnlyList<CapAlert> batch, DateTime nowUtc, CancellationToken cancellationToken)
        {
            if (batch.Count == 0)
                return;

            PublishToasts(batch);

            if (_emailOptions.Enabled)
                await SendEmailsAsync(batch, nowUtc, cancellationToken);
        }

        private void PublishToasts(IReadOnlyList<CapAlert> batch)
        {
            foreach (var toast in _toastFactory.Create(batch))
            {
                try
                {
                    _publisher.Publish(JsonSerializer.Serialize(toast));
                    _instrumentation.ToastsPublished.Add(1);
                }
                catch (Exception ex)
                {
                    _instrumentation.ToastFailures.Add(1);
                    _logger.LogError(ex, "Failed to publish the price boost cap notification for client {ClientCode}",
                            toast.ClientCode);
                }
            }
        }

        private async Task SendEmailsAsync(IReadOnlyList<CapAlert> batch, DateTime nowUtc, CancellationToken cancellationToken)
        {
            if (!_emailOptions.IsConfigured)
            {
                if (!_warnedAboutMissingSmtpHost)
                {
                    _warnedAboutMissingSmtpHost = true;
                    _logger.LogWarning("Email is enabled but Email:Smtp:Host is empty, so price boost cap emails are not sent");
                }

                return;
            }

            var snapshot = _snapshotHolder.Current;
            var alertsByRecipient = new Dictionary<string, List<CapAlert>>(StringComparer.OrdinalIgnoreCase);

            foreach (var alert in batch)
            {
                var recipients = string.IsNullOrWhiteSpace(_emailOptions.OverrideRecipient)
                        ? snapshot.RecipientsFor(alert.ClientCode)
                        : new[] { _emailOptions.OverrideRecipient.Trim() };

                foreach (var recipient in recipients)
                {
                    if (!alertsByRecipient.TryGetValue(recipient, out var alerts))
                        alertsByRecipient[recipient] = alerts = new List<CapAlert>();

                    alerts.Add(alert);
                }
            }

            foreach (var pair in alertsByRecipient)
            {
                if (!_rateLimiter.TryAcquire(pair.Key, nowUtc))
                {
                    _instrumentation.EmailsSuppressed.Add(1);
                    _logger.LogWarning("Hourly email limit reached for {Recipient}; {Count} price boost cap alerts not emailed",
                            MaskAddress(pair.Key), pair.Value.Count);
                    continue;
                }

                try
                {
                    await _emailSender.SendAsync(pair.Key,
                            EmailDigest.BuildSubject(_emailOptions, pair.Value.Count),
                            EmailDigest.BuildBody(_emailOptions, pair.Value),
                            cancellationToken);

                    _instrumentation.EmailsSent.Add(1);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _instrumentation.EmailsFailed.Add(1);
                    _logger.LogError(ex, "Failed to email {Count} price boost cap alerts to {Recipient}",
                            pair.Value.Count, MaskAddress(pair.Key));
                }
            }
        }

        // Keeps the domain and the first character, enough to tell recipients apart in a log without printing
        // an address in full.
        public static string MaskAddress(string address)
        {
            var at = address?.IndexOf('@') ?? -1;
            return at <= 0 ? "***" : address.Substring(0, 1) + "***" + address.Substring(at);
        }
    }
}

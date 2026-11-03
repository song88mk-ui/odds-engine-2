using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace OE.Job.BoostedOddsCap.Alerts
{
    public interface IAlertEmailSender
    {
        Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken);
    }

    // Plain SMTP through the framework client, as the v1 job did, so there is no new package. Host, port, TLS and
    // credentials are configuration only, which keeps the choice of relay (SES's SMTP endpoint, an internal MTA)
    // out of the code. The send is bounded by a timeout so a hung server cannot stall the delivery loop.
    public class SmtpAlertEmailSender : IAlertEmailSender
    {
        private readonly EmailOptions _options;

        public SmtpAlertEmailSender(EmailOptions options, ILogger<SmtpAlertEmailSender> logger)
        {
            _options = options;

            // a typo in the internal list would otherwise lose that copy without a trace; the entries are masked
            // like every other address in the logs
            if (options.Enabled && options.BccRejected.Length > 0)
            {
                logger.LogWarning("Email:Bcc holds {RejectedCount} entries that are not valid addresses and are ignored ({Entries}). {BccCount} valid addresses get the internal copy",
                        options.BccRejected.Length, string.Join(", ", options.BccRejected.Select(AlertDispatcher.MaskAddress)),
                        options.Bcc.Length);
            }
        }

        // Public so the message, recipients included, can be checked without a mail server.
        public static MailMessage BuildMessage(EmailOptions options, string toAddress, string subject, string htmlBody)
        {
            var message = new MailMessage
            {
                From = new MailAddress(options.From),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toAddress);

            // The internal copy is hidden (BCC), so the client never sees it. It is skipped while OverrideRecipient
            // redirects everything to one test address, so a test environment never writes to a real internal
            // mailbox, and for a recipient that already is one of the internal addresses, which would get it twice.
            if (string.IsNullOrWhiteSpace(options.OverrideRecipient))
            {
                foreach (var address in options.Bcc)
                {
                    if (!string.Equals(address, toAddress, StringComparison.OrdinalIgnoreCase))
                        message.Bcc.Add(address);
                }
            }

            return message;
        }

        public async Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            using var message = BuildMessage(_options, toAddress, subject, htmlBody);

            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.SmtpEnableSsl
            };

            if (!string.IsNullOrEmpty(_options.SmtpUsername))
                client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.SmtpTimeoutSeconds)));

            await client.SendMailAsync(message, timeout.Token);
        }
    }
}

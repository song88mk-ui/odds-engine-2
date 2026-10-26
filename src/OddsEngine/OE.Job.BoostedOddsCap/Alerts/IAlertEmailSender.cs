using System.Net;
using System.Net.Mail;

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

        public SmtpAlertEmailSender(EmailOptions options)
        {
            _options = options;
        }

        public async Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            using var message = new MailMessage
            {
                From = new MailAddress(_options.From),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            message.To.Add(toAddress);

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

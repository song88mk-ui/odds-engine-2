using Microsoft.Extensions.Configuration;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // Off by default. No SMTP relay or SES is wired into the odds-engine deployments yet, so the job ships
    // with the toast alone and email is switched on per environment by configuration, with no code change
    // (see docs/Configuration.md).
    public sealed class EmailOptions
    {
        public bool Enabled { get; init; }
        public string From { get; init; } = "Notifications <noreply@song88.com>";
        public string Subject { get; init; } = "Song88 | Price Boost Threshold Crossed";

        // When set, every email goes to this one address instead of the client's own, so a lower
        // environment can be exercised without writing to a real client.
        public string OverrideRecipient { get; init; } = string.Empty;

        // At most this many emails reach one recipient in any rolling hour; 0 removes the limit.
        public int MaxPerRecipientPerHour { get; init; } = 10;

        // Base address of Backoffice.Web, for the link to the Manage Boost page. No link when empty.
        public string BackofficeUrl { get; init; } = string.Empty;

        public string SmtpHost { get; init; } = string.Empty;
        public int SmtpPort { get; init; } = 587;
        public bool SmtpEnableSsl { get; init; } = true;
        public string SmtpUsername { get; init; } = string.Empty;
        public string SmtpPassword { get; init; } = string.Empty;
        public int SmtpTimeoutSeconds { get; init; } = 10;

        public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(SmtpHost);

        public static EmailOptions FromConfiguration(IConfiguration configuration)
        {
            var defaults = new EmailOptions();

            return new EmailOptions
            {
                Enabled = configuration.GetValue("Email:Enabled", defaults.Enabled),
                From = configuration.GetValue("Email:From", defaults.From),
                Subject = configuration.GetValue("Email:Subject", defaults.Subject),
                OverrideRecipient = configuration.GetValue("Email:OverrideRecipient", defaults.OverrideRecipient),
                MaxPerRecipientPerHour = configuration.GetValue("Email:MaxPerRecipientPerHour", defaults.MaxPerRecipientPerHour),
                BackofficeUrl = configuration.GetValue("Email:BackofficeUrl", defaults.BackofficeUrl),
                SmtpHost = configuration.GetValue("Email:Smtp:Host", defaults.SmtpHost),
                SmtpPort = configuration.GetValue("Email:Smtp:Port", defaults.SmtpPort),
                SmtpEnableSsl = configuration.GetValue("Email:Smtp:EnableSsl", defaults.SmtpEnableSsl),
                SmtpUsername = configuration.GetValue("Email:Smtp:Username", defaults.SmtpUsername),
                SmtpPassword = configuration.GetValue("Email:Smtp:Password", defaults.SmtpPassword),
                SmtpTimeoutSeconds = configuration.GetValue("Email:Smtp:TimeoutSeconds", defaults.SmtpTimeoutSeconds)
            };
        }
    }
}

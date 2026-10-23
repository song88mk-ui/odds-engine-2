using System.Globalization;
using System.Net;
using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // Same JSON shape as Backoffice.Shared NotificationsDetailsModel, which Backoffice.WebSocket.Service
    // deserialises into when it forwards the message to the Backoffice.Web toast.
    public sealed class NotificationMessage
    {
        public Guid Id { get; set; }
        public string Page { get; set; }
        public string ClientCode { get; set; }
        public int? SportsId { get; set; }
        public string Details { get; set; }
        public string PageRedirect { get; set; }

        // Null on purpose. Backoffice.Web shows a toast whose UserId is empty to every connected user, and
        // silently drops one whose UserId is not the viewer's, which includes Guid.Empty (the v1 job sent
        // that, so its toasts reached nobody).
        public Guid? UserId { get; set; }

        public string ClaimType { get; set; }
        public int Type { get; set; }
    }

    public class ToastFactory
    {
        private readonly NotificationOptions _options;

        public ToastFactory(NotificationOptions options)
        {
            _options = options;
        }

        // One toast per alert, except that a client with more alerts than the threshold in the same batch
        // (a market-wide move can push dozens of boosts over their cap at once) gets a single summary.
        public IReadOnlyList<NotificationMessage> Create(IReadOnlyList<CapAlert> batch)
        {
            var toasts = new List<NotificationMessage>();

            foreach (var group in batch.GroupBy(a => a.ClientCode))
            {
                var alerts = group.ToList();

                if (alerts.Count > _options.SummaryThreshold)
                    toasts.Add(Summary(group.Key, alerts));
                else
                    toasts.AddRange(alerts.Select(Single));
            }

            return toasts;
        }

        private NotificationMessage Single(CapAlert alert)
        {
            // names come from the odds feed and the toast is rendered as markup, so they are encoded
            var details = $"The boosted odds (<b>{Odds(alert.BoostedOdds)}</b>) for {Encode(alert.SportName)} - "
                    + $"{Encode(alert.SegmentName)} - {Encode(alert.LeagueName)} - {Encode(alert.MarketName)} - "
                    + $"{Encode(alert.SelectionName)} of <b>{Encode(alert.ClientCode)}</b> with CAP "
                    + $"(<b>{Odds(alert.CapPercent)}%</b>) for the Current Odds <b>{Odds(alert.CurrentOdds)}</b> has been reached.";

            return Build(alert.ClientCode, alert.SportId, details);
        }

        private NotificationMessage Summary(string clientCode, IReadOnlyList<CapAlert> alerts)
        {
            var details = $"<b>{alerts.Count}</b> boosted odds of <b>{Encode(clientCode)}</b> have reached their CAP. "
                    + "Open Manage Boost to review them.";

            return Build(clientCode, null, details);
        }

        private NotificationMessage Build(string clientCode, int? sportId, string details)
        {
            return new NotificationMessage
            {
                Id = Guid.NewGuid(),
                Page = _options.Page,
                ClientCode = clientCode,
                SportsId = sportId,
                Details = details,
                PageRedirect = _options.PageRedirect,
                UserId = null,
                ClaimType = _options.ClaimType,
                Type = _options.Type
            };
        }

        public static string Odds(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        public static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
    }
}

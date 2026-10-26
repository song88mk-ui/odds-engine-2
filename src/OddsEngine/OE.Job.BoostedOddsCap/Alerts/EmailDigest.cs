using System.Text;
using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // One email per recipient per batch, listing every boost that reached its cap in it. A market-wide move can
    // push many boosts over the line within seconds; a digest turns that into a single message instead of one
    // per boost, which is what keeps both the mail server and the recipient's inbox out of trouble.
    public static class EmailDigest
    {
        public static string BuildSubject(EmailOptions options, int alertCount)
        {
            return alertCount > 1 ? $"{options.Subject} ({alertCount})" : options.Subject;
        }

        public static string BuildBody(EmailOptions options, IReadOnlyList<CapAlert> alerts)
        {
            var html = new StringBuilder();

            html.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head>");
            html.Append("<body style=\"margin:0;padding:16px;background:#f4f4f4;font-family:'Segoe UI',Arial,sans-serif;color:#222;\">");
            html.Append("<div style=\"max-width:720px;margin:0 auto;background:#ffffff;\">");
            html.Append("<div style=\"background:#ca0303;color:#ffffff;padding:12px 20px;font-size:18px;font-weight:600;\">Price Boosts</div>");
            html.Append("<p style=\"padding:0 20px;\">");
            html.Append(alerts.Count == 1
                    ? "The current odds of a boosted selection have reached its CAP %. See details below."
                    : $"The current odds of {alerts.Count} boosted selections have reached their CAP %. See details below.");
            html.Append("</p>");

            html.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"6\" border=\"1\" ");
            html.Append("style=\"width:100%;border-collapse:collapse;border-color:#dddddd;font-size:14px;\">");
            html.Append("<tr style=\"background:#f0f0f0;text-align:left;\"><th>Client</th><th>Fixture</th><th>Market - Selection</th>");
            html.Append("<th>Boosted Odds</th><th>Current Odds</th><th>Cap %</th></tr>");

            foreach (var alert in alerts)
            {
                html.Append("<tr>");
                html.Append(Cell(alert.ClientCode));
                html.Append(Cell($"{alert.SportName} - {alert.SegmentName} - {alert.LeagueName} - {alert.FixtureName}"));
                html.Append(Cell($"{alert.MarketName} - {alert.SelectionName}"));
                html.Append(Cell(ToastFactory.Odds(alert.BoostedOdds)));
                html.Append(Cell(ToastFactory.Odds(alert.CurrentOdds)));
                html.Append(Cell(ToastFactory.Odds(alert.CapPercent)));
                html.Append("</tr>");
            }

            html.Append("</table>");

            if (!string.IsNullOrWhiteSpace(options.BackofficeUrl))
            {
                var link = options.BackofficeUrl.TrimEnd('/') + "/hk/odds-engine/manage-boost";
                html.Append("<p style=\"padding:12px 20px;\"><a href=\"");
                html.Append(ToastFactory.Encode(link));
                html.Append("\">View in the Manage Boost page</a></p>");
            }

            html.Append("<p style=\"padding:0 20px 16px;font-size:12px;color:#666;\">");
            html.Append("This email was sent from a notification address that cannot accept replies.</p>");
            html.Append("</div></body></html>");

            return html.ToString();
        }

        private static string Cell(string value) => $"<td>{ToastFactory.Encode(value)}</td>";
    }
}

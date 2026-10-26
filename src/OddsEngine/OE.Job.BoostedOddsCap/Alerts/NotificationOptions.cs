using Microsoft.Extensions.Configuration;

namespace OE.Job.BoostedOddsCap.Alerts
{
    // How the back-office toast is addressed. The defaults are the current Manage Boost page of
    // backoffice-final (Backoffice.Web/Pages/OddsEngine/ManageBoost.razor) and its permission; the v1 job's
    // /price-boost/manage route and PriceBoost.BoostedSettings.Access claim no longer exist there.
    public sealed class NotificationOptions
    {
        public string Page { get; init; } = "Price Boost";
        public string PageRedirect { get; init; } = "/hk/odds-engine/manage-boost";
        public string ClaimType { get; init; } = "SBC.OE.PriceBoost.Manage";

        // Backoffice.Shared NotificationType: General = 0, Priceboost = 1, BoostedSettings = 2.
        public int Type { get; init; } = 1;

        // What backoffice-final's Backoffice.WebSocket.Service binds (r.oe.bm.engine.notifications) and reads.
        public string RoutingKey { get; init; } = "r.oe.bm.engine.notifications";
        public string MessageType { get; init; } = "OnNewNotification";

        // More alerts than this for one client in one batch are sent as a single summary toast.
        public int SummaryThreshold { get; init; } = 5;

        public static NotificationOptions FromConfiguration(IConfiguration configuration)
        {
            var defaults = new NotificationOptions();

            return new NotificationOptions
            {
                Page = configuration.GetValue("Notification:Page", defaults.Page),
                PageRedirect = configuration.GetValue("Notification:PageRedirect", defaults.PageRedirect),
                ClaimType = configuration.GetValue("Notification:ClaimType", defaults.ClaimType),
                Type = configuration.GetValue("Notification:Type", defaults.Type),
                RoutingKey = configuration.GetValue("Notification:RoutingKey", defaults.RoutingKey),
                MessageType = configuration.GetValue("Notification:MessageType", defaults.MessageType),
                SummaryThreshold = configuration.GetValue("Notification:SummaryThreshold", defaults.SummaryThreshold)
            };
        }
    }
}

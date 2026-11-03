using System.Net.Mail;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Boosts
{
    // Who is emailed for a client. Backoffice's Clients page holds an Email Address and a Notify email switch per
    // client, and the switch is what says "email this client", so a client is a recipient only when it is not
    // deleted, the switch is on and the field holds at least one valid address. The v1 job emailed every client
    // that had an address and ignored the switch.
    public static class RecipientRules
    {
        // The recipients by normalised client code.
        public static Dictionary<string, string[]> Build(IEnumerable<ClientRecipientRow> clients)
        {
            var byClient = new Dictionary<string, List<string>>();

            foreach (var row in clients)
            {
                if (row.IsDeleted || !row.IsEmailNotification)
                    continue;

                var code = BoostText.Code(row.Brand);
                if (code.Length == 0)
                    continue;

                var addresses = ParseAddresses(row.Email);
                if (addresses.Length == 0)
                    continue;

                // two records can share a brand (one per region), so their addresses are pooled
                if (!byClient.TryGetValue(code, out var list))
                    byClient[code] = list = new List<string>();

                list.AddRange(addresses);
            }

            return byClient.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }

        // The Email Address field is free text. A single address is the norm, a list separated by ; or , is
        // tolerated, and anything that is not a valid address is dropped rather than failing the whole send later.
        public static string[] ParseAddresses(string email)
        {
            return Entries(email)
                    .Where(address => MailAddress.TryCreate(address, out _))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
        }

        // The entries ParseAddresses drops, so a field that holds a typo can be reported instead of losing the
        // address without a trace.
        public static string[] RejectedAddresses(string email)
        {
            return Entries(email)
                    .Where(address => !MailAddress.TryCreate(address, out _))
                    .ToArray();
        }

        private static string[] Entries(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return new string[0];

            return email.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}

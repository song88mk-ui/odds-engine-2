using OE.Job.BoostedOddsCap.Boosts;
using OE.Job.BoostedOddsCap.Data;

namespace OE.Job.BoostedOddsCap.Tests
{
    // Who is emailed. The data is Backoffice's ClientInformation, where a client has an Email Address and a Notify
    // email switch. On DEV (checked 2026-10-27) MRD, OKT and TVL have an address and the switch off, so nobody there
    // is emailed until a person turns a switch on: the v1 job ignored the switch, this one honours it.
    public class RecipientRulesTests
    {
        private static ClientRecipientRow Row(string brand, string email, bool notify = true, bool deleted = false)
            => new ClientRecipientRow { Brand = brand, Email = email, IsEmailNotification = notify, IsDeleted = deleted };

        [Fact]
        public void AClientWithTheSwitchOnAndAnAddressIsARecipient()
        {
            var recipients = RecipientRules.Build(new[] { Row("S88", "ops@example.com") });

            Assert.Equal(new[] { "ops@example.com" }, recipients["S88"]);
        }

        [Fact]
        public void AClientWithTheSwitchOffIsNotEmailedEvenWithAnAddress()
        {
            Assert.Empty(RecipientRules.Build(new[] { Row("S88", "ops@example.com", notify: false) }));
        }

        [Fact]
        public void ADeletedClientIsNotEmailed()
        {
            Assert.Empty(RecipientRules.Build(new[] { Row("S88", "ops@example.com", deleted: true) }));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("not-an-address")]
        public void AClientWithoutAUsableAddressIsNotEmailed(string email)
        {
            Assert.Empty(RecipientRules.Build(new[] { Row("S88", email) }));
        }

        [Fact]
        public void TheBrandIsNormalisedLikeEveryOtherClientCode()
        {
            var recipients = RecipientRules.Build(new[] { Row(" s88 ", "ops@example.com") });

            Assert.Equal(new[] { "S88" }, recipients.Keys);
        }

        [Fact]
        public void ARecordWithoutABrandIsIgnored()
        {
            Assert.Empty(RecipientRules.Build(new[] { Row(null, "ops@example.com"), Row("  ", "ops@example.com") }));
        }

        [Fact]
        public void TwoRecordsOfOneBrandPoolTheirAddressesWithoutRepeats()
        {
            var recipients = RecipientRules.Build(new[]
            {
                Row("S88", "a@example.com; b@example.com"),
                Row("s88", "B@EXAMPLE.COM, c@example.com")
            });

            Assert.Equal(new[] { "a@example.com", "b@example.com", "c@example.com" }, recipients["S88"]);
        }

        [Fact]
        public void OnlyTheClientsThatQualifyAreListed()
        {
            var recipients = RecipientRules.Build(new[]
            {
                Row("MRD", "mrd@example.com", notify: false),
                Row("OKT", "okt@example.com"),
                Row("TVL", "tvl@example.com", deleted: true)
            });

            Assert.Equal(new[] { "OKT" }, recipients.Keys);
        }

        [Fact]
        public void ParseAddresses_SplitsOnSemicolonsAndCommasAndDropsWhatIsNotAnAddress()
        {
            var addresses = RecipientRules.ParseAddresses("a@example.com; b@example.com, not-an-address,A@EXAMPLE.COM ,");

            Assert.Equal(new[] { "a@example.com", "b@example.com" }, addresses);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void ParseAddresses_ReturnsNothingForAnEmptyField(string email)
        {
            Assert.Empty(RecipientRules.ParseAddresses(email));
        }
    }
}

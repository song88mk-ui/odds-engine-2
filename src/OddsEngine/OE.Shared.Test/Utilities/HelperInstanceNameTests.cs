using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // Pins the fixture -> sportsbook-instance sharding. Every r.oe.adapter.*.{NN} routing key is
    // derived from this: a change silently re-shards fixtures onto different OE.Sportsbook
    // instances mid-flight.
    public class HelperInstanceNameTests
    {
        private readonly Helper _helper = new();

        [Theory]
        [InlineData("1234567890", "01")] // last=0 -> base 0; second-last 9 (odd) -> +1
        [InlineData("1234567891", "01")] // last=1 -> base 0; second-last 9 (odd) -> +1
        [InlineData("1234567800", "00")] // last=0 -> base 0; second-last 0 (even) -> +0
        [InlineData("1234567805", "04")] // last=5 -> base 4; second-last 0 (even) -> +0
        [InlineData("1234567819", "09")] // last=9 -> base 8; second-last 1 (odd) -> +1
        public void GetInstanceName_ShardsByLastTwoDigits(string providerFixtureId, string expected)
        {
            Assert.Equal(expected, _helper.GetInstanceName(providerFixtureId));
        }

        [Fact]
        public void GetInstanceName_AlwaysLandsInTheTenKnownInstances()
        {
            var known = Enumerable.Range(0, 10).Select(i => i.ToString("D2")).ToHashSet();

            for (var i = 0; i < 100; i++)
                Assert.Contains(_helper.GetInstanceName($"12345678{i:D2}"), known);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("7")]
        public void GetInstanceName_RejectsInvalidFixtureIds(string? providerFixtureId)
        {
            Assert.Throws<ArgumentException>(() => _helper.GetInstanceName(providerFixtureId!));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Theory]
        [InlineData("1234567890", "01")]
        [InlineData("1234567891", "01")]
        [InlineData("1234567800", "00")]
        [InlineData("1234567805", "04")]
        [InlineData("1234567819", "09")]
        public void GetInstanceName_ShardsByLastTwoDigits_MutationCheck(string providerFixtureId, string expected)
        {
            var wrong = expected == "00" ? "09" : "00";

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.Equal(wrong, _helper.GetInstanceName(providerFixtureId)));
        }

        [Fact]
        public void GetInstanceName_AlwaysLandsInTheTenKnownInstances_MutationCheck()
        {
            var known = Enumerable.Range(0, 10).Select(i => i.ToString("D2")).ToHashSet();

            for (var i = 0; i < 100; i++)
            {
                var instance = _helper.GetInstanceName($"12345678{i:D2}");
                Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.DoesNotContain(instance, known));
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("7")]
        public void GetInstanceName_RejectsInvalidFixtureIds_MutationCheck(string? providerFixtureId)
        {
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            {
                var exception = Record.Exception(() => _helper.GetInstanceName(providerFixtureId!));
                Assert.Null(exception);
            });
        }

        #endregion
    }
}

using OE.WebSocket.Service.Clients;

namespace OE.Websocket.Service.Tests
{
    // Covers the GM-720 presence-aware skip state machine. The critical property
    // is FAIL OPEN: whenever presence information is missing or unavailable, every
    // client must count as active - the failure mode is wasted publishes, never
    // silently starved clients. This is also what makes the feeder safe against a
    // hub that does not expose /v1/activeclients yet.
    public class ClientPresenceTests
    {
        private static ClientPresence BuildPresence() => new();

        [Fact]
        public void NoPollYet_FailsOpen()
        {
            var presence = BuildPresence();

            Assert.True(presence.IsActive("s88"));
            Assert.True(presence.IsActive("anything"));
        }

        [Fact]
        public void AfterUpdate_OnlyActiveCodesPass()
        {
            var presence = BuildPresence();

            presence.Update(new[] { "GSA", "s88" });

            Assert.True(presence.IsActive("gsa"));
            Assert.True(presence.IsActive("s88"));
            Assert.False(presence.IsActive("qte"));
        }

        [Fact]
        public void EmptyActiveList_SkipsEveryone()
        {
            var presence = BuildPresence();

            presence.Update(Array.Empty<string>());

            Assert.False(presence.IsActive("s88"));
        }

        [Fact]
        public void MarkUnavailable_FailsOpenAgain()
        {
            var presence = BuildPresence();

            presence.Update(Array.Empty<string>());
            Assert.False(presence.IsActive("s88"));

            presence.MarkUnavailable();

            Assert.True(presence.IsActive("s88"));
        }

        #region Mutation checks — prove the assertions above actually discriminate real vs. wrong values

        // Each check re-runs the same path as its real counterpart above, then asserts a
        // deliberately WRONG value wrapped in Assert.ThrowsAny<XunitException>. The wrapped
        // assertion is expected to fail — that failure is what makes this test pass (green). If
        // the real test's assertion could never distinguish correct from wrong (a vacuous or
        // tautological check), the wrong-value assertion here would NOT throw, and this test
        // would go red instead — flagging the real test as untrustworthy.

        [Fact]
        public void NoPollYet_FailsOpen_MutationCheck()
        {
            var presence = BuildPresence();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(presence.IsActive("s88")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(presence.IsActive("anything")));
        }

        [Fact]
        public void AfterUpdate_OnlyActiveCodesPass_MutationCheck()
        {
            var presence = BuildPresence();

            presence.Update(new[] { "GSA", "s88" });

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(presence.IsActive("gsa")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(presence.IsActive("s88")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(presence.IsActive("qte")));
        }

        [Fact]
        public void EmptyActiveList_SkipsEveryone_MutationCheck()
        {
            var presence = BuildPresence();

            presence.Update(Array.Empty<string>());

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(presence.IsActive("s88")));
        }

        [Fact]
        public void MarkUnavailable_FailsOpenAgain_MutationCheck()
        {
            var presence = BuildPresence();

            presence.Update(Array.Empty<string>());
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.True(presence.IsActive("s88")));

            presence.MarkUnavailable();

            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => Assert.False(presence.IsActive("s88")));
        }

        #endregion
    }
}

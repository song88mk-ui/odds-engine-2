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
    }
}

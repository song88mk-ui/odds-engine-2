using OE.Job.BoostedOddsCap.Evaluation;

namespace OE.Job.BoostedOddsCap.Tests
{
    // The tracker is what stops the v1 behaviour of one alert per odds tick while a boost sits inside its cap.
    // The rule used throughout has a 5% cap and the tracker a 0.25 point hysteresis.
    public class CapAlertTrackerTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

        private static CapAlertTracker NewTracker() => new CapAlertTracker(0.25m);

        [Fact]
        public void AlertsOnceWhenABoostCrossesIntoItsCap()
        {
            var tracker = NewTracker();
            var rule = TestMessages.Rule();

            Assert.False(tracker.ShouldAlert(rule, 8m, Now));   // first sight, still outside the cap
            Assert.True(tracker.ShouldAlert(rule, 4m, Now));    // crosses in
            Assert.False(tracker.ShouldAlert(rule, 3m, Now));   // still inside, no repeat
            Assert.False(tracker.ShouldAlert(rule, 4.9m, Now));
        }

        [Fact]
        public void IsNotReArmedByOddsJitteringAroundTheCapLine()
        {
            var tracker = NewTracker();
            var rule = TestMessages.Rule();

            tracker.ShouldAlert(rule, 8m, Now);
            Assert.True(tracker.ShouldAlert(rule, 4m, Now));

            // above the cap but within the hysteresis band, then back in: the same breach, not a new one
            Assert.False(tracker.ShouldAlert(rule, 5.2m, Now));
            Assert.False(tracker.ShouldAlert(rule, 4m, Now));
        }

        [Fact]
        public void AlertsAgainAfterTheGapHasClimbedClearOfTheCap()
        {
            var tracker = NewTracker();
            var rule = TestMessages.Rule();

            tracker.ShouldAlert(rule, 8m, Now);
            Assert.True(tracker.ShouldAlert(rule, 4m, Now));
            Assert.False(tracker.ShouldAlert(rule, 5.3m, Now));  // past cap + hysteresis: re-armed
            Assert.True(tracker.ShouldAlert(rule, 4m, Now));
        }

        [Fact]
        public void TreatsAChangeToTheBoostOrTheCapAsANewBoost()
        {
            var tracker = NewTracker();

            tracker.ShouldAlert(TestMessages.Rule(boostedOdds: 2.10m), 8m, Now);
            Assert.True(tracker.ShouldAlert(TestMessages.Rule(boostedOdds: 2.10m), 4m, Now));
            Assert.False(tracker.ShouldAlert(TestMessages.Rule(boostedOdds: 2.10m), 4m, Now));

            // the trader edits the boosted odds and the new boost is already inside its cap
            Assert.True(tracker.ShouldAlert(TestMessages.Rule(boostedOdds: 2.05m), 4m, Now));
        }

        [Fact]
        public void TracksEachBoostSeparately()
        {
            var tracker = NewTracker();
            var first = TestMessages.Rule(clientCode: "S88");
            var second = TestMessages.Rule(clientCode: "TSO");

            tracker.ShouldAlert(first, 8m, Now);
            tracker.ShouldAlert(second, 8m, Now);

            Assert.True(tracker.ShouldAlert(first, 4m, Now));
            Assert.True(tracker.ShouldAlert(second, 4m, Now));
        }

        [Fact]
        public void BoostFirstSeenInsideItsCapDuringTheSilentWindowIsRecordedWithoutAlerting()
        {
            var tracker = NewTracker();
            var rule = TestMessages.Rule();
            tracker.StartSilentWindow(Now.AddSeconds(120));

            Assert.False(tracker.ShouldAlert(rule, 4m, Now));

            // it is remembered as inside, so it does not alert once the window ends either
            Assert.False(tracker.ShouldAlert(rule, 4m, Now.AddSeconds(300)));
        }

        [Fact]
        public void BoostFirstSeenInsideItsCapAfterTheSilentWindowAlerts()
        {
            var tracker = NewTracker();
            tracker.StartSilentWindow(Now.AddSeconds(120));

            Assert.True(tracker.ShouldAlert(TestMessages.Rule(), 4m, Now.AddSeconds(121)));
        }

        [Fact]
        public void CrossingIntoTheCapDuringTheSilentWindowStillAlerts()
        {
            var tracker = NewTracker();
            var rule = TestMessages.Rule();
            tracker.StartSilentWindow(Now.AddSeconds(120));

            tracker.ShouldAlert(rule, 8m, Now);

            // a crossing the job watched happen is real news whatever the window says
            Assert.True(tracker.ShouldAlert(rule, 4m, Now.AddSeconds(10)));
        }

        [Fact]
        public void RetainForgetsBoostsThatNoLongerExist()
        {
            var tracker = NewTracker();
            var kept = TestMessages.Rule(clientCode: "S88");
            var removed = TestMessages.Rule(clientCode: "TSO");
            tracker.ShouldAlert(kept, 8m, Now);
            tracker.ShouldAlert(removed, 8m, Now);

            tracker.Retain(new[] { kept.Key });

            Assert.Equal(1, tracker.Count);
        }
    }
}

using OE.Shared.Enums;
using OE.Shared.Utilities;

namespace OE.Shared.Test.Utilities
{
    // GetInstanceName is covered by HelperInstanceNameTests; this covers Helper's other public
    // surface — the settlement void-factor mapping used to compute payout adjustments.
    public class HelperVoidFactorTests
    {
        [Theory]
        [InlineData(BetStatus.Won, VoidFactor.Zero)]
        [InlineData(BetStatus.Lost, VoidFactor.Zero)]
        [InlineData(BetStatus.Void, VoidFactor.One)]
        [InlineData(BetStatus.HalfWon, VoidFactor.Half)]
        [InlineData(BetStatus.HalfLost, VoidFactor.Half)]
        [InlineData(BetStatus.Waiting, VoidFactor.Zero)]
        [InlineData(BetStatus.Running, VoidFactor.Zero)]
        [InlineData(BetStatus.Rejected, VoidFactor.Zero)]
        [InlineData(BetStatus.Cancelled, VoidFactor.Zero)]
        [InlineData(BetStatus.CashedOut, VoidFactor.Zero)]
        public void GetVoidFactor_FromBetStatus_MapsToExpectedFactor(BetStatus status, VoidFactor expected)
        {
            Assert.Equal(expected, Helper.GetVoidFactor(status));
        }

        [Theory]
        [InlineData(SettlementStatus.Won, VoidFactor.Zero)]
        [InlineData(SettlementStatus.Lost, VoidFactor.Zero)]
        [InlineData(SettlementStatus.Void, VoidFactor.One)]
        [InlineData(SettlementStatus.HalfWon, VoidFactor.Half)]
        [InlineData(SettlementStatus.HalfLost, VoidFactor.Half)]
        [InlineData(SettlementStatus.Pending, VoidFactor.Zero)]
        [InlineData(SettlementStatus.Cancelled, VoidFactor.Zero)]
        [InlineData(SettlementStatus.CashedOut, VoidFactor.Zero)]
        public void GetVoidFactor_FromSettlementStatus_MapsToExpectedFactor(SettlementStatus status, VoidFactor expected)
        {
            Assert.Equal(expected, Helper.GetVoidFactor(status));
        }
    }
}

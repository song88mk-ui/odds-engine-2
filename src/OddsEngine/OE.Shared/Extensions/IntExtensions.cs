using OE.Shared.Enums;
using System;
using System.Collections.Generic;

namespace OE.Shared.Extensions
{
    public static class IntExtensions
    {
        // 11205870 is Virtual Cricket, the only virtual sport onboarded on the Genius Sports feed.
        // The other virtual sports (soccer, basketball, baseball, tennis, horse racing, dogs) still
        // carry the previous provider's ids 1001-1058 downstream in platform-web's sport-name
        // switch, and those are dead in this id space, so do not add them here. Nor is the id range
        // a guide: 11205863, right next to Virtual Cricket, is real 3x3 Basketball. Mirrors
        // platform-web Utility2.IsVirtualSportId and backoffice-final SportHelper.IsVirtualSportId.
        public static bool IsVirtualSportId(this int sportId)
        {
            var listOfVirtualSports = new List<int> { 11205870 };

            return listOfVirtualSports.Contains(sportId);
        }

        public static BetStatus GetBetStatus(this int settlementStatusId)
        {
            if (!Enum.IsDefined(typeof(SettlementStatus), settlementStatusId))
                throw new ArgumentOutOfRangeException(
                    nameof(settlementStatusId),
                    $"Invalid SettlementStatus: {settlementStatusId}"
                );

            var settlementStatus = (SettlementStatus)settlementStatusId;

            return settlementStatus switch
            {
                SettlementStatus.Pending => BetStatus.Running,
                SettlementStatus.Won => BetStatus.Won,
                SettlementStatus.HalfWon => BetStatus.HalfWon,
                SettlementStatus.Lost => BetStatus.Lost,
                SettlementStatus.HalfLost => BetStatus.HalfLost,
                SettlementStatus.Void => BetStatus.Void,
                SettlementStatus.Cancelled => BetStatus.Cancelled,
                SettlementStatus.CashedOut => BetStatus.CashedOut,
                // Unreachable via the Enum.IsDefined guard above; present so that adding a
                // SettlementStatus member without mapping it fails loudly instead of silently.
                _ => throw new ArgumentOutOfRangeException(
                    nameof(settlementStatusId),
                    $"Unmapped SettlementStatus: {settlementStatus}"
                ),
            };
        }
    }
}

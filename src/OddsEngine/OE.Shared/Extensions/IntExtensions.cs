using OE.Shared.Enums;
using System;
using System.Collections.Generic;

namespace OE.Shared.Extensions
{
    public static class IntExtensions
    {
        public static bool IsVirtualSportId(this int sportId)
        {
            var listOfVirtualSports = new List<int>();
            //todo: append virtual sports here

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

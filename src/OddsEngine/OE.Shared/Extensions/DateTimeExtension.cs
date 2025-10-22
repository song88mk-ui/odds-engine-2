using System;

namespace OE.Shared.Extensions
{
    public static class DateTimeExtension
    {
        public const long EpochTicks = 621355968000000000;
        public const long TicksPeriod = 10000000;
        public const long TicksPeriodMs = 10000;

        /// <summary>
        /// Number of milliseconds since epoch(1/1/1970).
        /// </summary>
        /// <param name="date">DateTime to convert</param>
        /// <returns>Number of milliseconds since 1/1/1970 (Unix timestamp)</returns>
        public static long ToEpochTimestamp(this DateTime date)
        {
            long ts = (date.Ticks - EpochTicks) / TicksPeriodMs;
            return ts;
        }
    }
}

using System;

namespace OE.Shared.Extensions
{
    internal static class LongExtension
    {
        public static DateTime ToDateTime(this long ticks)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ticks).DateTime;
        }
    }
}

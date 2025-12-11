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
    }
}

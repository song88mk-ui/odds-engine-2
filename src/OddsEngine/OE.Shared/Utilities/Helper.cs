using System;

namespace OE.Shared.Utilities
{
    public class Helper : IHelper
    {
        public string GetInstanceName(string providerFixtureId)
        {
            var digit = providerFixtureId;
            var lastDigit = digit.Substring(digit.Length - 1);
            var secondLastDigit = digit.Substring(digit.Length - 2).Substring(0, 1);

            var result = lastDigit switch
            {
                "1" => Convert.ToInt32(secondLastDigit) % 2 == 0 ? "0" : "1",
                "3" => Convert.ToInt32(secondLastDigit) % 2 == 0 ? "2" : "3",
                "5" => Convert.ToInt32(secondLastDigit) % 2 == 0 ? "4" : "5",
                "7" => Convert.ToInt32(secondLastDigit) % 2 == 0 ? "6" : "7",
                "9" => Convert.ToInt32(secondLastDigit) % 2 == 0 ? "8" : "9",
                _ => lastDigit,
            };

            if (result.ToString().Length == 1)
                return $"0{result}";
            else
                return result.ToString();
        }
    }
}

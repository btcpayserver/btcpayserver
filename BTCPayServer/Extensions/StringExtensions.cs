using System;

namespace BTCPayServer
{
    public static class StringExtensions
    {
        public static string TrimEnd(this string input, string suffixToRemove,
            StringComparison comparisonType)
        {
            if (input != null && suffixToRemove != null
                              && input.EndsWith(suffixToRemove, comparisonType))
            {
                return input.Substring(0, input.Length - suffixToRemove.Length);
            }
            else
                return input;
        }

        public static bool HasValue(this string input)
        {
            return !string.IsNullOrEmpty(input);
        }

        public static bool IsNullOrEmpty(this string input)
        {
            return string.IsNullOrEmpty(input);
        }

    }
}

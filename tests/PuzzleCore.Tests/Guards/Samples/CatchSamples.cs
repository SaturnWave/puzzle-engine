using System;

// Deliberate violations for the catch-block scanner's self-test; the analyzer would reject the generic catches.
#pragma warning disable CA1031

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>Catch blocks the code standard forbids (CLAUDE.md, rule 6), one per method.</summary>
    internal static class ForbiddenCatchSample
    {
        public static int CatchesEverything(string text)
        {
            try
            {
                return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        public static int CatchesEverythingWithAFilter(string text, bool isLenient)
        {
            try
            {
                return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception) when (isLenient)
            {
                return -1;
            }
        }

        public static int SwallowsSilently(string text)
        {
            int result = 0;
            try
            {
                result = int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
            }

            return result;
        }

        public static int BareCatch(string text)
        {
            try
            {
                return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return -1;
            }
        }
    }

    /// <summary>A specific catch that turns the technical error into a domain answer: allowed.</summary>
    internal static class AllowedCatchSample
    {
        public static string ParseOrExplain(string text)
        {
            try
            {
                return int.Parse(text, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (FormatException error)
            {
                return "not a number: " + error.Message;
            }
        }
    }
}

using System.Collections.Generic;
using System.Globalization;

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// Near misses of the planted gaps in <see cref="PolicyGapSamples"/>: the same shapes on values whose text does not
    /// depend on the device culture, or with the invariant culture passed. The guard tests expect no violation here, so a
    /// rule that refuses more than its gap shows up at once.
    /// </summary>
    internal static class CleanNearMissSample
    {
        public static string NumberWithInvariantCulture(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string BoxedTruth(bool value) => ((object)value).ToString()!;

        public static string BoxedLetter(char letter) => ((object)letter).ToString()!;

        public static string TupleOfText(string first, string second) => (first, second).ToString();

        public static string PairOfText(KeyValuePair<string, string> pair) => pair.ToString();

        public static string RecordText(CleanCell cell) => cell.ToString();

        public static string EnumName(CleanColour colour) => colour.ToString();

        public static string GenericWithInvariantCulture<T>(T value) => string.Format(CultureInfo.InvariantCulture, "value {0}", value);
    }

    /// <summary>An enum of the sample's own: its ToString prints the member's name.</summary>
    internal enum CleanColour
    {
        Red = 1,
        Blue = 2,
    }
}

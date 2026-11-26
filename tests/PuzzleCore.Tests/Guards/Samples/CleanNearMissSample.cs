using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

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

        public static int ParseInvariant(string text) => int.Parse(text, CultureInfo.InvariantCulture);

        public static bool TryFormatInvariant(int value, Span<char> destination) => value.TryFormat(destination, out _, default, CultureInfo.InvariantCulture);

        public static string UpperInvariantCulture(string text) => text.ToUpper(CultureInfo.InvariantCulture);

        // Null for a parameter that takes no culture.
        public static string TextAfterNothing(string text) => string.Concat(null, text);

        public static List<string> OrderByOrdinalComparer(IEnumerable<string> names) => names.OrderBy(name => name, StringComparer.Ordinal).ToList();

        public static SortedSet<string> SortedSetWithOrdinalComparer() => new SortedSet<string>(StringComparer.Ordinal);

        public static bool EqualsOrdinal(string first, string second) => first.Equals(second, StringComparison.Ordinal);

        public static bool StaticEqualsOrdinalIgnoreCase(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

        public static int CompareOrdinal(string first, string second) => string.Compare(first, second, StringComparison.Ordinal);

        public static bool StartsWithOrdinalIgnoreCase(string text) => text.StartsWith("gold", StringComparison.OrdinalIgnoreCase);

        public static int IndexOfLetterOrdinal(string text) => text.IndexOf('g', StringComparison.Ordinal);

        public static bool SpanEqualsOrdinal(string first, string second) => first.AsSpan().Equals(second.AsSpan(), StringComparison.Ordinal);

        // The same constant on both paths where control merges: the walk keeps it.
        public static bool SameComparisonOnBothPaths(string first, string second, bool flag) =>
            first.Equals(second, flag ? StringComparison.Ordinal : StringComparison.Ordinal);

        public static int ListBinarySearchOfNumbers(List<int> values) => values.BinarySearch(5);

        public static int ListBinarySearchOrdinal(List<string> names) => names.BinarySearch("gold", StringComparer.Ordinal);

        public static int ArrayBinarySearchOfNumbers(int[] values) => Array.BinarySearch(values, 5);

        public static int ArrayBinarySearchOrdinal(string[] names) => Array.BinarySearch(names, "gold", StringComparer.Ordinal);

        public static int SpanBinarySearchOfNumbers(int[] values) => values.AsSpan().BinarySearch(5);

        public static int SpanBinarySearchOrdinal(string[] names) => names.AsSpan().BinarySearch("gold", StringComparer.Ordinal);

        public static int CompareNumbersThroughInterface(int first, int second) => ((IComparable<int>)first).CompareTo(second);

        public static int CompareBoxedNumber(int first, int second) => ((IComparable)first).CompareTo(second);

        public static int[] SortedSetCopyTo(SortedSet<int> values)
        {
            var copy = new int[values.Count];
            values.CopyTo(copy);
            return copy;
        }
    }

    /// <summary>An enum of the sample's own: its ToString prints the member's name.</summary>
    internal enum CleanColour
    {
        Red = 1,
        Blue = 2,
    }
}

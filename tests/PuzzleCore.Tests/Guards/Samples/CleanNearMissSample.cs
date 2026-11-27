using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// Near misses of the planted gaps in <see cref="PolicyGapSamples"/> and <see cref="ValueFormattingSamples"/>: the
    /// same shapes on values whose text does not depend on the device culture, or with the invariant culture passed. The
    /// guard tests expect no violation here, so a rule that refuses more than its gap shows up at once.
    /// </summary>
    internal static class CleanNearMissSample
    {
        public static string NumberWithInvariantCulture(int value) => value.ToString(CultureInfo.InvariantCulture);

        public static string BoxedTruth(bool value) => ((object)value).ToString()!;

        public static string BoxedLetter(char letter) => ((object)letter).ToString()!;

        public static string TupleOfText(string first, string second) => (first, second).ToString();

        public static string PairOfText(KeyValuePair<string, string> pair) => pair.ToString();

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

        // Text joined, and overloads whose integers are an index or a count: nothing is formatted by the device culture.
        public static StringBuilder AppendJoinText(StringBuilder builder, string[] names) => builder.AppendJoin(", ", names);

        public static StringBuilder AppendJoinTextWithChar(StringBuilder builder, string[] names) => builder.AppendJoin(',', names);

        public static StringBuilder AppendJoinTextList(StringBuilder builder, List<string> names) => builder.AppendJoin(", ", names);

        public static StringBuilder AppendJoinTextListWithChar(StringBuilder builder, List<string> names) => builder.AppendJoin(',', names);

        public static StringBuilder AppendJoinTextSpan(StringBuilder builder, string[] names) => builder.AppendJoin(", ", (ReadOnlySpan<string?>)names);

        public static StringBuilder AppendJoinTextSpanWithChar(StringBuilder builder, string[] names) => builder.AppendJoin(',', (ReadOnlySpan<string?>)names);

        public static string JoinTextWithChar(string[] names) => string.Join(',', names);

        public static string JoinTextSpan(string[] names) => string.Join(", ", (ReadOnlySpan<string?>)names);

        public static string ConcatTextSequence(IEnumerable<string> names) => string.Concat(names);

        public static string ConcatTextArray(string[] names) => string.Concat(names);

        public static StringBuilder AppendTruth(StringBuilder builder, bool value) => builder.Append(value);

        public static StringBuilder AppendRepeatedLetter(StringBuilder builder) => builder.Append('-', 3);

        public static StringBuilder AppendPartOfText(StringBuilder builder, string text) => builder.Append(text, 1, 2);

        public static StringBuilder InsertRepeatedText(StringBuilder builder, string text) => builder.Insert(0, text, 2);

        // A format with a provider over a tuple or a pair of text, and over a nullable number, which formats as the number.
        public static string FormatTupleOfTextWithProvider(string first, string second) =>
            string.Format(CultureInfo.InvariantCulture, "{0}", (first, second));

        public static string FormatNullableNumberWithProvider(int? value) => string.Format(CultureInfo.InvariantCulture, "{0}", value);

        public static StringBuilder AppendFormatPairOfTextWithProvider(StringBuilder builder, KeyValuePair<string, string> pair) =>
            builder.AppendFormat(CultureInfo.InvariantCulture, "{0}", pair);

        // A value read through a member of a generic type has the type the caller fills in: here text, or a class whose
        // ToString prints a constant, not a T.
        public static string LabelOfList(IReadOnlyList<CleanLabel> labels) => labels[0].ToString();

        public static string LabelInText(IReadOnlyList<CleanLabel> labels) => "label " + labels[0];

        public static string FirstLabel(IEnumerable<CleanLabel> labels) => labels.First().ToString();

        public static string LabelsInKeyOrder(SortedDictionary<int, CleanLabel> labels)
        {
            var text = new StringBuilder();
            foreach (KeyValuePair<int, CleanLabel> entry in labels)
            {
                text.Append(entry.Value.ToString());
            }

            return text.ToString();
        }

        public static string LabelOfPair((CleanLabel Label, int Count) pair) => pair.Label.ToString();

        public static string NameOfList(List<string> names) => names[0].ToString();

        // An anonymous type of text, a tuple of numbers compared, an ordering by ordinal or by a number, and an ordinal Replace.
        public static string AnonymousText(string name) => new { Name = name }.ToString()!;

        public static int CompareTupleOfNumbers((int Row, int Column) first, (int Row, int Column) second) => first.CompareTo(second);

        public static List<string> CreateOrderedEnumerableOrdinal(IEnumerable<string> names) =>
            names.OrderBy(name => name.Length).CreateOrderedEnumerable(name => name, StringComparer.Ordinal, false).ToList();

        public static List<string> CreateOrderedEnumerableOfNumbers(IEnumerable<string> names) =>
            names.OrderBy(name => name.Length).CreateOrderedEnumerable(name => name.Length * 2, null, true).ToList();

        public static string ReplaceOrdinal(string text) => text.Replace("a", "b", StringComparison.Ordinal);
    }

    /// <summary>A class of the clean samples' own whose ToString prints a constant, as a hand-written ToString may.</summary>
    internal sealed class CleanLabel
    {
        public override string ToString() => "label";
    }

    /// <summary>An enum of the sample's own: its ToString prints the member's name.</summary>
    internal enum CleanColour
    {
        Red = 1,
        Blue = 2,
    }
}

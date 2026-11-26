using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

// Every member below depends on the device culture or on hash order on purpose. The analyzers would (rightly) reject
// some of them, hence the local suppression.
#pragma warning disable CA1305

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// The known gaps that the remarks of <see cref="Support.UnityBoundApiPolicy"/> list, one planted use each: the guard
    /// tests expect both policies, or the member bans alone, to let each through. A change that closes a gap fails there,
    /// and the remarks and the tests change with it. Lives in the test assembly only.
    /// </summary>
    internal static class KnownGapSamples
    {
        // A set handed on as a sequence through an interface-typed local or a helper's return value.
        public static List<int> SetHandedOnThroughLocal(HashSet<int> values)
        {
            IEnumerable<int> sequence = values;
            return sequence.ToList();
        }

        public static List<int> SetHandedOnThroughHelper(HashSet<int> values) => AsSequence(values).ToList();

        // A value stored in a local first: loaded again, it has the local's declared type.
        public static string NumberBoxedIntoLocal(int value)
        {
            object boxed = value;
            return boxed.ToString()!;
        }

        public static string NullProviderInLocal(int value)
        {
            IFormatProvider? provider = null;
            return value.ToString(provider);
        }

        // A receiver the walk does not know, such as an element of an object array, which may be a boxed integer.
        public static string NumberInAnObjectArray(object[] values) => values[0].ToString()!;

        // A StringComparison passed through a delegate, whose parameter is a generic placeholder.
        public static bool ComparisonThroughDelegate(string first, string second)
        {
            Func<string, StringComparison, bool> equals = first.Equals;
            return equals(second, StringComparison.CurrentCulture);
        }

        // An enum value outside the enum's members prints its number, a negative one with the device culture's minus.
        public static string EnumValueOutsideItsMembers() => ((KnownGapColour)(-5)).ToString();

        // A tuple that holds a number, in an argument array: the walk does not see an array's elements, and the provider
        // does not reach the number, which the tuple's ToString formats by the device culture.
        public static string TupleInAnArgumentArray(int first, int second) =>
            string.Format(CultureInfo.InvariantCulture, "{0}", new object[] { (first, second) });

        // Of these, the member bans alone pass each, and only the allowlist refuses them: their types, or a member that
        // the member limit of an allowed type leaves out (Environment.NewLine).
        public static string FloatingPointText(double value) => value.ToString();

        public static string NullProviderOfInterpolation(int value) => string.Create(null, $"{value} pieces");

        // A tuple in the argument array that FormattableStringFactory.Create keeps for FormattableString.Invariant.
        public static string TupleInAFormattableString(int first, int second) => FormattableString.Invariant($"{(first, second)}");

        // The platform newline: a writer's WriteLine ends the line with Environment.NewLine unless told otherwise.
        public static void PlatformNewlineThroughWriter(TextWriter writer, string text) => writer.WriteLine(text);

        public static void PlatformNewlineThroughConsole(string text) => Console.WriteLine(text);

        public static string PlatformNewline(string text) => text + Environment.NewLine;

        public static int ConcurrentDictionaryOrder(ConcurrentDictionary<string, int> table)
        {
            int weighted = 0;
            int position = 1;
            foreach (KeyValuePair<string, int> entry in table)
            {
                weighted += entry.Value * position;
                position++;
            }

            return weighted;
        }

        private static IEnumerable<int> AsSequence(HashSet<int> values) => values;
    }

    /// <summary>An enum of the known-gap samples' own.</summary>
    internal enum KnownGapColour
    {
        Red = 1,
    }

    /// <summary>
    /// Near misses of the member bans that name a type the allowlist refuses, so they can only be checked against the
    /// member bans alone: the guard tests expect no violation here from <see cref="Support.MemberBansPolicy"/>.
    /// </summary>
    internal static class MemberBansNearMissSample
    {
        // An array's non-generic BinarySearch with the untyped comparer it takes.
        public static int ArrayBinarySearchWithUntypedComparer(Array names, System.Collections.IComparer comparer) => Array.BinarySearch(names, "gold", comparer);

        // A writer given text, a letter, a truth value, or a char array with its index and count formats nothing.
        public static void WriteText(TextWriter writer, string text) => writer.Write(text);

        public static void WriteLetter(TextWriter writer, char letter) => writer.Write(letter);

        public static void WriteTruth(TextWriter writer, bool value) => writer.Write(value);

        public static void WriteLetters(TextWriter writer, char[] letters) => writer.Write(letters, 0, 2);

        public static void WriteTextSpan(TextWriter writer, string text) => writer.Write(text.AsSpan());

        public static void ConsoleWriteText(string text) => Console.Write(text);

        // Convert.ToString in a number base, which has no culture, and with the invariant culture.
        public static string NumberInBinary(int value) => Convert.ToString(value, 2);

        public static string ConvertWithInvariantCulture(int value) => Convert.ToString(value, CultureInfo.InvariantCulture);

        public static string? ConvertObjectWithInvariantCulture(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);

        // FormattableStringFactory.Create keeps the format and the values; formatted with the invariant culture, they
        // print the same on every device.
        public static string FormattableStringInvariant(int pieces) => FormattableString.Invariant($"{pieces} pieces");

        public static string FormattableStringWithInvariantCulture(int pieces)
        {
            FormattableString text = $"{pieces} pieces";
            return text.ToString(CultureInfo.InvariantCulture);
        }

        // The interpolation handlers with the invariant culture, over a number and over a tuple of text.
        public static string InterpolatedWithInvariantCulture(int pieces) => string.Create(CultureInfo.InvariantCulture, $"{pieces} pieces");

        public static string InterpolatedTupleOfText(string first, string second) => string.Create(CultureInfo.InvariantCulture, $"{(first, second)}");

        public static bool SpanWriteWithInvariantCulture(Span<char> destination, int pieces) =>
            destination.TryWrite(CultureInfo.InvariantCulture, $"{pieces} pieces", out _);

        public static bool Utf8WriteWithInvariantCulture(Span<byte> destination, int pieces) =>
            System.Text.Unicode.Utf8.TryWrite(destination, CultureInfo.InvariantCulture, $"{pieces} pieces", out _);
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        // Of these, the member bans alone pass each, and only the allowlist refuses their types.
        public static string NumberTextThroughConvert(int value) => Convert.ToString(value);

        public static void NumberTextThroughWriter(TextWriter writer, int value) => writer.Write(value);

        public static string FloatingPointText(double value) => value.ToString();

        public static string NullProviderOfInterpolation(int value) => string.Create(null, $"{value} pieces");

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
    }
}

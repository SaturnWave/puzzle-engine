using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

// Every member below breaks a determinism rule on purpose, so the guard tests can prove
// that their scanners see each kind of violation. The analyzers would (rightly) reject
// several of them, hence the local suppressions.
#pragma warning disable CA1304, CA1305, CA1307, CA1309, CA1310, CA1311, CA1060, CA2101, SYSLIB1054, CA1838

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// Deliberately breaks the determinism rules. Lives in the test assembly only; the
    /// guard tests scan this assembly and expect a violation at each named member.
    /// </summary>
    internal static class DeterminismViolationsSample
    {
        public static double DoubleField = 0.75;

        public static float HalveWithFloatLocal(int count)
        {
            // A loop-carried local survives Release optimisation, so the scanner always sees it.
            float total = 0f;
            for (int index = 0; index < count; index++)
            {
                total += index / 2f;
            }

            return total;
        }

        public static decimal Price() => 1.5m;

        public static long FloorOfDoubleLiteral() => (long)Math.Floor(DoubleField + 0.25);

        public static bool NormalizedVector() => Vector2.Normalize(Vector2.One) == Vector2.UnitX;

        public static int NextFromSystemRandom() => new Random(1).Next();

        public static long ReadTheClock() => DateTime.UtcNow.Ticks;

        public static long LocalTimeConversion(long ticks) => new DateTime(ticks).ToLocalTime().Ticks;

        public static int PlainStringHash(string text) => text.GetHashCode();

        public static int OrdinalComparerHash(string text) => StringComparer.Ordinal.GetHashCode(text);

        public static int TupleHash(int first, int second) => (first, second).GetHashCode();

        public static string IntegerToTextByConcatenation(int delta) => "gold:" + delta;

        public static string FormatWithoutCulture(int delta) => string.Format("gold:{0}", delta);

        public static StringBuilder AppendInteger(StringBuilder builder, int delta) => builder.Append(delta);

        public static StringBuilder AppendPlatformNewline(StringBuilder builder) => builder.AppendLine("line");

        public static string UpperWithDeviceCulture(string text) => text.ToUpper();

        public static int CompareWithDeviceCulture(string first, string second) => string.Compare(first, second);

        public static int ChangeTypeThroughDouble(string text) =>
            Convert.ToInt32(Convert.ChangeType(text, TypeCode.Double, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public static bool TypeByName() => Type.GetType("System.Random") is not null;

        public static int ProcessorCount() => Environment.ProcessorCount;

        public static int ThreadIdentity() => Environment.CurrentManagedThreadId;

        public static nint NativeSizedSum(nint first, nint second) => first + second;

        public static int DictionaryOrder(Dictionary<string, int> table)
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

        public static List<int> SortWithListSort(List<int> values)
        {
            values.Sort();
            return values;
        }

        public static int[] SortWithArraySort(int[] values)
        {
            Array.Sort(values);
            return values;
        }

        public static int[] SortWithSpanSort(int[] values)
        {
            values.AsSpan().Sort();
            return values;
        }

        // Text ordered by the device culture: "ägg" sorts first in en-CA and last in sv-SE.
        public static List<string> OrderTextWithoutComparer(IEnumerable<string> names) => names.OrderBy(name => name).ToList();

        public static List<string> OrderTextByCurrentCulture(IEnumerable<string> names) => names.OrderBy(name => name, StringComparer.CurrentCulture).ToList();

        public static List<(string Name, int Rank)> OrderByTupleHoldingText(IEnumerable<(string Name, int Rank)> entries) =>
            entries.OrderBy(entry => (entry.Name, entry.Rank)).ToList();

        public static SortedDictionary<string, int> SortedDictionaryWithoutComparer() => new SortedDictionary<string, int>();

        public static SortedSet<string> SortedSetWithoutComparer() => new SortedSet<string>();

        public static int CompareWithDefaultComparer(string first, string second) => Comparer<string>.Default.Compare(first, second);

        // Hash order seen through an interface or a LINQ method.
        public static int ReadOnlyDictionaryOrder(IReadOnlyDictionary<string, int> table)
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

        public static List<string> ReadOnlyDictionaryKeys(IReadOnlyDictionary<string, int> table) => table.Keys.ToList();

        public static List<int> DictionaryThroughLinq(Dictionary<string, int> table) => table.Select(entry => entry.Value).ToList();

        public static List<string> HashSetToList(HashSet<string> names) => names.ToList();

        public static string HashSetJoined(HashSet<string> names) => string.Join(",", names);

        public static List<int> HashSetThroughLambda(HashSet<int> values) => values.Where(value => value > 0).ToList();

        public static int PairsOfUnknownOrder(IEnumerable<KeyValuePair<string, int>> pairs)
        {
            int weighted = 0;
            int position = 1;
            foreach (KeyValuePair<string, int> pair in pairs)
            {
                weighted += pair.Value * position;
                position++;
            }

            return weighted;
        }

        // C# 10 and later: DefaultInterpolatedStringHandler without a provider, which CA1305 does not flag.
        public static string InterpolatedWithDeviceCulture(int pieces) => $"limit {pieces}";

        [DllImport("libc", EntryPoint = "time")]
        public static extern long NativeTime(IntPtr target);
    }
}

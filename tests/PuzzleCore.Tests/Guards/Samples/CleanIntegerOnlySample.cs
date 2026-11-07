using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>A record like the engine will use, to check that compiler-written record members pass.</summary>
    internal sealed record CleanCell(int Row, int Column);

    /// <summary>
    /// Realistic integer-only code that follows every determinism rule. The guard tests
    /// expect zero violations here, so an over-broad scanner rule shows up at once.
    /// </summary>
    internal static class CleanIntegerOnlySample
    {
        public static long CheckedSum(IReadOnlyList<int> values)
        {
            long total = 0;
            foreach (int value in values)
            {
                total = checked(total + value);
            }

            return total;
        }

        public static string DescribeScore(int score) => "score:" + score.ToString(CultureInfo.InvariantCulture);

        public static int LookUp(Dictionary<string, int> table, string key) =>
            table.TryGetValue(key, out int value) ? value : -1;

        public static IReadOnlyList<int> SortedAscending(IEnumerable<int> values) => values.OrderBy(value => value).ToList();

        public static string JoinLines(IReadOnlyList<string> lines)
        {
            var builder = new StringBuilder();
            foreach (string line in lines)
            {
                builder.Append(line).Append('\n');
            }

            return builder.ToString();
        }

        public static bool SameCell(CleanCell first, CleanCell second) => first == second;

        public static string CellText(CleanCell cell) =>
            cell.Row.ToString(CultureInfo.InvariantCulture) + "," + cell.Column.ToString(CultureInfo.InvariantCulture);

        public static int ClampToBoard(int value) => Math.Clamp(value, 0, 9);

        public static byte[] Utf8Bytes(string text) => Encoding.UTF8.GetBytes(text);

        public static int ParseInvariant(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

        public static bool StartsWithGold(string text) => text.StartsWith("gold", StringComparison.Ordinal);

        public static string UpperInvariant(string text) => text.ToUpperInvariant();

        public static int SumInKeyOrder(SortedDictionary<int, int> table)
        {
            int sum = 0;
            foreach (KeyValuePair<int, int> entry in table)
            {
                sum += entry.Value;
            }

            return sum;
        }

        // Hash-ordered collections used in ways whose result does not depend on their order, or sorted first.
        public static IReadOnlyList<int> ValuesInKeyOrder(Dictionary<int, int> table) =>
            table.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();

        public static bool KnowsAll(HashSet<int> known, IReadOnlyList<int> wanted) => known.Count >= wanted.Count && wanted.All(known.Contains);

        public static HashSet<int> Union(HashSet<int> first, HashSet<int> second)
        {
            var union = new HashSet<int>(first);
            union.UnionWith(second);
            return union;
        }

        public static int CountPairs(IReadOnlyList<KeyValuePair<string, int>> pairs)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> pair in pairs)
            {
                total += pair.Value;
            }

            return total;
        }

        public static IReadOnlyList<string> NamesInOrdinalOrder(IEnumerable<string> names) =>
            names.OrderBy(name => name, StringComparer.Ordinal).ToList();

        public static string JoinNames(IReadOnlyList<string> names) => string.Join(",", names);

        public static int Classify(int lines) => lines switch
        {
            0 => 0,
            1 => 10,
            _ => 10 * lines * (lines + 1) / 2,
        };

        public static IEnumerable<int> CountUpTo(int limit)
        {
            for (int value = 0; value < limit; value++)
            {
                yield return value;
            }
        }

        public static int LowestSetBit(ulong mask)
        {
            int index = 0;
            while (index < 64 && (mask & (1UL << index)) == 0)
            {
                index++;
            }

            return index;
        }
    }
}

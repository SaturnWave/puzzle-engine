using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

// Every member below makes one banned call on purpose, so the guard tests can prove that each member ban reports its
// use. The analyzers would (rightly) reject several of them, hence the local suppressions.
#pragma warning disable CA1304, CA1305, CA1307, CA1309, CA1310, CA1311

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// One planted use of each member ban of <see cref="Support.UnityBoundApiPolicy"/>, named after the banned member.
    /// A ban that nothing plants can be dropped with every test green, so the guard tests require a planted use of every
    /// ban the policy lists and expect each method here to break its ban under both policies. Lives in the test
    /// assembly only.
    /// </summary>
    internal static class MemberBanSamples
    {
        // Integer text and parsing without a provider.
        public static string ByteToString(byte value) => value.ToString();

        public static byte ByteParse(string text) => byte.Parse(text);

        public static bool ByteTryParse(string text) => byte.TryParse(text, out _);

        public static string SByteToString(sbyte value) => value.ToString();

        public static sbyte SByteParse(string text) => sbyte.Parse(text);

        public static bool SByteTryParse(string text) => sbyte.TryParse(text, out _);

        public static string Int16ToString(short value) => value.ToString();

        public static short Int16Parse(string text) => short.Parse(text);

        public static bool Int16TryParse(string text) => short.TryParse(text, out _);

        public static string UInt16ToString(ushort value) => value.ToString();

        public static ushort UInt16Parse(string text) => ushort.Parse(text);

        public static bool UInt16TryParse(string text) => ushort.TryParse(text, out _);

        public static string Int32ToString(int value) => value.ToString();

        public static int Int32Parse(string text) => int.Parse(text);

        public static bool Int32TryParse(string text) => int.TryParse(text, out _);

        public static string UInt32ToString(uint value) => value.ToString();

        public static uint UInt32Parse(string text) => uint.Parse(text);

        public static bool UInt32TryParse(string text) => uint.TryParse(text, out _);

        public static string Int64ToString(long value) => value.ToString();

        public static long Int64Parse(string text) => long.Parse(text);

        public static bool Int64TryParse(string text) => long.TryParse(text, out _);

        public static string UInt64ToString(ulong value) => value.ToString();

        public static ulong UInt64Parse(string text) => ulong.Parse(text);

        public static bool UInt64TryParse(string text) => ulong.TryParse(text, out _);

        // Text by the device culture.
        public static string StringToUpper(string text) => text.ToUpper();

        public static string StringToLower(string text) => text.ToLower();

        public static int StringCompare(string first, string second) => string.Compare(first, second);

        public static int StringCompareTo(string first, string second) => first.CompareTo(second);

        public static bool StringStartsWith(string text) => text.StartsWith("gold");

        public static bool StringEndsWith(string text) => text.EndsWith("gold");

        public static int StringIndexOf(string text) => text.IndexOf("gold");

        public static int StringLastIndexOf(string text) => text.LastIndexOf("gold");

        public static string StringFormat(int pieces) => string.Format("{0} pieces", pieces);

        public static string StringConcat(object first, object second) => string.Concat(first, second);

        public static string StringJoin(IEnumerable<int> values) => string.Join(",", values);

        public static char CharToUpper(char letter) => char.ToUpper(letter);

        public static char CharToLower(char letter) => char.ToLower(letter);

        public static StringBuilder StringBuilderAppend(StringBuilder builder, long value) => builder.Append(value);

        public static StringBuilder StringBuilderInsert(StringBuilder builder, int value) => builder.Insert(0, value);

        public static StringBuilder StringBuilderAppendFormat(StringBuilder builder, int pieces) => builder.AppendFormat("{0} pieces", pieces);

        public static StringBuilder StringBuilderAppendLine(StringBuilder builder) => builder.AppendLine();

        public static string InterpolatedString(int pieces) => $"{pieces} pieces";

        public static StringBuilder InterpolatedAppend(StringBuilder builder, int pieces) => builder.Append($"{pieces} pieces");

        public static CultureInfo CurrentCulture() => CultureInfo.CurrentCulture;

        public static CultureInfo CurrentUICulture() => CultureInfo.CurrentUICulture;

        // Text order by the device culture or the platform's collation tables.
        public static StringComparer CurrentCultureComparer() => StringComparer.CurrentCulture;

        public static StringComparer CurrentCultureIgnoreCaseComparer() => StringComparer.CurrentCultureIgnoreCase;

        public static StringComparer InvariantCultureComparer() => StringComparer.InvariantCulture;

        public static StringComparer InvariantCultureIgnoreCaseComparer() => StringComparer.InvariantCultureIgnoreCase;

        public static StringComparer CreatedComparer() => StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: false);

        public static StringComparer ComparerFromComparison() => StringComparer.FromComparison(StringComparison.Ordinal);

        // Default and DefaultInvariant of System.Collections.Comparer are static fields, read with ldsfld.
        public static IComparer NonGenericDefaultComparer() => Comparer.Default;

        public static IComparer NonGenericDefaultInvariantComparer() => Comparer.DefaultInvariant;

        public static IComparer<string> DefaultTextComparer() => Comparer<string>.Default;

        public static SortedDictionary<string, int> SortedDictionaryOfText() => new SortedDictionary<string, int>();

        public static SortedSet<string> SortedSetOfText() => new SortedSet<string>();

        public static SortedList<string, int> SortedListOfText() => new SortedList<string, int>();

        public static List<string> OrderByText(IEnumerable<string> names) => names.OrderBy(name => name).ToList();

        public static List<string> OrderByDescendingText(IEnumerable<string> names) => names.OrderByDescending(name => name).ToList();

        public static List<(string Name, int Rank)> ThenByText(IEnumerable<(string Name, int Rank)> entries) =>
            entries.OrderBy(entry => entry.Rank).ThenBy(entry => entry.Name).ToList();

        public static List<(string Name, int Rank)> ThenByDescendingText(IEnumerable<(string Name, int Rank)> entries) =>
            entries.OrderBy(entry => entry.Rank).ThenByDescending(entry => entry.Name).ToList();

        public static List<string> OrderText(IEnumerable<string> names) => names.Order().ToList();

        public static List<string> OrderDescendingText(IEnumerable<string> names) => names.OrderDescending().ToList();

        public static string? MinText(IEnumerable<string> names) => names.Min();

        public static string? MaxText(IEnumerable<string> names) => names.Max();

        public static (string Name, int Rank) MinByText(IEnumerable<(string Name, int Rank)> entries) => entries.MinBy(entry => entry.Name);

        public static (string Name, int Rank) MaxByText(IEnumerable<(string Name, int Rank)> entries) => entries.MaxBy(entry => entry.Name);

        // Hash codes, which are randomized per process.
        public static int ObjectGetHashCode(object value) => value.GetHashCode();

        public static int StringGetHashCode(string text) => text.GetHashCode(StringComparison.Ordinal);

        public static int StringComparerGetHashCode(string text) => StringComparer.Ordinal.GetHashCode(text);

        public static int EqualityComparerGetHashCode(string text) => EqualityComparer<string>.Default.GetHashCode(text);

        public static int EqualityComparerInterfaceGetHashCode(IEqualityComparer<string> comparer, string text) => comparer.GetHashCode(text);

        public static int RuntimeHelpersGetHashCode(object value) => RuntimeHelpers.GetHashCode(value);

        // Thread identity.
        public static int CurrentManagedThreadId() => Environment.CurrentManagedThreadId;

        // Enumeration in hash order.
        public static int DictionaryGetEnumerator(Dictionary<int, int> table)
        {
            int sum = 0;
            foreach (KeyValuePair<int, int> entry in table)
            {
                sum += entry.Value;
            }

            return sum;
        }

        public static int DictionaryKeys(Dictionary<int, int> table) => table.Keys.Count;

        public static int DictionaryValues(Dictionary<int, int> table) => table.Values.Count;

        public static int HashSetGetEnumerator(HashSet<int> values)
        {
            int sum = 0;
            foreach (int value in values)
            {
                sum += value;
            }

            return sum;
        }

        public static IEnumerable<int> ReadOnlyDictionaryKeys(IReadOnlyDictionary<int, int> table) => table.Keys;

        public static IEnumerable<int> ReadOnlyDictionaryValues(IReadOnlyDictionary<int, int> table) => table.Values;

        public static ICollection<int> DictionaryInterfaceKeys(IDictionary<int, int> table) => table.Keys;

        public static ICollection<int> DictionaryInterfaceValues(IDictionary<int, int> table) => table.Values;

        // Unstable sorts.
        public static List<int> ListSort(List<int> values)
        {
            values.Sort();
            return values;
        }

        public static int[] ArraySort(int[] values)
        {
            Array.Sort(values);
            return values;
        }

        public static int[] SpanSort(int[] values)
        {
            values.AsSpan().Sort();
            return values;
        }

        public static ImmutableArray<int> ImmutableArraySort(ImmutableArray<int> values) => values.Sort();

        public static ImmutableArray<int>.Builder ImmutableArrayBuilderSort(ImmutableArray<int>.Builder values)
        {
            values.Sort();
            return values;
        }

        public static ImmutableList<int> ImmutableListSort(ImmutableList<int> values) => values.Sort();

        public static ImmutableList<int>.Builder ImmutableListBuilderSort(ImmutableList<int>.Builder values)
        {
            values.Sort();
            return values;
        }
    }

    /// <summary>A struct that asks System.ValueType for its hash code: base.GetHashCode() is how IL comes to name ValueType's.</summary>
    internal readonly struct ValueTypeHashSample
    {
        public int BaseHash() => base.GetHashCode();
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

// Every member below breaks a rule of the scan on purpose. The analyzers would (rightly) reject one of them, hence the
// local suppression.
#pragma warning disable CA1305

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// Planted violations that one part of the scan alone sees: a set whose type the IL walk learns from a local, a
    /// field, newobj, dup, a cast or a branch; each hash-ordered type and each sequence parameter type of
    /// <see cref="Support.UnorderedSequences"/>, and its receiver and LINQ-over-pairs rules; each member limit of the
    /// Unity-bound allowlist; a refused type that only a signature names, and one that only a type argument names; and a
    /// floating-point type argument. Lives in the test assembly only; the guard tests expect each violation at its member.
    /// </summary>
    internal static class PolicyEdgeSamples
    {
        private static readonly HashSet<string> KnownWords = new HashSet<string>(StringComparer.Ordinal) { "gold", "silver" };

        // A set handed to a sequence parameter, its type known to the walk from one kind of instruction each.
        public static string SetFromLocal(IEnumerable<string> words)
        {
            var set = new HashSet<string>(words, StringComparer.Ordinal);
            set.Add("gold");
            return string.Join(",", set);
        }

        public static List<string> SetFromField() => KnownWords.ToList();

        public static List<string> SetFromNewobj(IEnumerable<string> words) => new HashSet<string>(words, StringComparer.Ordinal).ToList();

        public static string SetFromDup() => string.Join(",", new HashSet<string>(StringComparer.Ordinal) { "gold" });

        public static List<int> SetFromCast(object values) => ((HashSet<int>)values).ToList();

        // The set is pushed before the branch that picks the count, so only the stack recorded at the jump target holds it.
        public static List<int> SetAcrossBranch(HashSet<int> values, bool one) => values.Take(one ? 1 : 2).ToList();

        // Each hash-ordered type, handed to a sequence parameter that no other rule judges. A dictionary goes to the List
        // constructor, which takes no type argument of its own, so the LINQ-over-pairs rule does not see it as well.
        public static List<KeyValuePair<string, int>> DictionaryIntoList(Dictionary<string, int> table) => new List<KeyValuePair<string, int>>(table);

        public static List<string> KeyCollectionToList(Dictionary<string, int>.KeyCollection keys) => keys.ToList();

        public static List<int> ValueCollectionToList(Dictionary<string, int>.ValueCollection values) => values.ToList();

        public static List<int> HashSetToList(HashSet<int> values) => values.ToList();

        public static List<int> SetInterfaceToList(ISet<int> values) => values.ToList();

        public static List<int> ReadOnlySetToList(IReadOnlySet<int> values) => values.ToList();

        public static List<KeyValuePair<string, int>> ReadOnlyDictionaryIntoList(IReadOnlyDictionary<string, int> table) => new List<KeyValuePair<string, int>>(table);

        public static List<KeyValuePair<string, int>> DictionaryInterfaceIntoList(IDictionary<string, int> table) => new List<KeyValuePair<string, int>>(table);

        public static List<KeyValuePair<string, int>> ConcurrentDictionaryIntoList(ConcurrentDictionary<string, int> table) => new List<KeyValuePair<string, int>>(table);

        public static List<int> ConcurrentBagToList(ConcurrentBag<int> values) => values.ToList();

        public static List<int> ImmutableHashSetToList(ImmutableHashSet<int> values) => values.ToList();

        public static List<KeyValuePair<string, int>> ImmutableDictionaryIntoList(ImmutableDictionary<string, int> table) => new List<KeyValuePair<string, int>>(table);

        public static List<int> ImmutableSetInterfaceToList(IImmutableSet<int> values) => values.ToList();

        public static List<KeyValuePair<string, int>> ImmutableDictionaryInterfaceIntoList(IImmutableDictionary<string, int> table) => new List<KeyValuePair<string, int>>(table);

        // A set handed to each sequence parameter type but IEnumerable<T>, which the members above take.
        public static List<int> SetAsNonGenericSequence(HashSet<int> values) => values.Cast<int>().ToList();

        public static int SetAsCollection(HashSet<int> values) => CountOfCollection(values);

        public static int SetAsReadOnlyCollection(HashSet<int> values) => CountOfReadOnlyCollection(values);

        // A set interface enumerated as a sequence: only the receiver rule sees it.
        public static int SumOfSetInterface(ISet<int> values)
        {
            int sum = 0;
            foreach (int value in values)
            {
                sum += value;
            }

            return sum;
        }

        // A set copied out through ICollection<T>: the receiver rule sees the set behind the interface.
        public static int[] SetCopiedThroughCollection(HashSet<int> values)
        {
            var copy = new int[values.Count];
            ((ICollection<int>)values).CopyTo(copy, 0);
            return copy;
        }

        // Key-value pairs of unknown order through LINQ: only the LINQ-over-pairs rule sees it.
        public static List<int> ValuesOfPairs(IEnumerable<KeyValuePair<string, int>> pairs) => pairs.Select(pair => pair.Value).ToList();

        // Each member limit of the allowlist, broken by a member left out of it.
        public static string TextInSwedish(int value) => value.ToString(CultureInfo.GetCultureInfo("sv-SE"));

        public static byte[] DefaultEncodingBytes(string text) => Encoding.Default.GetBytes(text);

        public static byte[] HashInOneCall(byte[] data) => SHA256.HashData(data);

        public static byte[]? LastHash(HashAlgorithm algorithm) => algorithm.Hash;

        public static bool HoldsReferences() => RuntimeHelpers.IsReferenceOrContainsReferences<int>();

        // A refused type that only this signature names: the body names no type, so only the type reference table has it.
        public static int KeepTime(TimeOnly moment, int fallback) => fallback;

        // A refused type that a call names only as a type argument.
        public static int CountOfTimes(List<DateTime> times) => times.Count;

        // A floating-point type that appears only as a type argument.
        public static List<double> EmptyPrices() => new List<double>();

        private static int CountOfCollection(ICollection<int> values) => values.Count;

        private static int CountOfReadOnlyCollection(IReadOnlyCollection<int> values) => values.Count;
    }

    /// <summary>
    /// A hand-written class whose members carry names the compiler gives to the members it writes for records and
    /// iterators. Being hand-written, none of them is exempt: each breaks a ban, and the exemptions must not reach them.
    /// </summary>
    internal sealed class HandWrittenMembers
    {
        private readonly int _row;

        // A constructor, as an iterator's plumbing is, but of a hand-written type: reading the thread id is banned.
        public HandWrittenMembers(int row)
        {
            _row = row;
            CreatingThread = Environment.CurrentManagedThreadId;
        }

        public int CreatingThread { get; }

        // Named as a record's ToString, but hand-written: it formats an integer with the device culture.
        public override string ToString() => "row " + _row;

        public override bool Equals(object? obj) => obj is HandWrittenMembers other && other._row == _row;

        // A GetHashCode override may call the hashing members, and no other banned member: this one formats an integer.
        public override int GetHashCode() => _row.ToString().Length;
    }
}

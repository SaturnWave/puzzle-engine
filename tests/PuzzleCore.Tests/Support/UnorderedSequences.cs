using System;
using System.Collections.Generic;
using System.Linq;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// Enumeration in hash order that the per-member bans of <see cref="UnityBoundApiPolicy"/> cannot see, because it goes
    /// through an interface or a LINQ method (CLAUDE.md: "Dictionary/HashSet üzerinde gezinilmez"). Three shapes, judged on
    /// the static types the IL walk of <see cref="AssemblyInspection"/> knows for a call's receiver and arguments:
    /// a dictionary, set or key collection handed to a parameter that enumerates it (hashSet.ToList(),
    /// string.Join(",", hashSet), list.AddRange(dictionary.Keys…)); a dictionary or set interface enumerated as a sequence
    /// (foreach over an IReadOnlyDictionary); and key-value pairs enumerated from a source not known to be ordered
    /// (foreach or LINQ over an IEnumerable&lt;KeyValuePair&lt;K, V&gt;&gt;). Members whose result does not depend on the
    /// order they see the elements in (Count, Any, Contains, set operations, building another set or dictionary) and the
    /// sorts that put them in order (OrderBy) stay allowed: sorting is how CLAUDE.md asks for an explicit order.
    /// </summary>
    internal static class UnorderedSequences
    {
        private const string PairType = "System.Collections.Generic.KeyValuePair`2";

        private const string EnumerableType = "System.Linq.Enumerable";

        /// <summary>Static types whose enumeration order is a hash table's, or may be: the interfaces of dictionaries and sets.</summary>
        private static readonly IReadOnlySet<string> HashOrdered = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.Dictionary`2+KeyCollection",
            "System.Collections.Generic.Dictionary`2+ValueCollection", "System.Collections.Generic.HashSet`1",
            "System.Collections.Generic.ISet`1", "System.Collections.Generic.IReadOnlySet`1",
            "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.IDictionary`2",
            "System.Collections.Concurrent.ConcurrentDictionary`2", "System.Collections.Concurrent.ConcurrentBag`1",
            "System.Collections.Immutable.ImmutableHashSet`1", "System.Collections.Immutable.ImmutableDictionary`2",
            "System.Collections.Immutable.IImmutableSet`1", "System.Collections.Immutable.IImmutableDictionary`2",
        };

        /// <summary>Parameter and interface types through which code enumerates what it is given.</summary>
        private static readonly IReadOnlySet<string> Sequences = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections.Generic.IEnumerable`1", "System.Collections.IEnumerable",
            "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IReadOnlyCollection`1",
        };

        /// <summary>Static types that enumerate in an order of their own: insertion, index or sorted order.</summary>
        private static readonly IReadOnlySet<string> Ordered = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Linq.IOrderedEnumerable`1", "System.Collections.Generic.List`1", "System.Collections.Generic.IList`1",
            "System.Collections.Generic.IReadOnlyList`1", "System.Array", "System.Collections.ObjectModel.ReadOnlyCollection`1",
            "System.Collections.Generic.SortedDictionary`2", "System.Collections.Generic.SortedList`2", "System.Collections.Generic.SortedSet`1",
            "System.Collections.Generic.Queue`1", "System.Collections.Generic.Stack`1", "System.Collections.Generic.LinkedList`1",
            "System.Collections.Immutable.ImmutableArray`1", "System.Collections.Immutable.ImmutableList`1",
            "System.Collections.Immutable.ImmutableSortedDictionary`2", "System.Collections.Immutable.ImmutableSortedSet`1",
        };

        /// <summary>Members ("Type::Member") whose result does not depend on the order they see the elements in, or that sort them.</summary>
        private static readonly IReadOnlySet<string> OrderInsensitive = new HashSet<string>(
            new[] { "Count", "LongCount", "Any", "All", "Contains", "Min", "Max", "OrderBy", "OrderByDescending", "Order", "OrderDescending", "ToHashSet", "ToDictionary" }
                .Select(member => EnumerableType + "::" + member)
                .Concat(new[] { "System.Collections.Generic.HashSet`1", "System.Collections.Generic.SortedSet`1", "System.Collections.Generic.ISet`1" }
                    .SelectMany(set => new[]
                    {
                        ".ctor", "UnionWith", "IntersectWith", "ExceptWith", "SymmetricExceptWith", "IsSubsetOf", "IsSupersetOf",
                        "IsProperSubsetOf", "IsProperSupersetOf", "Overlaps", "SetEquals",
                    }.Select(member => set + "::" + member)))
                .Concat(new[]
                {
                    "System.Collections.Generic.Dictionary`2::.ctor", "System.Collections.Generic.SortedDictionary`2::.ctor",
                    "System.Collections.Generic.SortedList`2::.ctor",
                }),
            StringComparer.Ordinal);

        /// <summary>Whether this use enumerates a dictionary, a set or key-value pairs in an order nothing fixes.</summary>
        public static bool EnumeratesInHashOrder(MemberUse use)
        {
            if (OrderInsensitive.Contains(use.DeclaringType.DefinitionName + "::" + use.MemberName))
            {
                return false;
            }

            bool passesHashOrderedSequence = use.Parameters.Zip(use.Arguments, (parameter, argument) => (Parameter: parameter, Argument: argument))
                .Any(pair => Sequences.Contains(pair.Parameter.DefinitionName) && IsHashOrdered(pair.Argument));
            bool enumeratesHashOrderedReceiver = Sequences.Contains(use.DeclaringType.DefinitionName) && IsHashOrdered(use.Instance);
            return passesHashOrderedSequence || enumeratesHashOrderedReceiver || EnumeratesPairsOfUnknownOrder(use);
        }

        /// <summary>
        /// A LINQ method over key-value pairs, or a foreach over IEnumerable&lt;KeyValuePair&lt;K, V&gt;&gt;, whose source is not
        /// known to be ordered: such a sequence is usually a dictionary seen through an interface.
        /// </summary>
        private static bool EnumeratesPairsOfUnknownOrder(MemberUse use)
        {
            bool isLinqOverPairs = use.DeclaringType.DefinitionName == EnumerableType
                && use.MethodTypeArguments.Count > 0 && use.MethodTypeArguments[0].DefinitionName == PairType;
            if (isLinqOverPairs)
            {
                return !IsOrdered(use.Arguments.Count > 0 ? use.Arguments[0] : null);
            }

            bool isForeachOverPairs = use.MemberName == "GetEnumerator"
                && use.DeclaringType.DefinitionName == "System.Collections.Generic.IEnumerable`1"
                && use.DeclaringType.TypeArguments.Length == 1 && use.DeclaringType.TypeArguments[0].DefinitionName == PairType;
            return isForeachOverPairs && !IsOrdered(use.Instance);
        }

        private static bool IsHashOrdered(TypeShape? type) => type is not null && HashOrdered.Contains(type.DefinitionName);

        private static bool IsOrdered(TypeShape? type) => type is not null && Ordered.Contains(type.DefinitionName);
    }
}

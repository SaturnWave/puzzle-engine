using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// Every member below breaks a determinism rule on purpose. The analyzers would (rightly) reject some of them in a .NET
// build, hence the local suppressions; Unity runs no analyzers, so the IL scan is what must see them.
#pragma warning disable CA1304, CA1305, CA1309, CA1311

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// Culture- and order-dependent uses that the policy let through before the walk and the bans learned them: each
    /// form below passed both policies, and the guard tests expect each now at its member. Lives in the test assembly
    /// only.
    /// </summary>
    internal static class PolicyGapSamples
    {
        // An integer formatted through System.Object.ToString, which names no integer type: the walk learns the value's own
        // type from box or the constrained. prefix.
        public static string BoxedNumber(int value) => ((object)value).ToString()!;

        public static string NullableNumber(int? value) => value.ToString()!;

        public static string TupleOfNumbers(int first, int second) => (first, second).ToString();

        public static string PairOfNumber(KeyValuePair<string, int> pair) => pair.ToString();

        public static string GenericValue<T>(T value) => value!.ToString()!;

        public static string GenericBoxed<T>(T value) => ((object)value!).ToString()!;

        public static string GenericConcatenated<T>(T value) => "value " + value;

        // A delegate over a boxed integer's ToString: ldvirtftn takes the object the delegate will call.
        public static string NumberToStringDelegate(int value)
        {
            Func<string> text = value.ToString;
            return text();
        }

        // A null provider or culture, which stands for the device culture: the overload takes one, so the bans on the
        // overloads without one let it through. Two of them pass null by the parameter's default value.
        public static string NullProviderToString(int value) => value.ToString((IFormatProvider?)null);

        public static string NullProviderWithFormat(int value) => value.ToString("D", null);

        public static int NullProviderParse(string text) => int.Parse(text, null);

        public static string NullProviderFormat(int value) => string.Format(null, "{0} pieces", value);

        public static StringBuilder NullProviderAppendFormat(StringBuilder builder, int value) => builder.AppendFormat(null, "{0} pieces", value);

        public static bool DefaultProviderTryFormat(int value, Span<char> destination) => value.TryFormat(destination, out _);

        public static int DefaultProviderSpanParse(ReadOnlySpan<char> text) => int.Parse(text);

        public static string NullCultureToUpper(string text) => text.ToUpper((CultureInfo?)null);

        // A null comparer, which stands for the default one: by the device culture for text.
        public static List<string> OrderByNullComparer(IEnumerable<string> names) => names.OrderBy(name => name, null).ToList();

        public static SortedSet<string> SortedSetNullComparer() => new SortedSet<string>((IComparer<string>?)null);

        // A StringComparison other than Ordinal and OrdinalIgnoreCase: the overload takes one, so the bans on the overloads
        // without one let it through. Every string member that takes a comparison, and the span ones, are judged alike.
        public static bool EqualsCurrentCulture(string first, string second) => first.Equals(second, StringComparison.CurrentCulture);

        public static bool StaticEqualsInvariantCulture(string first, string second) => string.Equals(first, second, StringComparison.InvariantCulture);

        public static int CompareCurrentCulture(string first, string second) => string.Compare(first, second, StringComparison.CurrentCulture);

        public static bool StartsWithCurrentCultureIgnoreCase(string text) => text.StartsWith("gold", StringComparison.CurrentCultureIgnoreCase);

        public static bool EndsWithInvariantCultureIgnoreCase(string text) => text.EndsWith("gold", StringComparison.InvariantCultureIgnoreCase);

        public static int IndexOfCurrentCulture(string text) => text.IndexOf("gold", StringComparison.CurrentCulture);

        public static int LastIndexOfCurrentCulture(string text) => text.LastIndexOf("gold", StringComparison.CurrentCulture);

        public static bool ContainsCurrentCulture(string text) => text.Contains("gold", StringComparison.CurrentCulture);

        public static bool SpanEqualsCurrentCulture(string first, string second) => first.AsSpan().Equals(second.AsSpan(), StringComparison.CurrentCulture);

        // A comparison the walk cannot read, which may be any value.
        public static bool ComparisonFromParameter(string first, string second, StringComparison comparison) => first.Equals(second, comparison);

        public static bool ComparisonFromLocal(string first, string second)
        {
            StringComparison comparison = StringComparison.CurrentCulture;
            return first.Equals(second, comparison);
        }

        // Values outside the enum, one per form of ldc.i4 that pushes them: .m1, .6, .7, .8, .s and the four-byte form.
        public static bool ComparisonMinusOne(string first, string second) => first.Equals(second, (StringComparison)(-1));

        public static bool ComparisonSix(string first, string second) => first.Equals(second, (StringComparison)6);

        public static bool ComparisonSeven(string first, string second) => first.Equals(second, (StringComparison)7);

        public static bool ComparisonEight(string first, string second) => first.Equals(second, (StringComparison)8);

        public static bool ComparisonOneHundred(string first, string second) => first.Equals(second, (StringComparison)100);

        public static bool ComparisonOneThousand(string first, string second) => first.Equals(second, (StringComparison)1000);

        // ldc.i4.s takes a signed byte.
        public static bool ComparisonMinusOneHundred(string first, string second) => first.Equals(second, (StringComparison)(-100));

        // BinarySearch over text without a comparer, or with null for one: by the device culture. An array's non-generic
        // overload compares objects, which compares text so.
        public static int ArrayBinarySearchOfObjects(Array names) => Array.BinarySearch(names, "gold");

        public static int ArrayBinarySearchNullComparer(string[] names) => Array.BinarySearch(names, "gold", null);

        public static int ListBinarySearchNullComparer(List<string> names) => names.BinarySearch("gold", null);

        public static int SpanBinarySearchComparable(string[] names) => names.AsSpan().BinarySearch((IComparable<string>)"gold");

        // CompareTo through an interface: a typed one over text, and the untyped one, which hides what it compares.
        public static int CompareThroughGenericInterfaceLocal(string first, string second)
        {
            IComparable<string> comparable = first;
            return comparable.CompareTo(second);
        }

        public static int CompareThroughUnknownComparable(IComparable comparable, string text) => comparable.CompareTo(text);

        public static int CompareElementOfComparables(IComparable[] comparables, string text) => comparables[0].CompareTo(text);

        // A value read through a member of a generic type has the type that the caller fills in: a set, text, or a T.
        public static string JoinSetOfDictionary(Dictionary<int, HashSet<string>> groups) => string.Join(",", groups[1]);

        public static List<int> ElementSetToList(List<HashSet<int>> sets) => sets[0].ToList();

        public static List<int> FirstSetToList(List<HashSet<int>> sets) => sets.First().ToList();

        public static List<int> TupleFieldSetToList((HashSet<int> Set, int Count) pair) => pair.Set.ToList();

        public static int CompareElementOfTextList(List<string> names) => ((IComparable)names[0]).CompareTo(names[1]);

        public static string ElementOfGenericList<T>(IReadOnlyList<T> values) => values[0]!.ToString()!;

        // An anonymous type formats its members as a tuple does.
        public static string AnonymousNumber(int count) => new { Count = count }.ToString()!;
    }

    /// <summary>A generic type whose own parameter (!0) is the receiver, as in an Option&lt;T&gt;.ToString.</summary>
    internal sealed class GenericHolderSample<T>
    {
        private readonly T _value;

        public GenericHolderSample(T value) => _value = value;

        public string Text() => _value!.ToString()!;
    }
}

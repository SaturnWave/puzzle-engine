using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

// Every member below breaks a determinism rule on purpose. The analyzers would (rightly) reject some of them in a .NET
// build, hence the local suppressions; Unity runs no analyzers, so the IL scan is what must see them.
#pragma warning disable CA1304, CA1305, CA1311

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
    }

    /// <summary>A generic type whose own parameter (!0) is the receiver, as in an Option&lt;T&gt;.ToString.</summary>
    internal sealed class GenericHolderSample<T>
    {
        private readonly T _value;

        public GenericHolderSample(T value) => _value = value;

        public string Text() => _value!.ToString()!;
    }
}

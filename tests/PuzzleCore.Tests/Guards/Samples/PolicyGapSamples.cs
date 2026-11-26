using System;
using System.Collections.Generic;

// Every member below breaks a determinism rule on purpose. The analyzers would (rightly) reject some of them in a .NET
// build, hence the local suppression; Unity runs no analyzers, so the IL scan is what must see them.
#pragma warning disable CA1305

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
    }

    /// <summary>A generic type whose own parameter (!0) is the receiver, as in an Option&lt;T&gt;.ToString.</summary>
    internal sealed class GenericHolderSample<T>
    {
        private readonly T _value;

        public GenericHolderSample(T value) => _value = value;

        public string Text() => _value!.ToString()!;
    }
}

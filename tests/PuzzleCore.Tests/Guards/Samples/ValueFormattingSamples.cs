using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

// Every member below formats a value by the device culture on purpose. The analyzers would (rightly) reject several
// of them, hence the local suppression.
#pragma warning disable CA1305

namespace PuzzleCore.Tests.Guards.Samples
{
    /// <summary>
    /// The members that format a value by its own ToString inside the platform, where the walk sees no ToString: each
    /// overload that takes an object, an object array or span, or a sequence of T, beyond the one use each ban plants in
    /// <see cref="MemberBanSamples"/>; a FormattableString's and an IFormattable's ToString(); and a tuple or a pair given
    /// to a format with a provider, which formats what it holds by the device culture all the same. The guard tests
    /// expect each refused under both policies; the near misses are in <see cref="CleanNearMissSample"/> and
    /// <see cref="MemberBansNearMissSample"/>. Lives in the test assembly only.
    /// </summary>
    internal static class ValueFormattingSamples
    {
        // StringBuilder.AppendJoin formats each value after the separator by its ToString.
        public static StringBuilder AppendJoinObjects(StringBuilder builder, object[] values) => builder.AppendJoin(", ", values);

        public static StringBuilder AppendJoinObjectsWithChar(StringBuilder builder, object[] values) => builder.AppendJoin(',', values);

        public static StringBuilder AppendJoinNumbersWithChar(StringBuilder builder, List<int> values) => builder.AppendJoin(',', values);

        public static StringBuilder AppendJoinObjectSpan(StringBuilder builder, object[] values) => builder.AppendJoin(", ", (ReadOnlySpan<object?>)values);

        public static StringBuilder AppendJoinObjectSpanWithChar(StringBuilder builder, object[] values) => builder.AppendJoin(',', (ReadOnlySpan<object?>)values);

        public static StringBuilder AppendJoinNumberArguments(StringBuilder builder, int first, int second) => builder.AppendJoin(", ", first, second);

        public static StringBuilder AppendJoinGeneric<T>(StringBuilder builder, IEnumerable<T> values) => builder.AppendJoin(", ", values);

        // string.Join and string.Concat do the same, one shape of overload each.
        public static string JoinObjects(object[] values) => string.Join(", ", values);

        public static string JoinObjectsWithChar(object[] values) => string.Join(',', values);

        public static string JoinNumbersWithChar(List<int> values) => string.Join(',', values);

        public static string JoinObjectSpan(object[] values) => string.Join(", ", (ReadOnlySpan<object?>)values);

        public static string JoinNumberArguments(int first, int second) => string.Join(", ", first, second);

        public static string ConcatObject(object value) => string.Concat(value);

        public static string ConcatThreeObjects(object first, object second, object third) => string.Concat(first, second, third);

        public static string ConcatObjects(object[] values) => string.Concat(values);

        public static string ConcatNumberArguments(int first, int second, int third, int fourth) => string.Concat(first, second, third, fourth);

        public static string ConcatNumbers(List<int> values) => string.Concat(values);

        // StringBuilder.Append, Insert and AppendFormat, and string.Format without a provider.
        public static StringBuilder AppendObject(StringBuilder builder, object value) => builder.Append(value);

        public static StringBuilder InsertObject(StringBuilder builder, object value) => builder.Insert(0, value);

        public static StringBuilder AppendFormatTwoValues(StringBuilder builder, int first, int second) => builder.AppendFormat("{0},{1}", first, second);

        public static StringBuilder AppendFormatArray(StringBuilder builder, object[] values) => builder.AppendFormat("{0}", values);

        public static StringBuilder AppendFormatNumberArguments(StringBuilder builder, int first, int second, int third, int fourth) =>
            builder.AppendFormat("{0}{1}{2}{3}", first, second, third, fourth);

        public static string FormatTwoValues(int first, int second) => string.Format("{0},{1}", first, second);

        public static string FormatArray(object[] values) => string.Format("{0}", values);

        public static string FormatNumberArguments(int first, int second, int third, int fourth) =>
            string.Format("{0}{1}{2}{3}", first, second, third, fourth);

        // A FormattableString keeps the format and the values; its ToString() formats them by the device culture, as an
        // IFormattable's does.
        public static string FormattableStringToString(int pieces)
        {
            FormattableString text = $"{pieces} pieces";
            return text.ToString();
        }

        public static string CreatedFormattableStringToString(int pieces) => FormattableStringFactory.Create("{0} pieces", pieces).ToString();

        public static string FormattableToString(int pieces)
        {
            IFormattable text = $"{pieces} pieces";
            return text.ToString()!;
        }

        public static string FormattableNumberToString(int pieces)
        {
            IFormattable number = pieces;
            return number.ToString()!;
        }

        // A writer formats a value by its FormatProvider, a StreamWriter's included; Console writes through Console.Out.
        public static void WriteObject(TextWriter writer, object value) => writer.Write(value);

        public static void WriteFormat(TextWriter writer, int pieces) => writer.Write("{0} pieces", pieces);

        public static void WriteFormatArray(TextWriter writer, object[] values) => writer.Write("{0}", values);

        public static void WriteNumberArguments(TextWriter writer, int first, int second, int third, int fourth) =>
            writer.Write("{0}{1}{2}{3}", first, second, third, fourth);

        public static void StreamWriterWrite(StreamWriter writer, long value) => writer.Write(value);

        public static void WriteLineNumber(TextWriter writer, int value) => writer.WriteLine(value);

        public static void WriteLineFormat(TextWriter writer, int pieces) => writer.WriteLine("{0} pieces", pieces);

        public static void ConsoleWriteObject(object value) => Console.Write(value);

        public static void ConsoleWriteFormat(int pieces) => Console.Write("{0} pieces", pieces);

        public static void ConsoleWriteLineNumber(int value) => Console.WriteLine(value);

        public static void ConsoleOutWrite(int value) => Console.Out.Write(value);

        public static string ConvertObject(object value) => Convert.ToString(value)!;

        // A provider does not reach a tuple or a pair: neither is IFormattable, so each formats what it holds by its own
        // ToString. A tuple that holds a generic T may hold a number.
        public static string FormatTupleWithProvider(int first, int second) => string.Format(CultureInfo.InvariantCulture, "{0}", (first, second));

        public static string FormatPairWithProvider(KeyValuePair<string, int> pair) => string.Format(CultureInfo.InvariantCulture, "{0}", pair);

        public static string FormatTupleWithCompositeFormat(CompositeFormat format, int first, int second) =>
            string.Format(CultureInfo.InvariantCulture, format, (first, second));

        public static StringBuilder AppendFormatTupleWithProvider(StringBuilder builder, int first, int second) =>
            builder.AppendFormat(CultureInfo.InvariantCulture, "{0}", (first, second));

        public static string InterpolatedTupleOfGeneric<T>(T value) => string.Create(CultureInfo.InvariantCulture, $"{(value, "pieces")}");
    }
}

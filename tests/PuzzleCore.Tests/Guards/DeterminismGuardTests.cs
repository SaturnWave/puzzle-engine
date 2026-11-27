using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PuzzleCore.Tests.Support;
using Xunit;

namespace PuzzleCore.Tests.Guards
{
    /// <summary>
    /// The same seed and the same actions must give the same game on every device (docs/shared-design.md, §3).
    /// These tests read the compiled Unity-bound assemblies and fail on anything that can make results differ between
    /// machines: floating point, platform APIs outside the allowlist (randomness, the clock, time zones, the environment,
    /// threads, reflection, native calls), culture-dependent text, randomized hashing, unordered enumeration and unstable
    /// sorts. The bans do not see every way to that behaviour: the policy's remarks list the known gaps.
    /// The self-tests prove that the scanners report each planted violation at its member, among them a planted use of
    /// every member ban, each planted gap the policy let through before, every shape of overload of the members that
    /// format a value by its own ToString inside the platform, and a planted break of every member limit of the
    /// allowlist; that they stay silent on the clean samples, the near misses of the gaps among them, whose declared types
    /// the allowlist all allows; that the member bans alone (<see cref="MemberBansPolicy"/>) report the planted bans but
    /// refuse no type; and that each known gap the policy's remarks list still passes. Ported from the Block engine.
    /// </summary>
    public sealed class DeterminismGuardTests
    {
        private const string Samples = "PuzzleCore.Tests.Guards.Samples.";

        private const string Culture = "depends on the device culture";
        private const string NullCulture = "a null provider or culture stands for it";
        private const string TextOrder = "orders text by the device culture";
        private const string RandomizedHash = "hash codes are randomized per process";
        private const string HashOrder = "enumeration order is an implementation detail";
        private const string UnstableSort = "unstable sort";
        private const string ThreadIdentity = "thread identity must not influence results";
        private const string Newline = "Environment.NewLine differs";
        private const string WriterCulture = "a writer formats a value by its FormatProvider";
        private const string TupleText = "a provider does not reach a tuple or a pair";

        /// <summary>Begins the reason of a StringComparison the rule refuses; the value the call passes follows.</summary>
        private const string Passes = "unless the comparison is StringComparison.Ordinal or OrdinalIgnoreCase, written at the call; this call passes ";

        /// <summary>
        /// Code that keeps every rule these scans check: realistic integer-only code, records whose compiler-written members
        /// must pass (one of them only through the record exemption), a GetHashCode override that passes only through the
        /// hash-code exemption, and the near misses of the planted gaps.
        /// </summary>
        private static readonly string[] CleanSamples =
        {
            Samples + "CleanIntegerOnlySample", Samples + "CleanCell", Samples + "CleanNamedCell", Samples + "CleanHashedName",
            Samples + "CleanNearMissSample",
        };

        /// <summary>The scans of this test assembly, made once for the whole run: the assembly cannot change while its own tests run.</summary>
        private static readonly Lazy<SampleScans> Scans = new(ScanSamples);

        public static TheoryData<string> UnityBoundAssemblies => new(ProjectRules.UnityBoundProjects);

        /// <summary>
        /// The planted violations of Guards/Samples/PolicyEdgeSamples.cs that both policies report: a set whose type the
        /// walk learns from one kind of instruction each, each hash-ordered type and sequence parameter type, the receiver
        /// and LINQ-over-pairs rules, two comparison constants that meet at a branch, and hand-written members that carry
        /// the names of the members the compiler writes.
        /// </summary>
        public static TheoryData<string, string> PlantedEdgeViolations => new()
        {
            { "PolicyEdgeSamples::SetFromLocal", HashOrder },
            { "PolicyEdgeSamples::SetFromField", HashOrder },
            { "PolicyEdgeSamples::SetFromNewobj", HashOrder },
            { "PolicyEdgeSamples::SetFromDup", HashOrder },
            { "PolicyEdgeSamples::SetFromCast", HashOrder },
            { "PolicyEdgeSamples::SetAcrossBranch", HashOrder },
            { "PolicyEdgeSamples::DictionaryIntoList", HashOrder },
            { "PolicyEdgeSamples::KeyCollectionToList", HashOrder },
            { "PolicyEdgeSamples::ValueCollectionToList", HashOrder },
            { "PolicyEdgeSamples::HashSetToList", HashOrder },
            { "PolicyEdgeSamples::SetInterfaceToList", HashOrder },
            { "PolicyEdgeSamples::ReadOnlySetToList", HashOrder },
            { "PolicyEdgeSamples::ReadOnlyDictionaryIntoList", HashOrder },
            { "PolicyEdgeSamples::DictionaryInterfaceIntoList", HashOrder },
            { "PolicyEdgeSamples::ConcurrentDictionaryIntoList", HashOrder },
            { "PolicyEdgeSamples::ConcurrentBagToList", HashOrder },
            { "PolicyEdgeSamples::ImmutableHashSetToList", HashOrder },
            { "PolicyEdgeSamples::ImmutableDictionaryIntoList", HashOrder },
            { "PolicyEdgeSamples::ImmutableSetInterfaceToList", HashOrder },
            { "PolicyEdgeSamples::ImmutableDictionaryInterfaceIntoList", HashOrder },
            { "PolicyEdgeSamples::SetAsNonGenericSequence", HashOrder },
            { "PolicyEdgeSamples::SetAsCollection", HashOrder },
            { "PolicyEdgeSamples::SetAsReadOnlyCollection", HashOrder },
            { "PolicyEdgeSamples::SumOfSetInterface", HashOrder },
            { "PolicyEdgeSamples::SetCopiedThroughCollection", HashOrder },
            { "PolicyEdgeSamples::ValuesOfPairs", HashOrder },
            { "PolicyEdgeSamples::ChoiceOfOrdinalComparisons", Passes + "a value the scan cannot read" },
            { "HandWrittenMembers::.ctor", ThreadIdentity },
            { "HandWrittenMembers::ToString", Culture },
            { "HandWrittenMembers::GetHashCode", Culture },
        };

        /// <summary>
        /// The planted gaps of Guards/Samples/PolicyGapSamples.cs, which both policies let through before: the sample
        /// member, the member the violation names, as Type::Member without generic arguments, and a fragment of the reason.
        /// </summary>
        public static TheoryData<string, string, string> PlantedGaps => new()
        {
            { "PolicyGapSamples::BoxedNumber", "System.Object::ToString", Culture },
            { "PolicyGapSamples::NullableNumber", "System.Object::ToString", Culture },
            { "PolicyGapSamples::TupleOfNumbers", "System.Object::ToString", Culture },
            { "PolicyGapSamples::PairOfNumber", "System.Object::ToString", Culture },
            { "PolicyGapSamples::GenericValue", "System.Object::ToString", Culture },
            { "PolicyGapSamples::GenericBoxed", "System.Object::ToString", Culture },
            { "PolicyGapSamples::GenericConcatenated", "System.Object::ToString", Culture },
            { "PolicyGapSamples::NumberToStringDelegate", "System.Object::ToString", Culture },
            { "GenericHolderSample`1::Text", "System.Object::ToString", Culture },
            { "PolicyGapSamples::NullProviderToString", "System.Int32::ToString", NullCulture },
            { "PolicyGapSamples::NullProviderWithFormat", "System.Int32::ToString", NullCulture },
            { "PolicyGapSamples::NullProviderParse", "System.Int32::Parse", NullCulture },
            { "PolicyGapSamples::NullProviderFormat", "System.String::Format", NullCulture },
            { "PolicyGapSamples::NullProviderAppendFormat", "System.Text.StringBuilder::AppendFormat", NullCulture },
            { "PolicyGapSamples::DefaultProviderTryFormat", "System.Int32::TryFormat", NullCulture },
            { "PolicyGapSamples::DefaultProviderSpanParse", "System.Int32::Parse", NullCulture },
            { "PolicyGapSamples::NullCultureToUpper", "System.String::ToUpper", NullCulture },
            { "PolicyGapSamples::OrderByNullComparer", "System.Linq.Enumerable::OrderBy", TextOrder },
            { "PolicyGapSamples::SortedSetNullComparer", "System.Collections.Generic.SortedSet`1::.ctor", TextOrder },
            { "PolicyGapSamples::EqualsCurrentCulture", "System.String::Equals", Passes + "StringComparison.CurrentCulture" },
            { "PolicyGapSamples::StaticEqualsInvariantCulture", "System.String::Equals", Passes + "StringComparison.InvariantCulture" },
            { "PolicyGapSamples::CompareCurrentCulture", "System.String::Compare", Passes + "StringComparison.CurrentCulture" },
            { "PolicyGapSamples::StartsWithCurrentCultureIgnoreCase", "System.String::StartsWith", Passes + "StringComparison.CurrentCultureIgnoreCase" },
            { "PolicyGapSamples::EndsWithInvariantCultureIgnoreCase", "System.String::EndsWith", Passes + "StringComparison.InvariantCultureIgnoreCase" },
            { "PolicyGapSamples::IndexOfCurrentCulture", "System.String::IndexOf", Passes + "StringComparison.CurrentCulture" },
            { "PolicyGapSamples::LastIndexOfCurrentCulture", "System.String::LastIndexOf", Passes + "StringComparison.CurrentCulture" },
            { "PolicyGapSamples::ContainsCurrentCulture", "System.String::Contains", Passes + "StringComparison.CurrentCulture" },
            { "PolicyGapSamples::SpanEqualsCurrentCulture", "System.MemoryExtensions::Equals", Passes + "StringComparison.CurrentCulture" },
            { "PolicyGapSamples::ComparisonFromParameter", "System.String::Equals", Passes + "a value the scan cannot read" },
            { "PolicyGapSamples::ComparisonFromLocal", "System.String::Equals", Passes + "a value the scan cannot read" },
            { "PolicyGapSamples::ComparisonMinusOne", "System.String::Equals", Passes + "the number -1" },
            { "PolicyGapSamples::ComparisonSix", "System.String::Equals", Passes + "the number 6" },
            { "PolicyGapSamples::ComparisonSeven", "System.String::Equals", Passes + "the number 7" },
            { "PolicyGapSamples::ComparisonEight", "System.String::Equals", Passes + "the number 8" },
            { "PolicyGapSamples::ComparisonOneHundred", "System.String::Equals", Passes + "the number 100" },
            { "PolicyGapSamples::ComparisonOneThousand", "System.String::Equals", Passes + "the number 1000" },
            { "PolicyGapSamples::ComparisonMinusOneHundred", "System.String::Equals", Passes + "the number -100" },
            { "PolicyGapSamples::ArrayBinarySearchOfObjects", "System.Array::BinarySearch", TextOrder },
            { "PolicyGapSamples::ArrayBinarySearchNullComparer", "System.Array::BinarySearch", TextOrder },
            { "PolicyGapSamples::ListBinarySearchNullComparer", "System.Collections.Generic.List`1::BinarySearch", TextOrder },
            { "PolicyGapSamples::SpanBinarySearchComparable", "System.MemoryExtensions::BinarySearch", TextOrder },
            { "PolicyGapSamples::CompareThroughGenericInterfaceLocal", "System.IComparable`1::CompareTo", TextOrder },
            { "PolicyGapSamples::CompareThroughUnknownComparable", "System.IComparable::CompareTo", TextOrder },
            { "PolicyGapSamples::CompareElementOfComparables", "System.IComparable::CompareTo", TextOrder },
            { "PolicyGapSamples::JoinSetOfDictionary", "System.String::Join", HashOrder },
            { "PolicyGapSamples::ElementSetToList", "System.Linq.Enumerable::ToList", HashOrder },
            { "PolicyGapSamples::FirstSetToList", "System.Linq.Enumerable::ToList", HashOrder },
            { "PolicyGapSamples::TupleFieldSetToList", "System.Linq.Enumerable::ToList", HashOrder },
            { "PolicyGapSamples::CompareElementOfTextList", "System.IComparable::CompareTo", TextOrder },
            { "PolicyGapSamples::ElementOfGenericList", "System.Object::ToString", Culture },
            { "PolicyGapSamples::AnonymousNumber", "System.Object::ToString", Culture },
        };

        /// <summary>
        /// The members that format a value by its own ToString inside the platform (Guards/Samples/ValueFormattingSamples.cs),
        /// beyond the one use each ban plants: every shape of overload of StringBuilder.AppendJoin, string.Join and Concat,
        /// StringBuilder.Append, Insert and AppendFormat, string.Format, TextWriter, Console and Convert that takes a value; a
        /// FormattableString's and an IFormattable's ToString(); and a tuple or a pair given to a format with a provider.
        /// The sample member, the member the violation names, and a fragment of the reason.
        /// </summary>
        public static TheoryData<string, string, string> PlantedValueFormatting => new()
        {
            { "ValueFormattingSamples::AppendJoinObjects", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::AppendJoinObjectsWithChar", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::AppendJoinNumbersWithChar", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::AppendJoinObjectSpan", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::AppendJoinObjectSpanWithChar", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::AppendJoinNumberArguments", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::AppendJoinGeneric", "System.Text.StringBuilder::AppendJoin", Culture },
            { "ValueFormattingSamples::JoinObjects", "System.String::Join", Culture },
            { "ValueFormattingSamples::JoinObjectsWithChar", "System.String::Join", Culture },
            { "ValueFormattingSamples::JoinNumbersWithChar", "System.String::Join", Culture },
            { "ValueFormattingSamples::JoinObjectSpan", "System.String::Join", Culture },
            { "ValueFormattingSamples::JoinNumberArguments", "System.String::Join", Culture },
            { "ValueFormattingSamples::ConcatObject", "System.String::Concat", Culture },
            { "ValueFormattingSamples::ConcatThreeObjects", "System.String::Concat", Culture },
            { "ValueFormattingSamples::ConcatObjects", "System.String::Concat", Culture },
            { "ValueFormattingSamples::ConcatNumberArguments", "System.String::Concat", Culture },
            { "ValueFormattingSamples::ConcatNumbers", "System.String::Concat", Culture },
            { "ValueFormattingSamples::AppendObject", "System.Text.StringBuilder::Append", Culture },
            { "ValueFormattingSamples::InsertObject", "System.Text.StringBuilder::Insert", Culture },
            { "ValueFormattingSamples::AppendFormatTwoValues", "System.Text.StringBuilder::AppendFormat", Culture },
            { "ValueFormattingSamples::AppendFormatArray", "System.Text.StringBuilder::AppendFormat", Culture },
            { "ValueFormattingSamples::AppendFormatNumberArguments", "System.Text.StringBuilder::AppendFormat", Culture },
            { "ValueFormattingSamples::FormatTwoValues", "System.String::Format", Culture },
            { "ValueFormattingSamples::FormatArray", "System.String::Format", Culture },
            { "ValueFormattingSamples::FormatNumberArguments", "System.String::Format", Culture },
            { "ValueFormattingSamples::FormattableStringToString", "System.Object::ToString", Culture },
            { "ValueFormattingSamples::CreatedFormattableStringToString", "System.Object::ToString", Culture },
            { "ValueFormattingSamples::FormattableToString", "System.Object::ToString", Culture },
            { "ValueFormattingSamples::FormattableNumberToString", "System.Object::ToString", Culture },
            { "ValueFormattingSamples::WriteObject", "System.IO.TextWriter::Write", WriterCulture },
            { "ValueFormattingSamples::WriteFormat", "System.IO.TextWriter::Write", WriterCulture },
            { "ValueFormattingSamples::WriteFormatArray", "System.IO.TextWriter::Write", WriterCulture },
            { "ValueFormattingSamples::WriteNumberArguments", "System.IO.TextWriter::Write", WriterCulture },
            { "ValueFormattingSamples::StreamWriterWrite", "System.IO.TextWriter::Write", WriterCulture },
            { "ValueFormattingSamples::WriteLineNumber", "System.IO.TextWriter::WriteLine", WriterCulture },
            { "ValueFormattingSamples::WriteLineFormat", "System.IO.TextWriter::WriteLine", WriterCulture },
            { "ValueFormattingSamples::ConsoleWriteObject", "System.Console::Write", WriterCulture },
            { "ValueFormattingSamples::ConsoleWriteFormat", "System.Console::Write", WriterCulture },
            { "ValueFormattingSamples::ConsoleWriteLineNumber", "System.Console::WriteLine", WriterCulture },
            { "ValueFormattingSamples::ConsoleOutWrite", "System.IO.TextWriter::Write", WriterCulture },
            { "ValueFormattingSamples::ConvertObject", "System.Convert::ToString", Culture },
            { "ValueFormattingSamples::FormatTupleWithProvider", "System.String::Format", TupleText },
            { "ValueFormattingSamples::FormatPairWithProvider", "System.String::Format", TupleText },
            { "ValueFormattingSamples::FormatTupleWithCompositeFormat", "System.String::Format", TupleText },
            { "ValueFormattingSamples::AppendFormatTupleWithProvider", "System.Text.StringBuilder::AppendFormat", TupleText },
            { "ValueFormattingSamples::InterpolatedTupleOfGeneric", "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::AppendFormatted", TupleText },
        };

        /// <summary>
        /// One planted use of each member ban (Guards/Samples/MemberBanSamples.cs): the sample member, the banned member
        /// it uses, as Type::Member without generic arguments, and a fragment of the ban's reason.
        /// </summary>
        public static TheoryData<string, string, string> PlantedBans => new()
        {
            { "MemberBanSamples::ByteToString", "System.Byte::ToString", Culture },
            { "MemberBanSamples::ByteParse", "System.Byte::Parse", Culture },
            { "MemberBanSamples::ByteTryParse", "System.Byte::TryParse", Culture },
            { "MemberBanSamples::SByteToString", "System.SByte::ToString", Culture },
            { "MemberBanSamples::SByteParse", "System.SByte::Parse", Culture },
            { "MemberBanSamples::SByteTryParse", "System.SByte::TryParse", Culture },
            { "MemberBanSamples::Int16ToString", "System.Int16::ToString", Culture },
            { "MemberBanSamples::Int16Parse", "System.Int16::Parse", Culture },
            { "MemberBanSamples::Int16TryParse", "System.Int16::TryParse", Culture },
            { "MemberBanSamples::UInt16ToString", "System.UInt16::ToString", Culture },
            { "MemberBanSamples::UInt16Parse", "System.UInt16::Parse", Culture },
            { "MemberBanSamples::UInt16TryParse", "System.UInt16::TryParse", Culture },
            { "MemberBanSamples::Int32ToString", "System.Int32::ToString", Culture },
            { "MemberBanSamples::Int32Parse", "System.Int32::Parse", Culture },
            { "MemberBanSamples::Int32TryParse", "System.Int32::TryParse", Culture },
            { "MemberBanSamples::UInt32ToString", "System.UInt32::ToString", Culture },
            { "MemberBanSamples::UInt32Parse", "System.UInt32::Parse", Culture },
            { "MemberBanSamples::UInt32TryParse", "System.UInt32::TryParse", Culture },
            { "MemberBanSamples::Int64ToString", "System.Int64::ToString", Culture },
            { "MemberBanSamples::Int64Parse", "System.Int64::Parse", Culture },
            { "MemberBanSamples::Int64TryParse", "System.Int64::TryParse", Culture },
            { "MemberBanSamples::UInt64ToString", "System.UInt64::ToString", Culture },
            { "MemberBanSamples::UInt64Parse", "System.UInt64::Parse", Culture },
            { "MemberBanSamples::UInt64TryParse", "System.UInt64::TryParse", Culture },
            { "MemberBanSamples::StringToUpper", "System.String::ToUpper", Culture },
            { "MemberBanSamples::StringToLower", "System.String::ToLower", Culture },
            { "MemberBanSamples::StringCompare", "System.String::Compare", Culture },
            { "MemberBanSamples::StringCompareTo", "System.String::CompareTo", Culture },
            { "MemberBanSamples::StringStartsWith", "System.String::StartsWith", Culture },
            { "MemberBanSamples::StringEndsWith", "System.String::EndsWith", Culture },
            { "MemberBanSamples::StringIndexOf", "System.String::IndexOf", Culture },
            { "MemberBanSamples::StringLastIndexOf", "System.String::LastIndexOf", Culture },
            { "MemberBanSamples::StringFormat", "System.String::Format", Culture },
            { "MemberBanSamples::StringConcat", "System.String::Concat", Culture },
            { "MemberBanSamples::StringJoin", "System.String::Join", Culture },
            { "MemberBanSamples::CharToUpper", "System.Char::ToUpper", Culture },
            { "MemberBanSamples::CharToLower", "System.Char::ToLower", Culture },
            { "MemberBanSamples::StringBuilderAppend", "System.Text.StringBuilder::Append", Culture },
            { "MemberBanSamples::StringBuilderInsert", "System.Text.StringBuilder::Insert", Culture },
            { "MemberBanSamples::StringBuilderAppendFormat", "System.Text.StringBuilder::AppendFormat", Culture },
            { "MemberBanSamples::StringBuilderAppendJoin", "System.Text.StringBuilder::AppendJoin", Culture },
            { "MemberBanSamples::StringBuilderAppendLine", "System.Text.StringBuilder::AppendLine", Newline },
            { "MemberBanSamples::InterpolatedString", "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::.ctor", Culture },
            { "MemberBanSamples::InterpolatedAppend", "System.Text.StringBuilder+AppendInterpolatedStringHandler::.ctor", Culture },
            { "MemberBanSamples::InterpolatedSpanWrite", "System.MemoryExtensions+TryWriteInterpolatedStringHandler::.ctor", Culture },
            { "MemberBanSamples::InterpolatedUtf8Write", "System.Text.Unicode.Utf8+TryWriteInterpolatedStringHandler::.ctor", Culture },
            { "MemberBanSamples::InterpolatedTuple", "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler::AppendFormatted", TupleText },
            { "MemberBanSamples::InterpolatedAppendTuple", "System.Text.StringBuilder+AppendInterpolatedStringHandler::AppendFormatted", TupleText },
            { "MemberBanSamples::InterpolatedSpanWriteTuple", "System.MemoryExtensions+TryWriteInterpolatedStringHandler::AppendFormatted", TupleText },
            { "MemberBanSamples::InterpolatedUtf8WriteTuple", "System.Text.Unicode.Utf8+TryWriteInterpolatedStringHandler::AppendFormatted", TupleText },
            { "MemberBanSamples::ObjectToString", "System.Object::ToString", Culture },
            { "MemberBanSamples::FormattableStringCurrentCulture", "System.FormattableString::CurrentCulture", Culture },
            { "MemberBanSamples::ConvertToString", "System.Convert::ToString", Culture },
            { "MemberBanSamples::TextWriterWrite", "System.IO.TextWriter::Write", WriterCulture },
            { "MemberBanSamples::TextWriterWriteLine", "System.IO.TextWriter::WriteLine", WriterCulture },
            { "MemberBanSamples::ConsoleWrite", "System.Console::Write", WriterCulture },
            { "MemberBanSamples::ConsoleWriteLine", "System.Console::WriteLine", WriterCulture },
            { "MemberBanSamples::CurrentCulture", "System.Globalization.CultureInfo::get_CurrentCulture", Culture },
            { "MemberBanSamples::CurrentUICulture", "System.Globalization.CultureInfo::get_CurrentUICulture", Culture },
            { "MemberBanSamples::CurrentCultureComparer", "System.StringComparer::get_CurrentCulture", TextOrder },
            { "MemberBanSamples::CurrentCultureIgnoreCaseComparer", "System.StringComparer::get_CurrentCultureIgnoreCase", TextOrder },
            { "MemberBanSamples::InvariantCultureComparer", "System.StringComparer::get_InvariantCulture", TextOrder },
            { "MemberBanSamples::InvariantCultureIgnoreCaseComparer", "System.StringComparer::get_InvariantCultureIgnoreCase", TextOrder },
            { "MemberBanSamples::CreatedComparer", "System.StringComparer::Create", TextOrder },
            { "MemberBanSamples::ComparerFromComparison", "System.StringComparer::FromComparison", TextOrder },
            { "MemberBanSamples::NonGenericDefaultComparer", "System.Collections.Comparer::Default", TextOrder },
            { "MemberBanSamples::NonGenericDefaultInvariantComparer", "System.Collections.Comparer::DefaultInvariant", TextOrder },
            { "MemberBanSamples::DefaultTextComparer", "System.Collections.Generic.Comparer`1::get_Default", TextOrder },
            { "MemberBanSamples::SortedDictionaryOfText", "System.Collections.Generic.SortedDictionary`2::.ctor", TextOrder },
            { "MemberBanSamples::SortedSetOfText", "System.Collections.Generic.SortedSet`1::.ctor", TextOrder },
            { "MemberBanSamples::SortedListOfText", "System.Collections.Generic.SortedList`2::.ctor", TextOrder },
            { "MemberBanSamples::OrderByText", "System.Linq.Enumerable::OrderBy", TextOrder },
            { "MemberBanSamples::OrderByDescendingText", "System.Linq.Enumerable::OrderByDescending", TextOrder },
            { "MemberBanSamples::ThenByText", "System.Linq.Enumerable::ThenBy", TextOrder },
            { "MemberBanSamples::ThenByDescendingText", "System.Linq.Enumerable::ThenByDescending", TextOrder },
            { "MemberBanSamples::OrderText", "System.Linq.Enumerable::Order", TextOrder },
            { "MemberBanSamples::OrderDescendingText", "System.Linq.Enumerable::OrderDescending", TextOrder },
            { "MemberBanSamples::MinText", "System.Linq.Enumerable::Min", TextOrder },
            { "MemberBanSamples::MaxText", "System.Linq.Enumerable::Max", TextOrder },
            { "MemberBanSamples::MinByText", "System.Linq.Enumerable::MinBy", TextOrder },
            { "MemberBanSamples::MaxByText", "System.Linq.Enumerable::MaxBy", TextOrder },
            { "MemberBanSamples::ListBinarySearchText", "System.Collections.Generic.List`1::BinarySearch", TextOrder },
            { "MemberBanSamples::ArrayBinarySearchText", "System.Array::BinarySearch", TextOrder },
            { "MemberBanSamples::SpanBinarySearchText", "System.MemoryExtensions::BinarySearch", TextOrder },
            { "MemberBanSamples::ImmutableArrayBinarySearchText", "System.Collections.Immutable.ImmutableArray::BinarySearch", TextOrder },
            { "MemberBanSamples::ImmutableListBinarySearchText", "System.Collections.Immutable.ImmutableList`1::BinarySearch", TextOrder },
            { "MemberBanSamples::ImmutableListBuilderBinarySearchText", "System.Collections.Immutable.ImmutableList`1+Builder::BinarySearch", TextOrder },
            { "MemberBanSamples::ComparableCompareTo", "System.IComparable::CompareTo", TextOrder },
            { "MemberBanSamples::GenericComparableCompareTo", "System.IComparable`1::CompareTo", TextOrder },
            { "MemberBanSamples::StringReplaceWithCulture", "System.String::Replace", TextOrder },
            { "MemberBanSamples::ValueTuple2CompareTo", "System.ValueTuple`2::CompareTo", TextOrder },
            { "MemberBanSamples::ValueTuple3CompareTo", "System.ValueTuple`3::CompareTo", TextOrder },
            { "MemberBanSamples::ValueTuple4CompareTo", "System.ValueTuple`4::CompareTo", TextOrder },
            { "MemberBanSamples::CreateOrderedEnumerableText", "System.Linq.IOrderedEnumerable`1::CreateOrderedEnumerable", TextOrder },
            { "MemberBanSamples::ObjectGetHashCode", "System.Object::GetHashCode", RandomizedHash },
            { "ValueTypeHashSample::BaseHash", "System.ValueType::GetHashCode", RandomizedHash },
            { "MemberBanSamples::StringGetHashCode", "System.String::GetHashCode", RandomizedHash },
            { "MemberBanSamples::StringComparerGetHashCode", "System.StringComparer::GetHashCode", RandomizedHash },
            { "MemberBanSamples::EqualityComparerGetHashCode", "System.Collections.Generic.EqualityComparer`1::GetHashCode", RandomizedHash },
            { "MemberBanSamples::EqualityComparerInterfaceGetHashCode", "System.Collections.Generic.IEqualityComparer`1::GetHashCode", RandomizedHash },
            { "MemberBanSamples::RuntimeHelpersGetHashCode", "System.Runtime.CompilerServices.RuntimeHelpers::GetHashCode", RandomizedHash },
            { "MemberBanSamples::CurrentManagedThreadId", "System.Environment::get_CurrentManagedThreadId", ThreadIdentity },
            { "MemberBanSamples::DictionaryGetEnumerator", "System.Collections.Generic.Dictionary`2::GetEnumerator", HashOrder },
            { "MemberBanSamples::DictionaryKeys", "System.Collections.Generic.Dictionary`2::get_Keys", HashOrder },
            { "MemberBanSamples::DictionaryValues", "System.Collections.Generic.Dictionary`2::get_Values", HashOrder },
            { "MemberBanSamples::HashSetGetEnumerator", "System.Collections.Generic.HashSet`1::GetEnumerator", HashOrder },
            { "MemberBanSamples::HashSetCopyTo", "System.Collections.Generic.HashSet`1::CopyTo", HashOrder },
            { "MemberBanSamples::KeyCollectionCopyTo", "System.Collections.Generic.Dictionary`2+KeyCollection::CopyTo", HashOrder },
            { "MemberBanSamples::KeyCollectionGetEnumerator", "System.Collections.Generic.Dictionary`2+KeyCollection::GetEnumerator", HashOrder },
            { "MemberBanSamples::ValueCollectionCopyTo", "System.Collections.Generic.Dictionary`2+ValueCollection::CopyTo", HashOrder },
            { "MemberBanSamples::ValueCollectionGetEnumerator", "System.Collections.Generic.Dictionary`2+ValueCollection::GetEnumerator", HashOrder },
            { "MemberBanSamples::ReadOnlyDictionaryKeys", "System.Collections.Generic.IReadOnlyDictionary`2::get_Keys", HashOrder },
            { "MemberBanSamples::ReadOnlyDictionaryValues", "System.Collections.Generic.IReadOnlyDictionary`2::get_Values", HashOrder },
            { "MemberBanSamples::DictionaryInterfaceKeys", "System.Collections.Generic.IDictionary`2::get_Keys", HashOrder },
            { "MemberBanSamples::DictionaryInterfaceValues", "System.Collections.Generic.IDictionary`2::get_Values", HashOrder },
            { "MemberBanSamples::ListSort", "System.Collections.Generic.List`1::Sort", UnstableSort },
            { "MemberBanSamples::ArraySort", "System.Array::Sort", UnstableSort },
            { "MemberBanSamples::SpanSort", "System.MemoryExtensions::Sort", UnstableSort },
            { "MemberBanSamples::ImmutableArraySort", "System.Collections.Immutable.ImmutableArray`1::Sort", UnstableSort },
            { "MemberBanSamples::ImmutableArrayBuilderSort", "System.Collections.Immutable.ImmutableArray`1+Builder::Sort", UnstableSort },
            { "MemberBanSamples::ImmutableListSort", "System.Collections.Immutable.ImmutableList`1::Sort", UnstableSort },
            { "MemberBanSamples::ImmutableListBuilderSort", "System.Collections.Immutable.ImmutableList`1+Builder::Sort", UnstableSort },
        };

        /// <summary>One planted break of each member limit of the allowlist: the sample member and the type whose limit it breaks.</summary>
        public static TheoryData<string, string> PlantedLimitBreaks => new()
        {
            { "PolicyEdgeSamples::TextInSwedish", "System.Globalization.CultureInfo" },
            { "PolicyEdgeSamples::DefaultEncodingBytes", "System.Text.Encoding" },
            { "PolicyEdgeSamples::HashInOneCall", "System.Security.Cryptography.SHA256" },
            { "PolicyEdgeSamples::LastHash", "System.Security.Cryptography.HashAlgorithm" },
            { "PolicyEdgeSamples::HoldsReferences", "System.Runtime.CompilerServices.RuntimeHelpers" },
            { "DeterminismViolationsSample::TypeByName", "System.Type" },
            { "DeterminismViolationsSample::ProcessorCount", "System.Environment" },
        };

        [Theory]
        [MemberData(nameof(UnityBoundAssemblies))]
        public void Unity_bound_assemblies_contain_no_floating_point_types_or_instructions(string assemblyName)
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath(assemblyName));

            IReadOnlyList<Violation> violations = inspection.FloatingPointUses();

            Assert.True(violations.Count == 0, Report(assemblyName + " breaks the determinism rules", violations));
        }

        [Theory]
        [MemberData(nameof(UnityBoundAssemblies))]
        public void Unity_bound_assemblies_use_only_allowlisted_platform_apis(string assemblyName)
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath(assemblyName));

            IReadOnlyList<Violation> violations = inspection.ApiPolicyViolations(UnityBoundApiPolicy.Default);

            Assert.True(violations.Count == 0, Report(assemblyName + " breaks the determinism rules", violations));
        }

        [Fact]
        public void The_offline_writer_builds_its_text_without_culture_dependent_members()
        {
            // Gen's files (the grading report, and the presentation file that P7 pins) are compared byte for byte, so
            // the member bans apply to it as well; every platform type stays allowed, files and the clock included.
            const string Gen = "PuzzleCore.Gen";
            using var inspection = new AssemblyInspection(Path.Combine(AppContext.BaseDirectory, ProjectRules.AssemblyFileName(Gen)));

            IReadOnlyList<Violation> violations = inspection.ApiPolicyViolations(MemberBansPolicy.Default);

            Assert.True(violations.Count == 0, Report(Gen + " breaks the determinism rules", violations));
        }

        [Theory]
        [InlineData("DeterminismViolationsSample::DoubleField", "floating-point field")]
        [InlineData("DeterminismViolationsSample::HalveWithFloatLocal", "floating-point local variable")]
        [InlineData("DeterminismViolationsSample::HalveWithFloatLocal", "floating-point instruction conv.r4")]
        [InlineData("DeterminismViolationsSample::Price", "floating-point parameter or return type")]
        [InlineData("DeterminismViolationsSample::FloorOfDoubleLiteral", "floating-point instruction ldc.r8")]
        [InlineData("DeterminismViolationsSample::FloorOfDoubleLiteral", "uses floating point via System.Math::Floor(System.Double)")]
        [InlineData("DeterminismViolationsSample::NormalizedVector", "uses floating point via System.Numerics.Vector2::Normalize(System.Numerics.Vector2)")]
        [InlineData("PolicyEdgeSamples::EmptyPrices", "floating-point parameter or return type")]
        public void The_floating_point_scanner_reports_each_planted_use_at_its_member(string member, string reason)
        {
            Assert.Contains(Scans.Value.FloatingPoint, violation => violation.Location == Samples + member && violation.Reason == reason);
        }

        [Theory]
        [InlineData("DeterminismViolationsSample::NextFromSystemRandom", "uses System.Random")]
        [InlineData("DeterminismViolationsSample::ReadTheClock", "uses System.DateTime")]
        [InlineData("DeterminismViolationsSample::LocalTimeConversion", "uses System.DateTime")]
        [InlineData("DeterminismViolationsSample::NormalizedVector", "uses System.Numerics.Vector2")]
        [InlineData("DeterminismViolationsSample::PlainStringHash", RandomizedHash)]
        [InlineData("DeterminismViolationsSample::OrdinalComparerHash", RandomizedHash)]
        [InlineData("DeterminismViolationsSample::TupleHash", RandomizedHash)]
        [InlineData("DeterminismViolationsSample::IntegerToTextByConcatenation", Culture)]
        [InlineData("DeterminismViolationsSample::FormatWithoutCulture", Culture)]
        [InlineData("DeterminismViolationsSample::AppendInteger", Culture)]
        [InlineData("DeterminismViolationsSample::AppendPlatformNewline", Newline)]
        [InlineData("DeterminismViolationsSample::UpperWithDeviceCulture", Culture)]
        [InlineData("DeterminismViolationsSample::CompareWithDeviceCulture", Culture)]
        [InlineData("DeterminismViolationsSample::ChangeTypeThroughDouble", "uses System.Convert")]
        [InlineData("DeterminismViolationsSample::TypeByName", "may be used on System.Type")]
        [InlineData("DeterminismViolationsSample::ProcessorCount", "may be used on System.Environment")]
        [InlineData("DeterminismViolationsSample::ThreadIdentity", ThreadIdentity)]
        [InlineData("DeterminismViolationsSample::NativeSizedSum", "native-sized integer")]
        [InlineData("DeterminismViolationsSample::DictionaryOrder", HashOrder)]
        [InlineData("DeterminismViolationsSample::NativeTime", "native call (P/Invoke) is not allowed")]
        [InlineData("DeterminismViolationsSample::SortWithListSort", UnstableSort)]
        [InlineData("DeterminismViolationsSample::SortWithArraySort", UnstableSort)]
        [InlineData("DeterminismViolationsSample::SortWithSpanSort", UnstableSort)]
        [InlineData("DeterminismViolationsSample::OrderTextWithoutComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::OrderTextByCurrentCulture", TextOrder)]
        [InlineData("DeterminismViolationsSample::OrderByTupleHoldingText", TextOrder)]
        [InlineData("DeterminismViolationsSample::SortedDictionaryWithoutComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::SortedSetWithoutComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::CompareWithDefaultComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::ReadOnlyDictionaryOrder", HashOrder)]
        [InlineData("DeterminismViolationsSample::ReadOnlyDictionaryKeys", HashOrder)]
        [InlineData("DeterminismViolationsSample::DictionaryThroughLinq", HashOrder)]
        [InlineData("DeterminismViolationsSample::HashSetToList", HashOrder)]
        [InlineData("DeterminismViolationsSample::HashSetJoined", HashOrder)]
        [InlineData("DeterminismViolationsSample::HashSetThroughLambda", HashOrder)]
        [InlineData("DeterminismViolationsSample::PairsOfUnknownOrder", HashOrder)]
        [InlineData("DeterminismViolationsSample::InterpolatedWithDeviceCulture", Culture)]
        [InlineData("PolicyEdgeSamples::CountOfTimes", "uses System.DateTime")]
        [MemberData(nameof(PlantedEdgeViolations))]
        public void The_api_scanner_reports_each_planted_violation_at_its_method(string member, string reasonFragment)
        {
            AssertReported(Scans.Value.UnityBound, Samples + member, reasonFragment);
        }

        /// <summary>The planted member bans, and the native-sized integer and native call that the scan refuses under every policy.</summary>
        [Theory]
        [InlineData("DeterminismViolationsSample::PlainStringHash", RandomizedHash)]
        [InlineData("DeterminismViolationsSample::OrdinalComparerHash", RandomizedHash)]
        [InlineData("DeterminismViolationsSample::TupleHash", RandomizedHash)]
        [InlineData("DeterminismViolationsSample::IntegerToTextByConcatenation", Culture)]
        [InlineData("DeterminismViolationsSample::FormatWithoutCulture", Culture)]
        [InlineData("DeterminismViolationsSample::AppendInteger", Culture)]
        [InlineData("DeterminismViolationsSample::AppendPlatformNewline", Newline)]
        [InlineData("DeterminismViolationsSample::UpperWithDeviceCulture", Culture)]
        [InlineData("DeterminismViolationsSample::CompareWithDeviceCulture", Culture)]
        [InlineData("DeterminismViolationsSample::ThreadIdentity", ThreadIdentity)]
        [InlineData("DeterminismViolationsSample::NativeSizedSum", "native-sized integer")]
        [InlineData("DeterminismViolationsSample::DictionaryOrder", HashOrder)]
        [InlineData("DeterminismViolationsSample::NativeTime", "native call (P/Invoke) is not allowed")]
        [InlineData("DeterminismViolationsSample::SortWithListSort", UnstableSort)]
        [InlineData("DeterminismViolationsSample::SortWithArraySort", UnstableSort)]
        [InlineData("DeterminismViolationsSample::SortWithSpanSort", UnstableSort)]
        [InlineData("DeterminismViolationsSample::OrderTextWithoutComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::OrderTextByCurrentCulture", TextOrder)]
        [InlineData("DeterminismViolationsSample::OrderByTupleHoldingText", TextOrder)]
        [InlineData("DeterminismViolationsSample::SortedDictionaryWithoutComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::SortedSetWithoutComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::CompareWithDefaultComparer", TextOrder)]
        [InlineData("DeterminismViolationsSample::ReadOnlyDictionaryOrder", HashOrder)]
        [InlineData("DeterminismViolationsSample::ReadOnlyDictionaryKeys", HashOrder)]
        [InlineData("DeterminismViolationsSample::DictionaryThroughLinq", HashOrder)]
        [InlineData("DeterminismViolationsSample::HashSetToList", HashOrder)]
        [InlineData("DeterminismViolationsSample::HashSetJoined", HashOrder)]
        [InlineData("DeterminismViolationsSample::HashSetThroughLambda", HashOrder)]
        [InlineData("DeterminismViolationsSample::PairsOfUnknownOrder", HashOrder)]
        [InlineData("DeterminismViolationsSample::InterpolatedWithDeviceCulture", Culture)]
        [MemberData(nameof(PlantedEdgeViolations))]
        public void The_member_bans_alone_report_each_planted_ban_at_its_method(string member, string reasonFragment)
        {
            AssertReported(Scans.Value.MemberBans, Samples + member, reasonFragment);
        }

        [Theory]
        [MemberData(nameof(PlantedBans))]
        public void Each_member_ban_reports_its_planted_use_under_both_policies(string member, string bannedMember, string reasonFragment)
        {
            AssertReportedUnderBothPolicies(Samples + member, bannedMember, reasonFragment);
        }

        [Theory]
        [MemberData(nameof(PlantedGaps))]
        public void Each_planted_gap_is_reported_under_both_policies(string member, string usedMember, string reasonFragment)
        {
            AssertReportedUnderBothPolicies(Samples + member, usedMember, reasonFragment);
        }

        [Theory]
        [MemberData(nameof(PlantedValueFormatting))]
        public void Each_planted_value_formatting_is_reported_under_both_policies(string member, string usedMember, string reasonFragment)
        {
            AssertReportedUnderBothPolicies(Samples + member, usedMember, reasonFragment);
        }

        [Fact]
        public void Every_member_ban_has_a_planted_use()
        {
            IReadOnlyList<string> banned = UnityBoundApiPolicy.Default.BannedMembers;
            var planted = new HashSet<string>(PlantedBans.Select(row => row[1]).OfType<string>(), StringComparer.Ordinal);

            IReadOnlyList<string> unplanted = banned.Where(member => !planted.Contains(member)).ToList();
            IReadOnlyList<string> plantedButNotBanned = planted.Where(member => !banned.Contains(member, StringComparer.Ordinal))
                .OrderBy(member => member, StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(banned);
            Assert.True(unplanted.Count == 0, "No sample in MemberBanSamples plants a use of " + string.Join(", ", unplanted) + ".");
            Assert.True(plantedButNotBanned.Count == 0, "These planted uses name no ban of the policy: " + string.Join(", ", plantedButNotBanned) + ".");
        }

        [Theory]
        [MemberData(nameof(PlantedLimitBreaks))]
        public void Each_member_limit_of_the_allowlist_reports_its_planted_break(string member, string limitedType)
        {
            AssertReported(Scans.Value.UnityBound, Samples + member, "may be used on " + limitedType);
        }

        [Fact]
        public void Every_member_limit_of_the_allowlist_has_a_planted_break()
        {
            IReadOnlyList<string> limited = UnityBoundApiPolicy.Default.TypesWithMemberLimits;
            var broken = new HashSet<string>(PlantedLimitBreaks.Select(row => row[1]).OfType<string>(), StringComparer.Ordinal);

            IReadOnlyList<string> unbroken = limited.Where(type => !broken.Contains(type)).ToList();

            Assert.NotEmpty(limited);
            Assert.True(unbroken.Count == 0, "No sample breaks the member limit of " + string.Join(", ", unbroken) + ".");
        }

        /// <summary>Methods whose only fault is a type, or a member of an allowed type, outside the Unity-bound allowlist.</summary>
        [Theory]
        [InlineData("DeterminismViolationsSample::NextFromSystemRandom")]
        [InlineData("DeterminismViolationsSample::ReadTheClock")]
        [InlineData("DeterminismViolationsSample::LocalTimeConversion")]
        [InlineData("DeterminismViolationsSample::NormalizedVector")]
        [InlineData("DeterminismViolationsSample::ChangeTypeThroughDouble")]
        [InlineData("DeterminismViolationsSample::TypeByName")]
        [InlineData("DeterminismViolationsSample::ProcessorCount")]
        [InlineData("PolicyEdgeSamples::TextInSwedish")]
        [InlineData("PolicyEdgeSamples::DefaultEncodingBytes")]
        [InlineData("PolicyEdgeSamples::HashInOneCall")]
        [InlineData("PolicyEdgeSamples::LastHash")]
        [InlineData("PolicyEdgeSamples::HoldsReferences")]
        [InlineData("PolicyEdgeSamples::CountOfTimes")]
        // Known gaps of the member bans alone (the policy's remarks): only the allowlist keeps these out.
        [InlineData("KnownGapSamples::FloatingPointText")]
        [InlineData("KnownGapSamples::NullProviderOfInterpolation")]
        [InlineData("KnownGapSamples::TupleInAFormattableString")]
        [InlineData("KnownGapSamples::PlatformNewlineThroughWriter")]
        [InlineData("KnownGapSamples::PlatformNewlineThroughConsole")]
        [InlineData("KnownGapSamples::PlatformNewline")]
        [InlineData("KnownGapSamples::ConcurrentDictionaryOrder")]
        public void The_member_bans_alone_pass_a_method_that_the_allowlist_refuses(string member)
        {
            string at = Samples + member;

            IReadOnlyList<Violation> refused = Scans.Value.UnityBound.Where(violation => IsAt(violation, at)).ToList();
            IReadOnlyList<Violation> banned = Scans.Value.MemberBans.Where(violation => IsAt(violation, at)).ToList();

            Assert.True(refused.Count > 0, "The Unity-bound policy reports nothing at " + at + ".");
            Assert.True(refused.All(violation => IsRefusal(violation, UnityBoundApiPolicy.Default)),
                Report("The Unity-bound policy reports more than an allowlist refusal at " + at, refused));
            Assert.True(banned.Count == 0, Report("The member bans alone report " + at + ", which only the allowlist refuses", banned));
        }

        /// <summary>
        /// The known gaps that the policy's remarks list and both policies let through, each planted: a change that closes
        /// one fails here, and the remarks and this list change with it.
        /// </summary>
        [Theory]
        [InlineData("KnownGapSamples::SetHandedOnThroughLocal")]
        [InlineData("KnownGapSamples::SetHandedOnThroughHelper")]
        [InlineData("KnownGapSamples::NumberBoxedIntoLocal")]
        [InlineData("KnownGapSamples::NullProviderInLocal")]
        [InlineData("KnownGapSamples::NumberInAnObjectArray")]
        [InlineData("KnownGapSamples::ComparisonThroughDelegate")]
        [InlineData("KnownGapSamples::EnumValueOutsideItsMembers")]
        [InlineData("KnownGapSamples::TupleInAnArgumentArray")]
        [InlineData("KnownGapSamples::EnumNumberOfAMember")]
        [InlineData("KnownGapSamples::RecordText")]
        [InlineData("KnownGapSamples::GenericCompareTo")]
        [InlineData("KnownGapSamples::TextOfObject")]
        [InlineData("KnownGapSamples::TextOfObjectField")]
        [InlineData("KnownGapSamples::TextOfObjectElement")]
        [InlineData("KnownGapSamples::NullCultureFromField")]
        [InlineData("KnownGapSamples::NullProviderAtBranch")]
        [InlineData("KnownGapSamples::ProviderThroughDelegate")]
        public void The_known_gaps_in_the_policy_remarks_pass_both_policies(string member)
        {
            string at = Samples + member;

            IReadOnlyList<Violation> reported = Scans.Value.UnityBound.Concat(Scans.Value.MemberBans).Where(violation => IsAt(violation, at)).ToList();

            Assert.True(Scans.Value.MemberReferences.Any(site => site.CallerType + "::" + site.CallerMethod == at), "No member use was read in " + at + ".");
            Assert.True(reported.Count == 0, Report("The scans report " + at + ", a known gap: update the policy's remarks and this list", reported));
        }

        [Fact]
        public void The_member_bans_alone_pass_their_near_misses_outside_the_allowlist()
        {
            const string Sample = Samples + "MemberBansNearMissSample";

            IReadOnlyList<Violation> banned = Scans.Value.MemberBans.Where(violation => IsIn(violation, Sample)).ToList();

            Assert.Contains(Scans.Value.MemberReferences, site => IsIn(site, Sample));
            Assert.True(banned.Count == 0, Report("The member bans alone report their near misses", banned));
        }

        [Fact]
        public void A_field_use_is_named_without_a_parameter_list()
        {
            const string Member = Samples + "MemberBanSamples::NonGenericDefaultComparer";

            Assert.Contains(Scans.Value.MemberBans, violation => IsAt(violation, Member)
                && violation.Reason.StartsWith("System.Collections.Comparer::Default: ", StringComparison.Ordinal));
        }

        [Fact]
        public void The_member_bans_alone_refuse_no_type_and_no_member_of_an_allowed_type()
        {
            IReadOnlyList<Violation> banned = Scans.Value.MemberBans;
            IReadOnlyList<Violation> refusals = banned.Where(violation => IsRefusal(violation, MemberBansPolicy.Default)).ToList();

            // The same assembly holds types and members that the allowlist refuses, and planted bans to report.
            Assert.Contains(Scans.Value.UnityBound, violation => IsRefusal(violation, UnityBoundApiPolicy.Default));
            Assert.NotEmpty(banned);
            Assert.True(refusals.Count == 0, Report("The member bans alone refuse these", refusals));
        }

        [Fact]
        public void The_api_scanner_refuses_a_type_that_only_a_signature_names()
        {
            const string Member = Samples + "PolicyEdgeSamples::KeepTime";
            const string TimeOnly = "System.TimeOnly";
            SampleScans scans = Scans.Value;

            // KeepTime's body names no type, so the walk reports nothing at it; its signature names TimeOnly, which only the
            // type reference table then holds, unlocated.
            Assert.DoesNotContain(scans.UnityBound, violation => IsAt(violation, Member));
            Assert.Contains(scans.DeclaredTypes, use => use.DeclaringType == Samples + "PolicyEdgeSamples" && use.ExternalType == TimeOnly);
            Assert.Contains(scans.UnityBound, violation => violation.Location == "type reference " + TimeOnly
                && violation.Reason == "type " + UnityBoundApiPolicy.Default.RefusedTypeReason);
        }

        [Theory]
        [InlineData("FieldOnlyDeclaration", "System.TimeOnly")]
        [InlineData("InterfaceOnlyDeclaration", "System.ICloneable")]
        public void The_declared_types_show_a_refused_type_that_only_a_field_or_an_interface_names(string type, string external)
        {
            SampleScans scans = Scans.Value;

            Assert.DoesNotContain(scans.UnityBound, violation => IsIn(violation, Samples + type));
            Assert.Contains(scans.DeclaredTypes, use => use.DeclaringType == Samples + type && use.ExternalType == external);
            Assert.Contains(scans.UnityBound, violation => violation.Location == "type reference " + external);
        }

        [Fact]
        public void The_members_the_compiler_writes_for_an_anonymous_type_pass_both_policies()
        {
            const string AnonymousType = "<>f__AnonymousType";
            SampleScans scans = Scans.Value;

            IReadOnlyList<Violation> reported = scans.UnityBound.Concat(scans.MemberBans)
                .Where(violation => violation.Location.StartsWith(AnonymousType, StringComparison.Ordinal))
                .ToList();

            // CleanIntegerOnlySample's query with two from clauses makes one, so there is always such a ToString to read.
            Assert.Contains(scans.MemberReferences, site => site.CallerType.StartsWith(AnonymousType, StringComparison.Ordinal) && site.CallerMethod == "ToString");
            Assert.True(reported.Count == 0, Report("The scans report members the compiler wrote for an anonymous type", reported));
        }

        [Fact]
        public void The_scanners_stay_silent_on_clean_integer_only_code()
        {
            SampleScans scans = Scans.Value;

            IReadOnlyList<Violation> violations = scans.FloatingPoint
                .Concat(scans.UnityBound)
                .Concat(scans.MemberBans)
                .Where(violation => CleanSamples.Any(type => IsIn(violation, type)))
                .ToList();
            IReadOnlyList<string> unread = CleanSamples.Where(type => !scans.MemberReferences.Any(site => IsIn(site, type))).ToList();
            IReadOnlyList<string> declared = scans.DeclaredTypes
                .Where(use => CleanSamples.Any(type => IsIn(use, type)))
                .Select(use => use.ExternalType)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            IReadOnlyList<string> refused = declared.Where(type => !UnityBoundApiPolicy.Default.IsTypeAllowed(type)).ToList();

            // The scans read every method body, as the member reference list does, so each clean sample's code was read.
            Assert.True(unread.Count == 0, "No member use was read in " + string.Join(", ", unread) + ".");
            // A refused type that only a declaration names is reported once, unlocated, so the filter above cannot see it:
            // the clean samples' declared types are checked against the allowlist here.
            Assert.Contains("System.Collections.Generic.IReadOnlyList`1", declared);
            Assert.True(refused.Count == 0, "The allowlist refuses types the clean samples declare: " + string.Join(", ", refused) + ".");
            Assert.True(violations.Count == 0, Report("The scans report the clean samples", violations));
        }

        [Fact]
        public void Tests_run_under_a_culture_that_exposes_culture_bugs()
        {
            CultureInfo culture = CultureInfo.CurrentCulture;
#pragma warning disable CA1862 // The point is to observe the culture-specific lower-casing.
            bool changesDottedI = "I".ToLower(culture) != "i";
#pragma warning restore CA1862
            bool usesUnusualMinus = culture.NumberFormat.NegativeSign != "-";

            Assert.Equal(HostileCulture.CultureName, culture.Name);
            Assert.True(changesDottedI || usesUnusualMinus, culture.Name + " does not expose culture bugs; pick another PUZZLE_TEST_CULTURE.");
        }

        /// <summary>Scans this test assembly, which compiles the planted samples of Guards/Samples.</summary>
        private static SampleScans ScanSamples()
        {
            using var samples = new AssemblyInspection(typeof(DeterminismGuardTests).Assembly.Location);
            return new SampleScans
            {
                FloatingPoint = samples.FloatingPointUses(),
                UnityBound = samples.ApiPolicyViolations(UnityBoundApiPolicy.Default),
                MemberBans = samples.ApiPolicyViolations(MemberBansPolicy.Default),
                MemberReferences = samples.MemberReferences(),
                DeclaredTypes = samples.DeclaredExternalTypes(),
            };
        }

        /// <summary>Asserts that both policies report, at the member, a use of <paramref name="usedMember"/> (Type::Member) for a reason containing the fragment.</summary>
        private static void AssertReportedUnderBothPolicies(string member, string usedMember, string reasonFragment)
        {
            bool Reports(IReadOnlyList<Violation> violations) => violations.Any(violation => IsAt(violation, member)
                && BannedMemberOf(violation) == usedMember && violation.Reason.Contains(reasonFragment, StringComparison.Ordinal));

            Assert.True(Reports(Scans.Value.UnityBound),
                "The Unity-bound policy reports no use of " + usedMember + " ('" + reasonFragment + "') at " + member + ". " + FoundAt(member, Scans.Value.UnityBound));
            Assert.True(Reports(Scans.Value.MemberBans),
                "The member bans alone report no use of " + usedMember + " ('" + reasonFragment + "') at " + member + ". " + FoundAt(member, Scans.Value.MemberBans));
        }

        private static void AssertReported(IReadOnlyList<Violation> violations, string member, string reasonFragment)
        {
            Assert.True(
                violations.Any(violation => IsAt(violation, member) && violation.Reason.Contains(reasonFragment, StringComparison.Ordinal)),
                "No violation containing '" + reasonFragment + "' at " + member + ". " + FoundAt(member, violations));
        }

        /// <summary>Whether the violation lies in the member (Type::Method), at an IL offset or on the member itself.</summary>
        private static bool IsAt(Violation violation, string member) =>
            violation.Location == member || violation.Location.StartsWith(member + "+IL_", StringComparison.Ordinal);

        /// <summary>Whether a location (Type::Member…) lies in the type or in a type nested in it (a closure, a state machine).</summary>
        private static bool IsIn(Violation violation, string type) =>
            violation.Location.StartsWith(type + "::", StringComparison.Ordinal) || violation.Location.StartsWith(type + "+", StringComparison.Ordinal);

        /// <summary>Whether the member use lies in the type or in a type nested in it.</summary>
        private static bool IsIn(MemberReferenceSite site, string type) => IsTypeOrNestedIn(site.CallerType, type);

        /// <summary>Whether the declaration belongs to the type or to a type nested in it.</summary>
        private static bool IsIn(DeclaredTypeUse use, string type) => IsTypeOrNestedIn(use.DeclaringType, type);

        private static bool IsTypeOrNestedIn(string typeName, string type) =>
            typeName == type || typeName.StartsWith(type + "+", StringComparison.Ordinal);

        /// <summary>
        /// The member a member-use violation names ("Display: reason"), as Type::Member with the type's generic arguments
        /// stripped, for example System.Collections.Generic.Dictionary`2::get_Keys; null for a violation of another kind.
        /// </summary>
        private static string? BannedMemberOf(Violation violation)
        {
            string reason = violation.Reason;
            int separator = reason.IndexOf("::", StringComparison.Ordinal);
            int displayEnd = reason.IndexOf(": ", StringComparison.Ordinal);
            if (separator < 0 || displayEnd < separator)
            {
                return null;
            }

            string display = reason[..displayEnd];
            int parameters = display.IndexOf('(', separator);
            string type = display[..separator];
            int generic = type.IndexOf('<', StringComparison.Ordinal);
            return (generic < 0 ? type : type[..generic]) + (parameters < 0 ? display[separator..] : display[separator..parameters]);
        }

        /// <summary>A type the policy refuses ("type ..." or "uses T, which ..."), or a member of an allowed type its allowlist leaves out.</summary>
        private static bool IsRefusal(Violation violation, IPlatformApiPolicy policy) =>
            violation.Reason.EndsWith(policy.RefusedTypeReason, StringComparison.Ordinal)
            || violation.Reason.Contains(" may be used on ", StringComparison.Ordinal);

        private static string FoundAt(string member, IReadOnlyList<Violation> violations) =>
            "Found there:\n" + string.Join("\n", violations.Where(violation => IsAt(violation, member)));

        private static string Report(string heading, IReadOnlyList<Violation> violations) =>
            heading + ":\n" + string.Join("\n", violations.Select(violation => "  " + violation));

        /// <summary>The scans the sample tests share: each scan's results are immutable records, set by name.</summary>
        private sealed record SampleScans
        {
            public required IReadOnlyList<Violation> FloatingPoint { get; init; }

            public required IReadOnlyList<Violation> UnityBound { get; init; }

            public required IReadOnlyList<Violation> MemberBans { get; init; }

            public required IReadOnlyList<MemberReferenceSite> MemberReferences { get; init; }

            public required IReadOnlyList<DeclaredTypeUse> DeclaredTypes { get; init; }
        }
    }
}

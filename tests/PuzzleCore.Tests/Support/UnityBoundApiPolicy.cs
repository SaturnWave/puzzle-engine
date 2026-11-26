using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// The platform APIs that the Unity-bound engine assemblies may use, so that the same actions give the same results
    /// on every device and on .NET, Mono and IL2CPP alike (docs/shared-design.md, §3). It is an ALLOWLIST: a type that is
    /// not listed is rejected, so a new way to read the clock, draw randomness, depend on the device culture, platform or
    /// time zone, or do floating-point math cannot slip in unnoticed. Member bans remove the culture-, platform- and
    /// process-dependent members they name: of allowed types, and of types left out that the member bans alone
    /// (<see cref="MemberBansPolicy"/>) must still refuse, such as a TextWriter's formatting. Rules over every call refuse
    /// the values that make a call depend on the device culture (a null provider, culture or comparer; a StringComparison
    /// other than a constant Ordinal or OrdinalIgnoreCase). The remarks list the uses they do not see. Adding a type or
    /// lifting a ban is a reviewed change with a reason in the comment next to it. Members the compiler synthesizes for
    /// records (ToString, PrintMembers, Equals, GetHashCode...) are exempt, on the rule that no result is ever built from
    /// their output.
    /// </summary>
    /// <remarks>
    /// Known gaps. Each of these passes this policy and <see cref="MemberBansPolicy"/>, and the guard tests plant each one
    /// (Guards/Samples/KnownGapSamples.cs), so a change that closes one shows there:
    /// <list type="bullet">
    /// <item><description>A dictionary or set handed on as an IEnumerable&lt;T&gt; through a helper's return value or an
    /// interface-typed local. What the IL walk knows of a value ends at a call it does not follow and at a local (see
    /// <see cref="AssemblyInspection"/>); closing this needs tracking across methods.</description></item>
    /// <item><description>For the same reason, other values stored in a local first: an integer boxed into an object
    /// local before its ToString, or a null provider kept in a local. A StringComparison kept in a local is refused all
    /// the same, as a value the walk cannot read.</description></item>
    /// <item><description>A receiver the walk does not know, such as an element of an object array, which may be a boxed
    /// integer: an unknown receiver of Object.ToString counts as none.</description></item>
    /// <item><description>A StringComparison or a provider passed through a delegate (Func&lt;string, StringComparison,
    /// bool&gt;.Invoke), whose parameter is a generic placeholder.</description></item>
    /// <item><description>An enum value outside the enum's members: its ToString prints the number, a negative one with
    /// the device culture's minus sign.</description></item>
    /// <item><description>A tuple or a pair that holds an integer, given to a format with a provider where the walk does
    /// not see it: in an argument array (string.Format(provider, format, new object[] { pair }); likewise the array that
    /// FormattableString.Invariant($"{pair}") formats, whose types the allowlist refuses), or as a generic value. Its own
    /// ToString formats the integer by the device culture, which no provider reaches. A generic value passes on purpose,
    /// as a generic number does reach the provider: string.Format(CultureInfo.InvariantCulture, "{0}", value) is a near
    /// miss in CleanNearMissSample.</description></item>
    /// </list>
    /// <see cref="MemberBansPolicy"/> lets more through, as it refuses no type: number text, text order and the platform
    /// newline through the members of types this allowlist leaves out, which the bans do not name. For example
    /// double.ToString(), string.Create(null, $"…") (the interpolation handler keeps the provider in a local),
    /// TextWriter.WriteLine, Console.WriteLine and Environment.NewLine (the platform newline), and enumerating a
    /// ConcurrentDictionary through its own GetEnumerator.
    /// </remarks>
    internal sealed class UnityBoundApiPolicy : IPlatformApiPolicy
    {
        private const string Culture = "depends on the device culture; pass CultureInfo.InvariantCulture or use an ordinal overload";
        private const string RandomizedHash = "hash codes are randomized per process; use the engine's own FNV-1a hash";
        private const string Newline = "Environment.NewLine differs between Windows and other platforms; append '\\n' explicitly";
        private const string UnorderedEnumeration = "enumeration order is an implementation detail that may differ between .NET and Unity; keep a sorted or explicit order";
        private const string UnstableSort = "unstable sort: equal keys may come out in a different order on Mono/IL2CPP and .NET; use OrderBy";
        private const string LinguisticOrder = "orders text by the device culture or by the platform's collation tables (ICU, NLS and Mono differ); use StringComparer.Ordinal";
        private const string NullCulture = "depends on the device culture: a null provider or culture stands for it; pass CultureInfo.InvariantCulture";
        private const string CultureComparison = "depends on the device culture unless the comparison is StringComparison.Ordinal or OrdinalIgnoreCase, written at the call; this call passes ";
        private const string WriterCulture = "depends on the device culture: a writer formats a value by its FormatProvider, the device culture unless the writer was made with another; write text formatted with CultureInfo.InvariantCulture";
        private const string TupleText = "depends on the device culture: a provider does not reach a tuple or a pair, whose own ToString formats the numbers it holds by the device culture; format the items with CultureInfo.InvariantCulture";

        /// <summary>The only StringComparison values that compare text the same on every device: Ordinal and OrdinalIgnoreCase.</summary>
        private static readonly int[] OrdinalComparisons = { (int)StringComparison.Ordinal, (int)StringComparison.OrdinalIgnoreCase };

        /// <summary>Parameter types through which a call takes the culture it formats, parses or compares by.</summary>
        private static readonly string[] CultureParameterTypes = { "System.IFormatProvider", "System.Globalization.CultureInfo" };

        /// <summary>Parameter types through which a call takes the comparer it orders by (an array's non-generic overloads take the untyped one).</summary>
        private static readonly string[] ComparerParameterTypes = { "System.Collections.Generic.IComparer`1", "System.Collections.IComparer" };

        private static readonly string[] IntegerTypes =
        {
            "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
        };

        /// <summary>The platform's handlers for an interpolated string ($"…"), each of which formats the holes it is given.</summary>
        private static readonly string[] InterpolationHandlers =
        {
            "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler", "System.Text.StringBuilder+AppendInterpolatedStringHandler",
            "System.MemoryExtensions+TryWriteInterpolatedStringHandler", "System.Text.Unicode.Utf8+TryWriteInterpolatedStringHandler",
        };

        public static UnityBoundApiPolicy Default { get; } = new UnityBoundApiPolicy(DefaultTypes(), DefaultBans());

        private readonly IReadOnlyDictionary<string, TypeRule> _types;
        private readonly IReadOnlyList<MemberBan> _bans;

        private UnityBoundApiPolicy(IEnumerable<TypeRule> types, IReadOnlyList<MemberBan> bans)
        {
            _types = types.ToDictionary(rule => rule.TypeName, StringComparer.Ordinal);
            _bans = bans;
        }

        public string RefusedTypeReason => "is not on the Unity-bound allowlist";

        /// <summary>Every banned member as Type::Member (the type without generic arguments), each once; the self-tests plant a use of each.</summary>
        public IReadOnlyList<string> BannedMembers => _bans.Select(ban => ban.TypeName + "::" + ban.MemberName).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>The allowed types of which only some members may be used, in ordinal order; the self-tests plant a use of a member each leaves out.</summary>
        public IReadOnlyList<string> TypesWithMemberLimits => _types.Values.OfType<OnlyMembers>()
            .Select(rule => rule.TypeName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        public bool IsTypeAllowed(string typeName) => _types.ContainsKey(typeName);

        public IEnumerable<string> MemberViolations(MemberUse use, CallerContext caller)
        {
            string typeName = use.DeclaringType.DefinitionName;
            if (_types.TryGetValue(typeName, out TypeRule? rule) && rule is OnlyMembers only && !only.Members.Contains(use.MemberName))
            {
                yield return "only " + string.Join(", ", only.Members.OrderBy(name => name, StringComparer.Ordinal)) + " may be used on " + typeName;
            }

            foreach (string reason in BannedMemberUses(use, caller))
            {
                yield return reason;
            }
        }

        /// <summary>
        /// The member bans alone, without the allowlist: the members <see cref="DefaultBans"/> names for culture-dependent
        /// text and text order, the platform newline, randomized hashing, thread identity, enumeration in hash order and
        /// unstable sorts; the values that make any call depend on the device culture (<see cref="CultureDependentArguments"/>);
        /// and the hash-order rules of <see cref="UnorderedSequences"/>. <see cref="MemberBansPolicy"/> applies them to
        /// code that may use every platform type.
        /// </summary>
        public IEnumerable<string> BannedMemberUses(MemberUse use, CallerContext caller)
        {
            string typeName = use.DeclaringType.DefinitionName;
            foreach (MemberBan ban in _bans.Where(ban => ban.TypeName == typeName && ban.MemberName == use.MemberName))
            {
                bool isExempt = caller.IsSynthesizedRecordMember
                    || (ban.ExemptInHashCodeOverrides && caller.MethodName == "GetHashCode")
                    || (ban.ExemptInIteratorPlumbing && caller.IsIteratorPlumbing);
                if (!isExempt && ban.AppliesTo(use))
                {
                    yield return ban.Reason;
                }
            }

            if (caller.IsSynthesizedRecordMember)
            {
                yield break;
            }

            foreach (string reason in CultureDependentArguments(use))
            {
                yield return reason;
            }

            if (UnorderedSequences.EnumeratesInHashOrder(use))
            {
                yield return UnorderedEnumeration;
            }
        }

        /// <summary>
        /// Why the values a call passes make it depend on the device culture, whatever member it calls: null given for a
        /// provider or a culture stands for the device culture, and of the StringComparison values only a constant Ordinal
        /// or OrdinalIgnoreCase compares text the same on every device. The bans on the overloads without a provider or a
        /// comparison cannot see either, as the overload takes one; nor can they see an optional provider left out, which
        /// C# passes as null. A comparison the walk cannot read (a parameter, a local, a field, two constants that meet at
        /// a branch) is refused, as it may be any value.
        /// </summary>
        private static IEnumerable<string> CultureDependentArguments(MemberUse use)
        {
            bool passesNullCulture = PassedValues(use)
                .Any(passed => CultureParameterTypes.Contains(passed.Parameter.DefinitionName) && passed.Argument is { IsNullReference: true });
            if (passesNullCulture)
            {
                yield return NullCulture;
            }

            IEnumerable<int?> comparisons = PassedValues(use)
                .Where(passed => passed.Parameter.DefinitionName == "System.StringComparison")
                .Select(passed => passed.Argument?.Constant);
            foreach (int? comparison in comparisons.Where(comparison => comparison is not int value || !OrdinalComparisons.Contains(value)))
            {
                yield return CultureComparison + DescribeComparison(comparison);
            }
        }

        /// <summary>A StringComparison as a call passes it, for a reason: its member's name, a number outside the enum, or a value the scan cannot read.</summary>
        private static string DescribeComparison(int? comparison)
        {
            if (comparison is not int value)
            {
                return "a value the scan cannot read";
            }

            return Enum.GetName((StringComparison)value) is string name
                ? "StringComparison." + name
                : "the number " + value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Each parameter of a call with what the walk knows of the value passed to it; for a field or token use, nothing.</summary>
        private static IEnumerable<(TypeShape Parameter, TypeShape? Argument)> PassedValues(MemberUse use) =>
            use.Parameters.Zip(use.Arguments, (parameter, argument) => (parameter, argument));

        private static IEnumerable<TypeRule> DefaultTypes()
        {
            // Core language types. Floating-point types are absent on purpose.
            foreach (string name in IntegerTypes.Concat(new[]
            {
                "System.Object", "System.ValueType", "System.Enum", "System.String", "System.Boolean", "System.Char", "System.Void",
                "System.Array", "System.Math", "System.Nullable`1", "System.Index", "System.Range", "System.MemoryExtensions",
                "System.Span`1", "System.ReadOnlySpan`1", "System.IEquatable`1", "System.IComparable", "System.IComparable`1",
                "System.IDisposable", "System.IFormatProvider", "System.StringComparison", "System.StringComparer",
                // An options enum of string.Split (the char overload takes it as an optional argument); no culture or platform input.
                "System.StringSplitOptions",
                "System.ValueTuple`2", "System.ValueTuple`3", "System.ValueTuple`4",
                "System.Func`1", "System.Func`2", "System.Func`3", "System.Func`4", "System.Func`5",
                "System.Action", "System.Action`1", "System.Action`2", "System.Action`3", "System.Predicate`1", "System.Comparison`1",
                "System.RuntimeTypeHandle", "System.RuntimeFieldHandle",
            }))
            {
                yield return new AllMembers(name);
            }

            // Exceptions the engine may throw or catch by name.
            foreach (string name in new[]
            {
                "System.Exception", "System.ArgumentException", "System.ArgumentNullException", "System.ArgumentOutOfRangeException",
                "System.InvalidOperationException", "System.NotSupportedException", "System.FormatException", "System.OverflowException",
                "System.IndexOutOfRangeException", "System.Collections.Generic.KeyNotFoundException",
            })
            {
                yield return new AllMembers(name);
            }

            // Collections. Dictionary/HashSet enumeration is banned below; lookups are fine.
            foreach (string name in new[]
            {
                "System.Collections.IEnumerable", "System.Collections.IEnumerator",
                "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IEnumerator`1",
                "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IList`1",
                "System.Collections.Generic.IReadOnlyCollection`1", "System.Collections.Generic.IReadOnlyList`1",
                "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.IDictionary`2",
                "System.Collections.Generic.List`1", "System.Collections.Generic.List`1+Enumerator",
                "System.Collections.Generic.Dictionary`2", "System.Collections.Generic.HashSet`1",
                "System.Collections.Generic.Queue`1", "System.Collections.Generic.Stack`1",
                "System.Collections.Generic.SortedDictionary`2", "System.Collections.Generic.SortedDictionary`2+Enumerator",
                "System.Collections.Generic.SortedSet`1", "System.Collections.Generic.SortedSet`1+Enumerator",
                "System.Collections.Generic.KeyValuePair`2", "System.Collections.Generic.IComparer`1",
                "System.Collections.Generic.IEqualityComparer`1", "System.Collections.Generic.Comparer`1",
                "System.Collections.Generic.EqualityComparer`1", "System.Collections.ObjectModel.ReadOnlyCollection`1",
                "System.Linq.Enumerable", "System.Linq.IOrderedEnumerable`1", "System.Linq.IGrouping`2", "System.Linq.ILookup`2",
            })
            {
                yield return new AllMembers(name);
            }

            // Text, hashing and binary helpers needed for records, rules fingerprints and traces.
            yield return new AllMembers("System.Text.StringBuilder");
            yield return new AllMembers("System.Buffers.Binary.BinaryPrimitives");
            yield return new OnlyMembers("System.Text.Encoding", "get_UTF8", "GetBytes", "GetString", "GetByteCount");
            yield return new AllMembers("System.Text.UTF8Encoding");
            yield return new OnlyMembers("System.Globalization.CultureInfo", "get_InvariantCulture");
            yield return new AllMembers("System.Globalization.NumberStyles");
            yield return new OnlyMembers("System.Security.Cryptography.SHA256", "Create");
            yield return new OnlyMembers("System.Security.Cryptography.HashAlgorithm", "ComputeHash", "Dispose");

            // Iterator methods (yield return) compile to a state machine whose constructor and
            // GetEnumerator read Environment.CurrentManagedThreadId; a ban below allows only that.
            yield return new OnlyMembers("System.Environment", "get_CurrentManagedThreadId");

            // Reflection is limited to what records and typeof comparisons compile to.
            yield return new OnlyMembers("System.Type", "GetTypeFromHandle", "op_Equality", "op_Inequality");
            // EnsureSufficientExecutionStack is what record ToString/PrintMembers compile to.
            yield return new OnlyMembers("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", "GetSubArray", "EnsureSufficientExecutionStack");

            // Attributes the compiler and SDK attach to every assembly; they carry no behaviour.
            foreach (string name in new[]
            {
                "System.Attribute", "System.AttributeUsageAttribute", "System.AttributeTargets", "System.FlagsAttribute",
                "System.ObsoleteAttribute", "System.ParamArrayAttribute", "System.Reflection.DefaultMemberAttribute",
                "System.Reflection.AssemblyCompanyAttribute", "System.Reflection.AssemblyConfigurationAttribute",
                "System.Reflection.AssemblyFileVersionAttribute", "System.Reflection.AssemblyInformationalVersionAttribute",
                "System.Reflection.AssemblyProductAttribute", "System.Reflection.AssemblyTitleAttribute",
                "System.Reflection.AssemblyVersionAttribute", "System.Reflection.AssemblyMetadataAttribute",
                "System.Runtime.Versioning.TargetFrameworkAttribute",
                "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
                "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
                "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
                "System.Runtime.CompilerServices.IsReadOnlyAttribute", "System.Runtime.CompilerServices.IsByRefLikeAttribute",
                // `in` parameters on interface and abstract members carry a modreq(InAttribute); a marker, no behaviour.
                "System.Runtime.InteropServices.InAttribute",
                "System.Runtime.CompilerServices.IsExternalInit", "System.Runtime.CompilerServices.ExtensionAttribute",
                "System.Runtime.CompilerServices.TupleElementNamesAttribute", "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
                "System.Runtime.CompilerServices.NullableAttribute", "System.Runtime.CompilerServices.NullableContextAttribute",
                "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
                "System.Diagnostics.DebuggableAttribute", "System.Diagnostics.DebuggableAttribute+DebuggingModes",
                "System.Diagnostics.DebuggerBrowsableAttribute", "System.Diagnostics.DebuggerBrowsableState",
                "System.Diagnostics.DebuggerHiddenAttribute", "System.Diagnostics.DebuggerStepThroughAttribute",
                "System.Diagnostics.DebuggerDisplayAttribute", "System.Diagnostics.DebuggerNonUserCodeAttribute",
                "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute", "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute",
                "System.Diagnostics.CodeAnalysis.NotNullAttribute", "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute",
                "System.Diagnostics.CodeAnalysis.DisallowNullAttribute", "System.Diagnostics.CodeAnalysis.AllowNullAttribute",
                "System.Diagnostics.CodeAnalysis.MaybeNullAttribute", "System.Diagnostics.CodeAnalysis.SuppressMessageAttribute",
            })
            {
                yield return new AllMembers(name);
            }
        }

        private static IReadOnlyList<MemberBan> DefaultBans()
        {
            var bans = new List<MemberBan>();

            foreach (string type in IntegerTypes)
            {
                bans.Add(new MemberBan(type, "ToString", WithParameters(parameters => !parameters.Contains("System.IFormatProvider")), Culture));
                bans.Add(new MemberBan(type, "Parse", WithParameters(parameters => !parameters.Contains("System.IFormatProvider")), Culture));
                bans.Add(new MemberBan(type, "TryParse", WithParameters(parameters => !parameters.Contains("System.IFormatProvider")), Culture));
            }

            bans.Add(new MemberBan("System.String", "ToUpper", WithParameters(parameters => parameters.Count == 0), Culture));
            bans.Add(new MemberBan("System.String", "ToLower", WithParameters(parameters => parameters.Count == 0), Culture));
            bans.Add(new MemberBan("System.String", "Compare", WithParameters(parameters => !parameters.Contains("System.StringComparison")), Culture));
            bans.Add(new MemberBan("System.String", "CompareTo", _ => true, Culture));
            foreach (string search in new[] { "StartsWith", "EndsWith", "IndexOf", "LastIndexOf" })
            {
                bans.Add(new MemberBan("System.String", search,
                    WithParameters(parameters => parameters.Count > 0 && parameters[0] == "System.String" && !parameters.Contains("System.StringComparison")), Culture));
            }

            bans.Add(new MemberBan("System.String", "Format", WithParameters(parameters => parameters.Count == 0 || parameters[0] != "System.IFormatProvider"), Culture));
            // Concat<T>, Join<T> and AppendJoin<T> call T.ToString(); with T = string that is the text itself.
            bans.Add(new MemberBan("System.String", "Concat", JoinsValues, Culture));
            bans.Add(new MemberBan("System.String", "Join", JoinsValues, Culture));
            bans.Add(new MemberBan("System.Char", "ToUpper", WithParameters(parameters => parameters.Count == 1), Culture));
            bans.Add(new MemberBan("System.Char", "ToLower", WithParameters(parameters => parameters.Count == 1), Culture));

            bans.Add(new MemberBan("System.Text.StringBuilder", "Append", WithParameters(FormatsAValue), Culture));
            // Insert's first parameter is the index it inserts at.
            bans.Add(new MemberBan("System.Text.StringBuilder", "Insert", WithParameters(parameters => FormatsAValue(parameters.Skip(1).ToList())), Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder", "AppendFormat",
                WithParameters(parameters => parameters.Count == 0 || parameters[0] != "System.IFormatProvider"), Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder", "AppendJoin", JoinsValues, Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder", "AppendLine", _ => true, Newline));

            // A writer formats a number or an object by its FormatProvider, which is the device culture unless the writer was
            // made with another, and the walk cannot see which; Console writes through Console.Out, which formats so.
            foreach (string writer in new[] { "System.IO.TextWriter", "System.Console" })
            {
                bans.Add(new MemberBan(writer, "Write", WithParameters(FormatsAValue), WriterCulture));
                bans.Add(new MemberBan(writer, "WriteLine", WithParameters(FormatsAValue), WriterCulture));
            }

            // Convert.ToString(value, toBase) writes the digits in a number base, which no culture changes.
            bans.Add(new MemberBan("System.Convert", "ToString",
                WithParameters(parameters => !parameters.Contains("System.IFormatProvider") && FormatsAValue(parameters)), Culture));

            // C# 10 and later compile $"…{n}" to these handlers; without a provider they format with the device culture, and
            // CA1305 does not see it (string.Create(CultureInfo.InvariantCulture, $"…") passes one). C# 9 compiles $"…" to
            // string.Format, banned above.
            foreach (string handler in InterpolationHandlers)
            {
                bans.Add(new MemberBan(handler, ".ctor", WithParameters(parameters => !parameters.Contains("System.IFormatProvider")), Culture));
            }

            // A provider reaches only a value that formats itself with one (an IFormattable). A tuple or a pair is none: it
            // formats what it holds by its own ToString, so a number inside prints by the device culture whatever the
            // provider. The walk sees such a value where it is passed alone; the remarks list where it does not.
            bans.Add(new MemberBan("System.String", "Format", PassesATupleOfNumbers, TupleText));
            bans.Add(new MemberBan("System.Text.StringBuilder", "AppendFormat", PassesATupleOfNumbers, TupleText));
            foreach (string handler in InterpolationHandlers)
            {
                bans.Add(new MemberBan(handler, "AppendFormatted", PassesATupleOfNumbers, TupleText));
            }

            bans.Add(new MemberBan("System.Globalization.CultureInfo", "get_CurrentCulture", _ => true, Culture));
            bans.Add(new MemberBan("System.Globalization.CultureInfo", "get_CurrentUICulture", _ => true, Culture));

            // ToString called through System.Object names no integer type: a boxed integer, a nullable, pair or tuple that
            // holds one, and a generic T all reach it, so the ban judges the receiver the walk knows (box and the
            // constrained. prefix keep the value's own type). The ToString() of a FormattableString or an IFormattable
            // formats by the device culture as well. System.ValueType.ToString needs no ban: C# names it only for
            // base.ToString() in a struct, whose receiver is that struct, and it prints the struct's type name.
            bans.Add(new MemberBan("System.Object", "ToString", use => ToStringFormatsByCulture(use.Instance), Culture));

            // FormattableStringFactory.Create only keeps a format and its values. What formats them is the FormattableString's
            // ToString() (above), its ToString(provider) given null (the rule over every call) or CurrentCulture;
            // FormattableString.Invariant formats them with the invariant culture.
            bans.Add(new MemberBan("System.FormattableString", "CurrentCulture", _ => true, Culture));

            // Text order: only an ordinal comparison orders strings the same on every device and runtime. A comparer-less
            // sort, sorted collection or Comparer<T>.Default over text compares by the device culture.
            foreach (string comparer in new[]
            {
                "get_CurrentCulture", "get_CurrentCultureIgnoreCase", "get_InvariantCulture", "get_InvariantCultureIgnoreCase", "Create", "FromComparison",
            })
            {
                bans.Add(new MemberBan("System.StringComparer", comparer, _ => true, LinguisticOrder));
            }

            // Default and DefaultInvariant of System.Collections.Comparer are static fields (ldsfld), not properties.
            bans.Add(new MemberBan("System.Collections.Comparer", "Default", _ => true, LinguisticOrder));
            bans.Add(new MemberBan("System.Collections.Comparer", "DefaultInvariant", _ => true, LinguisticOrder));
            bans.Add(new MemberBan("System.Collections.Generic.Comparer`1", "get_Default", use => OrdersText(TypeArgument(use.DeclaringType, 0)), LinguisticOrder));
            foreach (string sorted in new[] { "System.Collections.Generic.SortedDictionary`2", "System.Collections.Generic.SortedSet`1", "System.Collections.Generic.SortedList`2" })
            {
                bans.Add(new MemberBan(sorted, ".ctor", use => !TakesComparer(use) && OrdersText(TypeArgument(use.DeclaringType, 0)), LinguisticOrder));
            }

            foreach (string ordering in new[] { "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending", "Min", "Max", "MinBy", "MaxBy" })
            {
                bans.Add(new MemberBan("System.Linq.Enumerable", ordering, use => !TakesComparer(use) && OrdersText(SortKey(use)), LinguisticOrder));
            }

            // BinarySearch without a comparer searches by the default comparer, which compares text by the device culture:
            // on lists, arrays, spans and the immutable collections, as the sorts below. An array's non-generic overloads
            // compare objects, which compares text so. A span's BinarySearch searches with its last type argument: either
            // the comparable it is given (TComparable, or T for the overload that takes an IComparable<T>), and a string
            // compares by the device culture, or the comparer, which is never text.
            foreach (string list in new[] { "System.Collections.Generic.List`1", "System.Collections.Immutable.ImmutableList`1", "System.Collections.Immutable.ImmutableList`1+Builder" })
            {
                bans.Add(new MemberBan(list, "BinarySearch", use => !TakesComparer(use) && OrdersText(TypeArgument(use.DeclaringType, 0)), LinguisticOrder));
            }

            foreach (string array in new[] { "System.Array", "System.Collections.Immutable.ImmutableArray" })
            {
                bans.Add(new MemberBan(array, "BinarySearch",
                    use => !TakesComparer(use) && (use.MethodTypeArguments.Count == 0 || OrdersText(use.MethodTypeArguments[0])), LinguisticOrder));
            }

            bans.Add(new MemberBan("System.MemoryExtensions", "BinarySearch",
                use => use.MethodTypeArguments.Count > 0 && OrdersText(use.MethodTypeArguments[^1]), LinguisticOrder));

            // CompareTo through IComparable<T> or IComparable compares as the value's own CompareTo does, a string's by the
            // device culture. The untyped interface hides what it compares, so only a receiver the walk knows is no text
            // (a boxed int) passes it.
            bans.Add(new MemberBan("System.IComparable`1", "CompareTo", use => OrdersText(TypeArgument(use.DeclaringType, 0)), LinguisticOrder));
            bans.Add(new MemberBan("System.IComparable", "CompareTo", use => MayCompareText(use.Instance), LinguisticOrder));

            foreach (string type in new[]
            {
                "System.Object", "System.ValueType", "System.String", "System.StringComparer",
                "System.Collections.Generic.EqualityComparer`1", "System.Collections.Generic.IEqualityComparer`1",
                "System.Runtime.CompilerServices.RuntimeHelpers",
            })
            {
                bans.Add(new MemberBan(type, "GetHashCode", _ => true, RandomizedHash, ExemptInHashCodeOverrides: true));
            }

            bans.Add(new MemberBan("System.Environment", "get_CurrentManagedThreadId", _ => true,
                "thread identity must not influence results", ExemptInIteratorPlumbing: true));

            bans.Add(new MemberBan("System.Collections.Generic.Dictionary`2", "GetEnumerator", _ => true, UnorderedEnumeration));
            bans.Add(new MemberBan("System.Collections.Generic.Dictionary`2", "get_Keys", _ => true, UnorderedEnumeration));
            bans.Add(new MemberBan("System.Collections.Generic.Dictionary`2", "get_Values", _ => true, UnorderedEnumeration));
            bans.Add(new MemberBan("System.Collections.Generic.HashSet`1", "GetEnumerator", _ => true, UnorderedEnumeration));
            // CopyTo copies a set's or a dictionary view's elements in the order enumerating them gives.
            bans.Add(new MemberBan("System.Collections.Generic.HashSet`1", "CopyTo", _ => true, UnorderedEnumeration));
            foreach (string view in new[] { "System.Collections.Generic.Dictionary`2+KeyCollection", "System.Collections.Generic.Dictionary`2+ValueCollection" })
            {
                bans.Add(new MemberBan(view, "GetEnumerator", _ => true, UnorderedEnumeration));
                bans.Add(new MemberBan(view, "CopyTo", _ => true, UnorderedEnumeration));
            }

            // Through an interface the dictionary behind it is unknown, and most are hash tables. Enumerating one as a
            // sequence (foreach, LINQ) or copying it out (ICollection<T>.CopyTo) is UnorderedSequences' part.
            foreach (string view in new[] { "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.IDictionary`2" })
            {
                bans.Add(new MemberBan(view, "get_Keys", _ => true, UnorderedEnumeration));
                bans.Add(new MemberBan(view, "get_Values", _ => true, UnorderedEnumeration));
            }

            // List<T>.Sort, Array.Sort and the span and immutable sorts are introsorts: not stable, and the order of equal keys
            // depends on the runtime's implementation. Enumerable.OrderBy is a stable sort, so it orders equal keys the same everywhere.
            bans.Add(new MemberBan("System.Collections.Generic.List`1", "Sort", _ => true, UnstableSort));
            bans.Add(new MemberBan("System.Array", "Sort", _ => true, UnstableSort));
            bans.Add(new MemberBan("System.MemoryExtensions", "Sort", _ => true, UnstableSort));
            foreach (string immutable in new[]
            {
                "System.Collections.Immutable.ImmutableArray`1", "System.Collections.Immutable.ImmutableArray`1+Builder",
                "System.Collections.Immutable.ImmutableList`1", "System.Collections.Immutable.ImmutableList`1+Builder",
            })
            {
                bans.Add(new MemberBan(immutable, "Sort", _ => true, UnstableSort));
            }

            return bans;
        }

        private static Func<MemberUse, bool> WithParameters(Func<IReadOnlyList<string>, bool> test) =>
            use => test(use.Parameters.Select(parameter => parameter.Display).ToList());

        private static bool IsObjectOrGeneric(string parameter) =>
            parameter == "System.Object" || parameter == "System.Object[]" || parameter.StartsWith('!')
            || parameter.StartsWith("System.Collections.Generic.IEnumerable`1<!", StringComparison.Ordinal)
            || parameter.StartsWith("System.ReadOnlySpan`1<System.Object>", StringComparison.Ordinal);

        /// <summary>
        /// A generic Concat, Join or AppendJoin over strings only, such as builder.AppendJoin(", ", names) with a
        /// List&lt;string&gt;, for which StringBuilder has no overload of its own.
        /// </summary>
        private static bool JoinsOnlyText(MemberUse use) =>
            use.MethodTypeArguments.Count > 0 && use.MethodTypeArguments.All(argument => argument.DefinitionName == "System.String");

        /// <summary>
        /// Whether a Concat, Join or AppendJoin is given values to join (an object, an object array or span, or a sequence
        /// of T), each of which it formats by the value's own ToString; a generic one over strings only joins text. No
        /// separator is an object.
        /// </summary>
        private static bool JoinsValues(MemberUse use) =>
            use.Parameters.Any(parameter => IsObjectOrGeneric(parameter.Display)) && !JoinsOnlyText(use);

        /// <summary>
        /// Whether an overload formats a value it is given instead of copying text: a lone integer (Append(long),
        /// Write(int)), or an object or a generic value (Append(object), Write(string, object), Write(string, params
        /// object[])). Text, a char and a bool print the same on every device, and a char array with its index and count
        /// is text.
        /// </summary>
        private static bool FormatsAValue(IReadOnlyList<string> parameters) =>
            (parameters.Count == 1 && IntegerTypes.Contains(parameters[0])) || parameters.Any(IsObjectOrGeneric);

        /// <summary>
        /// Whether a call is given a tuple or a pair that holds an integer, as far as the walk knows the values it passes.
        /// Neither is IFormattable, so the call formats it by its own ToString, which formats what it holds by the device
        /// culture.
        /// </summary>
        private static bool PassesATupleOfNumbers(MemberUse use) =>
            use.Arguments.Any(argument => argument is not null && IsTupleLike(argument.DefinitionName)
                && argument.TypeArguments.Any(item => FormatsAnInteger(item)));

        /// <summary>
        /// Whether the call passes a comparer: a comparer parameter given anything but null, which stands for the default
        /// comparer. A value the walk does not know counts as a comparer.
        /// </summary>
        private static bool TakesComparer(MemberUse use) =>
            use.Parameters.Select((parameter, index) => (Parameter: parameter, Argument: index < use.Arguments.Count ? use.Arguments[index] : null))
                .Any(passed => ComparerParameterTypes.Contains(passed.Parameter.DefinitionName) && passed.Argument is not { IsNullReference: true });

        private static TypeShape? TypeArgument(TypeShape type, int index) => index < type.TypeArguments.Length ? type.TypeArguments[index] : null;

        /// <summary>
        /// The key an ordering method compares: what its selector (the Func`2 parameter) returns, or else the element type
        /// (Order&lt;T&gt;, Min&lt;T&gt;); null for a non-generic overload such as Min(IEnumerable&lt;int&gt;).
        /// </summary>
        private static TypeShape? SortKey(MemberUse use)
        {
            TypeShape? selector = use.Parameters.FirstOrDefault(parameter => parameter.DefinitionName == "System.Func`2");
            if (selector is not null)
            {
                return TypeArgument(selector, 1) is TypeShape result ? use.Resolve(result) : null;
            }

            return use.MethodTypeArguments.Count > 0 ? use.MethodTypeArguments[0] : null;
        }

        /// <summary>
        /// Whether the default comparer of a key compares text by culture: string, object (which compares strings so), and
        /// tuples or pairs that hold one. A generic parameter the scan cannot see through counts as no text.
        /// </summary>
        private static bool OrdersText(TypeShape? key) =>
            key is not null
            && (key.DefinitionName is "System.String" or "System.Object"
                || (IsTupleLike(key.DefinitionName) && key.TypeArguments.Any(argument => OrdersText(argument))));

        /// <summary>
        /// Whether a receiver of IComparable.CompareTo may compare text: text as <see cref="OrdersText"/> has it, the
        /// interface itself (which a string may be), or a value the walk does not know. A generic parameter counts as no
        /// text, as everywhere else.
        /// </summary>
        private static bool MayCompareText(TypeShape? receiver) =>
            receiver is null || OrdersText(receiver) || receiver.DefinitionName == "System.IComparable";

        private static bool IsTupleLike(string definitionName) =>
            definitionName.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || definitionName.StartsWith("System.Tuple`", StringComparison.Ordinal)
            || definitionName == "System.Collections.Generic.KeyValuePair`2";

        /// <summary>
        /// Whether a value's ToString formats an integer by the device culture: an integer, a generic parameter that may be
        /// one, or a nullable, pair or tuple that holds one, whose ToString formats what it holds. An unknown value (null)
        /// counts as none.
        /// </summary>
        private static bool FormatsAnInteger(TypeShape? value) =>
            value is not null
            && (IntegerTypes.Contains(value.DefinitionName) || value.IsGenericParameter
                || ((value.DefinitionName == "System.Nullable`1" || IsTupleLike(value.DefinitionName)) && value.TypeArguments.Any(argument => FormatsAnInteger(argument))));

        /// <summary>
        /// Whether a receiver's ToString() formats by the device culture: a value that formats an integer
        /// (<see cref="FormatsAnInteger"/>), or a FormattableString or an IFormattable, whose ToString() formats with the
        /// current culture. An unknown receiver counts as none.
        /// </summary>
        private static bool ToStringFormatsByCulture(TypeShape? receiver) =>
            FormatsAnInteger(receiver) || receiver?.DefinitionName is "System.FormattableString" or "System.IFormattable";

        private abstract record TypeRule(string TypeName);

        private sealed record AllMembers(string TypeName) : TypeRule(TypeName);

        private sealed record OnlyMembers : TypeRule
        {
            public OnlyMembers(string typeName, params string[] members)
                : base(typeName) => Members = new HashSet<string>(members, StringComparer.Ordinal);

            public IReadOnlySet<string> Members { get; }
        }

        private sealed record MemberBan(
            string TypeName,
            string MemberName,
            Func<MemberUse, bool> AppliesTo,
            string Reason,
            bool ExemptInHashCodeOverrides = false,
            bool ExemptInIteratorPlumbing = false);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// The platform APIs that the Unity-bound engine assemblies may use (CLAUDE.md,
    /// "Belirleyicilik kuralları"). It is an ALLOWLIST: a type that is not listed is
    /// rejected, so a new way to read the clock, draw randomness, depend on the device
    /// culture, platform or time zone, or do floating-point math cannot slip in unnoticed.
    /// Within allowed types, member bans remove the culture-, platform- and
    /// process-dependent members. Adding a type or lifting a ban is a reviewed change
    /// with a reason in the comment next to it.
    /// Members the compiler synthesizes for records (ToString, PrintMembers, Equals,
    /// GetHashCode...) are exempt: their output is never used for results (CLAUDE.md).
    /// </summary>
    internal sealed class UnityBoundApiPolicy : IPlatformApiPolicy
    {
        private const string Culture = "depends on the device culture; pass CultureInfo.InvariantCulture or use an ordinal overload";
        private const string RandomizedHash = "hash codes are randomized per process; use the engine's own FNV-1a hash";
        private const string Newline = "Environment.NewLine differs between Windows and other platforms; append '\\n' explicitly";
        private const string UnorderedEnumeration = "enumeration order is an implementation detail that may differ between .NET and Unity; keep a sorted or explicit order";
        private const string UnstableSort = "unstable sort: equal keys may come out in a different order on Mono/IL2CPP and .NET; use OrderBy";
        private const string LinguisticOrder = "orders text by the device culture or by the platform's collation tables (ICU, NLS and Mono differ); use StringComparer.Ordinal";

        private static readonly string[] IntegerTypes =
        {
            "System.Byte", "System.SByte", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
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
        /// The member bans alone, without the allowlist: culture-dependent text, the platform newline, randomized hashing,
        /// unordered enumeration and unstable sorts. The generator content policy applies them too (T30).
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

            if (!caller.IsSynthesizedRecordMember && UnorderedSequences.EnumeratesInHashOrder(use))
            {
                yield return UnorderedEnumeration;
            }
        }

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
            // Concat<T> and Join<T> call T.ToString(); with T = string that is the text itself.
            bans.Add(new MemberBan("System.String", "Concat",
                use => use.Parameters.Any(parameter => IsObjectOrGeneric(parameter.Display)) && !JoinsOnlyText(use), Culture));
            bans.Add(new MemberBan("System.String", "Join",
                use => use.Parameters.Skip(1).Any(parameter => IsObjectOrGeneric(parameter.Display)) && !JoinsOnlyText(use), Culture));
            bans.Add(new MemberBan("System.Char", "ToUpper", WithParameters(parameters => parameters.Count == 1), Culture));
            bans.Add(new MemberBan("System.Char", "ToLower", WithParameters(parameters => parameters.Count == 1), Culture));

            bans.Add(new MemberBan("System.Text.StringBuilder", "Append",
                WithParameters(parameters => parameters.Count == 1 && (IntegerTypes.Contains(parameters[0]) || IsObjectOrGeneric(parameters[0]))), Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder", "Insert",
                WithParameters(parameters => parameters.Count == 2 && (IntegerTypes.Contains(parameters[1]) || IsObjectOrGeneric(parameters[1]))), Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder", "AppendFormat",
                WithParameters(parameters => parameters.Count == 0 || parameters[0] != "System.IFormatProvider"), Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder", "AppendLine", _ => true, Newline));

            // C# 10 and later compile $"…{n}" to these handlers; without a provider they format with the device culture, and
            // CA1305 does not see it (string.Create(CultureInfo.InvariantCulture, $"…") passes one). C# 9 compiles $"…" to
            // string.Format, banned above.
            bans.Add(new MemberBan("System.Runtime.CompilerServices.DefaultInterpolatedStringHandler", ".ctor",
                WithParameters(parameters => !parameters.Contains("System.IFormatProvider")), Culture));
            bans.Add(new MemberBan("System.Text.StringBuilder+AppendInterpolatedStringHandler", ".ctor",
                WithParameters(parameters => !parameters.Contains("System.IFormatProvider")), Culture));
            bans.Add(new MemberBan("System.Globalization.CultureInfo", "get_CurrentCulture", _ => true, Culture));
            bans.Add(new MemberBan("System.Globalization.CultureInfo", "get_CurrentUICulture", _ => true, Culture));

            // Text order: only an ordinal comparison orders strings the same on every device and runtime. A comparer-less
            // sort, sorted collection or Comparer<T>.Default over text compares by the device culture.
            foreach (string comparer in new[]
            {
                "get_CurrentCulture", "get_CurrentCultureIgnoreCase", "get_InvariantCulture", "get_InvariantCultureIgnoreCase", "Create", "FromComparison",
            })
            {
                bans.Add(new MemberBan("System.StringComparer", comparer, _ => true, LinguisticOrder));
            }

            bans.Add(new MemberBan("System.Collections.Comparer", "get_Default", _ => true, LinguisticOrder));
            bans.Add(new MemberBan("System.Collections.Comparer", "get_DefaultInvariant", _ => true, LinguisticOrder));
            bans.Add(new MemberBan("System.Collections.Generic.Comparer`1", "get_Default", use => OrdersText(TypeArgument(use.DeclaringType, 0)), LinguisticOrder));
            foreach (string sorted in new[] { "System.Collections.Generic.SortedDictionary`2", "System.Collections.Generic.SortedSet`1", "System.Collections.Generic.SortedList`2" })
            {
                bans.Add(new MemberBan(sorted, ".ctor", use => !TakesComparer(use) && OrdersText(TypeArgument(use.DeclaringType, 0)), LinguisticOrder));
            }

            foreach (string ordering in new[] { "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending", "Order", "OrderDescending", "Min", "Max", "MinBy", "MaxBy" })
            {
                bans.Add(new MemberBan("System.Linq.Enumerable", ordering, use => !TakesComparer(use) && OrdersText(SortKey(use)), LinguisticOrder));
            }

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
            // Through an interface the dictionary behind it is unknown, and most are hash tables. Enumerating one as a
            // sequence (foreach, LINQ) is UnorderedSequences' part.
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

        /// <summary>A generic Concat or Join over strings only, such as string.Join&lt;string&gt;(", ", names).</summary>
        private static bool JoinsOnlyText(MemberUse use) =>
            use.MethodTypeArguments.Count > 0 && use.MethodTypeArguments.All(argument => argument.DefinitionName == "System.String");

        private static bool TakesComparer(MemberUse use) =>
            use.Parameters.Any(parameter => parameter.DefinitionName == "System.Collections.Generic.IComparer`1");

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

        private static bool IsTupleLike(string definitionName) =>
            definitionName.StartsWith("System.ValueTuple`", StringComparison.Ordinal) || definitionName.StartsWith("System.Tuple`", StringComparison.Ordinal)
            || definitionName == "System.Collections.Generic.KeyValuePair`2";

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

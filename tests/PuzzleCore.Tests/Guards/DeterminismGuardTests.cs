using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PuzzleCore.Tests.Support;
using Xunit;

namespace PuzzleCore.Tests.Guards
{
    /// <summary>
    /// The same seed and the same actions must give the same game on every device.
    /// These tests read the compiled Unity-bound assemblies and fail on anything that can
    /// make results differ between machines: floating point, platform APIs outside the
    /// allowlist (randomness, the clock, time zones, the environment, threads, reflection,
    /// native calls), culture-dependent text, randomized hashing, unordered enumeration and
    /// unstable sorts.
    /// The self-tests prove that the scanners see planted violations and stay silent on
    /// clean integer-only code. Ported from the Block engine (shared engine §3).
    /// </summary>
    public sealed class DeterminismGuardTests
    {
        private const string ViolationsSample = "PuzzleCore.Tests.Guards.Samples.DeterminismViolationsSample";
        private const string CleanSample = "PuzzleCore.Tests.Guards.Samples.CleanIntegerOnlySample";
        private const string CleanRecord = "PuzzleCore.Tests.Guards.Samples.CleanCell";

        public static TheoryData<string> UnityBoundAssemblies => new(ProjectRules.UnityBoundProjects);

        [Theory]
        [MemberData(nameof(UnityBoundAssemblies))]
        public void Unity_bound_assemblies_contain_no_floating_point_types_or_instructions(string assemblyName)
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath(assemblyName));

            IReadOnlyList<Violation> violations = inspection.FloatingPointUses();

            Assert.True(violations.Count == 0, Report(assemblyName, violations));
        }

        [Theory]
        [MemberData(nameof(UnityBoundAssemblies))]
        public void Unity_bound_assemblies_use_only_allowlisted_platform_apis(string assemblyName)
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath(assemblyName));

            IReadOnlyList<Violation> violations = inspection.ApiPolicyViolations(UnityBoundApiPolicy.Default);

            Assert.True(violations.Count == 0, Report(assemblyName, violations));
        }

        [Theory]
        [InlineData("DoubleField", "floating-point field")]
        [InlineData("HalveWithFloatLocal", "floating-point local variable")]
        [InlineData("HalveWithFloatLocal", "floating-point instruction conv.r4")]
        [InlineData("Price", "floating-point parameter or return type")]
        [InlineData("FloorOfDoubleLiteral", "floating-point instruction ldc.r8")]
        [InlineData("FloorOfDoubleLiteral", "uses floating point via System.Math::Floor(System.Double)")]
        [InlineData("NormalizedVector", "uses floating point via System.Numerics.Vector2::Normalize(System.Numerics.Vector2)")]
        public void The_floating_point_scanner_reports_each_planted_use_at_its_member(string member, string reason)
        {
            using var inspection = new AssemblyInspection(typeof(DeterminismGuardTests).Assembly.Location);

            IReadOnlyList<Violation> violations = inspection.FloatingPointUses();

            Assert.Contains(violations, violation => violation.Location == ViolationsSample + "::" + member && violation.Reason == reason);
        }

        [Theory]
        [InlineData("NextFromSystemRandom", "uses System.Random")]
        [InlineData("ReadTheClock", "uses System.DateTime")]
        [InlineData("LocalTimeConversion", "uses System.DateTime")]
        [InlineData("NormalizedVector", "uses System.Numerics.Vector2")]
        [InlineData("PlainStringHash", "hash codes are randomized per process")]
        [InlineData("OrdinalComparerHash", "hash codes are randomized per process")]
        [InlineData("TupleHash", "hash codes are randomized per process")]
        [InlineData("IntegerToTextByConcatenation", "depends on the device culture")]
        [InlineData("FormatWithoutCulture", "depends on the device culture")]
        [InlineData("AppendInteger", "depends on the device culture")]
        [InlineData("AppendPlatformNewline", "Environment.NewLine differs")]
        [InlineData("UpperWithDeviceCulture", "depends on the device culture")]
        [InlineData("CompareWithDeviceCulture", "depends on the device culture")]
        [InlineData("ChangeTypeThroughDouble", "uses System.Convert")]
        [InlineData("TypeByName", "may be used on System.Type")]
        [InlineData("ProcessorCount", "may be used on System.Environment")]
        [InlineData("ThreadIdentity", "thread identity must not influence results")]
        [InlineData("NativeSizedSum", "native-sized integer")]
        [InlineData("DictionaryOrder", "enumeration order is an implementation detail")]
        [InlineData("NativeTime", "native call (P/Invoke) is not allowed")]
        [InlineData("SortWithListSort", "unstable sort")]
        [InlineData("SortWithArraySort", "unstable sort")]
        [InlineData("SortWithSpanSort", "unstable sort")]
        [InlineData("OrderTextWithoutComparer", "orders text by the device culture")]
        [InlineData("OrderTextByCurrentCulture", "orders text by the device culture")]
        [InlineData("OrderByTupleHoldingText", "orders text by the device culture")]
        [InlineData("SortedDictionaryWithoutComparer", "orders text by the device culture")]
        [InlineData("SortedSetWithoutComparer", "orders text by the device culture")]
        [InlineData("CompareWithDefaultComparer", "orders text by the device culture")]
        [InlineData("ReadOnlyDictionaryOrder", "enumeration order is an implementation detail")]
        [InlineData("ReadOnlyDictionaryKeys", "enumeration order is an implementation detail")]
        [InlineData("DictionaryThroughLinq", "enumeration order is an implementation detail")]
        [InlineData("HashSetToList", "enumeration order is an implementation detail")]
        [InlineData("HashSetJoined", "enumeration order is an implementation detail")]
        [InlineData("HashSetThroughLambda", "enumeration order is an implementation detail")]
        [InlineData("PairsOfUnknownOrder", "enumeration order is an implementation detail")]
        [InlineData("InterpolatedWithDeviceCulture", "depends on the device culture")]
        public void The_api_scanner_reports_each_planted_violation_at_its_method(string method, string reasonFragment)
        {
            using var inspection = new AssemblyInspection(typeof(DeterminismGuardTests).Assembly.Location);

            IReadOnlyList<Violation> violations = inspection.ApiPolicyViolations(UnityBoundApiPolicy.Default);

            Assert.True(
                violations.Any(violation => IsAt(violation, ViolationsSample, method)
                    && violation.Reason.Contains(reasonFragment, StringComparison.Ordinal)),
                "No violation containing '" + reasonFragment + "' at " + method + ". Found there:\n"
                + string.Join("\n", violations.Where(violation => IsAt(violation, ViolationsSample, method))));
        }

        [Fact]
        public void The_scanners_stay_silent_on_clean_integer_only_code()
        {
            using var inspection = new AssemblyInspection(typeof(DeterminismGuardTests).Assembly.Location);

            IReadOnlyList<Violation> violations = inspection.FloatingPointUses()
                .Concat(inspection.ApiPolicyViolations(UnityBoundApiPolicy.Default))
                .Where(violation => IsIn(violation, CleanSample) || IsIn(violation, CleanRecord))
                .ToList();

            Assert.True(violations.Count == 0, Report("clean sample", violations));
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

        private static bool IsAt(Violation violation, string type, string member) =>
            violation.Location == type + "::" + member || violation.Location.StartsWith(type + "::" + member + "+IL_", StringComparison.Ordinal);

        private static bool IsIn(Violation violation, string type) =>
            violation.Location.StartsWith(type + "::", StringComparison.Ordinal)
            || violation.Location.StartsWith(type + "+", StringComparison.Ordinal);

        private static string Report(string subject, IReadOnlyList<Violation> violations) =>
            subject + " breaks the determinism rules (see CLAUDE.md):\n"
            + string.Join("\n", violations.Select(violation => "  " + violation));
    }
}

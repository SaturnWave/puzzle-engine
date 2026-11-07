using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PuzzleCore.Tests.Support;
using Xunit;

namespace PuzzleCore.Tests.Guards
{
    /// <summary>
    /// Rules of the code standard that the compiler does not fully enforce, checked on the
    /// compiled output of every source project: no generic or empty catch blocks (CA1031
    /// misses filtered and empty catches), and no System.Random anywhere, because the lab,
    /// the economy simulator and the server must replay as deterministically as the engine.
    /// </summary>
    public sealed class CodeHygieneGuardTests
    {
        private const string ForbiddenSample = "PuzzleCore.Tests.Guards.Samples.ForbiddenCatchSample";
        private const string AllowedSample = "PuzzleCore.Tests.Guards.Samples.AllowedCatchSample";

        public static TheoryData<string> SourceProjects => new(ProjectRules.AllowedProjectReferences.Keys);

        [Theory]
        [MemberData(nameof(SourceProjects))]
        public void Source_assemblies_have_no_generic_or_empty_catch_blocks(string project)
        {
            using var inspection = new AssemblyInspection(AssemblyPath(project));

            IReadOnlyList<Violation> violations = inspection.GenericOrEmptyCatchBlocks();

            Assert.True(violations.Count == 0, string.Join("\n", violations));
        }

        [Theory]
        [MemberData(nameof(SourceProjects))]
        public void No_source_assembly_draws_numbers_from_system_random(string project)
        {
            using var inspection = new AssemblyInspection(AssemblyPath(project));

            Assert.False(inspection.References("System.Random"), project + " uses System.Random; use the seeded PCG32 streams of PuzzleCore.");
        }

        [Theory]
        [InlineData("CatchesEverything", "generic catch (System.Exception)")]
        [InlineData("CatchesEverythingWithAFilter", "generic catch with a when-filter")]
        [InlineData("SwallowsSilently", "empty catch block")]
        [InlineData("BareCatch", "generic catch (System.Object)")]
        public void The_catch_scanner_reports_each_forbidden_catch_block(string method, string reason)
        {
            using var inspection = new AssemblyInspection(typeof(CodeHygieneGuardTests).Assembly.Location);

            IReadOnlyList<Violation> violations = inspection.GenericOrEmptyCatchBlocks();

            Assert.Contains(violations, violation =>
                violation.Location.StartsWith(ForbiddenSample + "::" + method + "+IL_", StringComparison.Ordinal) && violation.Reason == reason);
        }

        [Fact]
        public void The_catch_scanner_accepts_a_specific_catch_that_handles_the_error()
        {
            using var inspection = new AssemblyInspection(typeof(CodeHygieneGuardTests).Assembly.Location);

            IEnumerable<Violation> violations = inspection.GenericOrEmptyCatchBlocks()
                .Where(violation => violation.Location.StartsWith(AllowedSample + "::", StringComparison.Ordinal));

            Assert.Empty(violations);
        }

        private static string AssemblyPath(string project) =>
            Path.Combine(AppContext.BaseDirectory, ProjectRules.AssemblyFileName(project));
    }
}

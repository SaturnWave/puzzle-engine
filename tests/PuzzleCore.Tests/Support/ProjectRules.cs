using System;
using System.Collections.Generic;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// The project layering of the engine (CoveredLot.md, shared engine §1), in one place for
    /// the tests. Directory.Build.targets holds the same table for the build-time guard; a
    /// test checks that the two are identical. Sort and Jam never reference each other.
    /// </summary>
    internal static class ProjectRules
    {
        public static IReadOnlyList<string> UnityBoundProjects { get; } = new[] { "PuzzleCore", "PuzzleCore.Jam" };

        public static IReadOnlyDictionary<string, IReadOnlyList<string>> AllowedProjectReferences { get; } =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["PuzzleCore"] = Array.Empty<string>(),
                ["PuzzleCore.Jam"] = new[] { "PuzzleCore" },
                ["PuzzleCore.Cli"] = new[] { "PuzzleCore", "PuzzleCore.Jam" },
            };

        /// <summary>The compiled file of each project.</summary>
        public static string AssemblyFileName(string project) => project + ".dll";
    }
}

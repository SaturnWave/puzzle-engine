using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PuzzleCore.Tests.Support;
using Xunit;

namespace PuzzleCore.Tests.Guards
{
    /// <summary>
    /// Keeps the layering of CLAUDE.md and the netstandard2.1 / C# 9 / no-package rule for
    /// the assemblies the Unity client compiles in. The build itself enforces the rules on
    /// evaluated MSBuild values (Directory.Build.targets, error BLOCK001); these tests prove
    /// that guard exists, fires and cannot be bypassed by an extra MSBuild file, and check the
    /// compiled output as a second line of defence.
    /// </summary>
    public sealed class ArchitectureGuardTests
    {
        private static readonly string[] AllowedMsBuildFiles = { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" };

        public static TheoryData<string> UnityBoundProjects => new(ProjectRules.UnityBoundProjects);

        public static TheoryData<string> SourceProjects => new(ProjectRules.AllowedProjectReferences.Keys);

        [Fact]
        public void Every_source_project_is_covered_by_the_layering_rules()
        {
            IEnumerable<string> onDisk = RepositoryLayout.Files("src")
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFileNameWithoutExtension(path));

            Assert.Equal(Sorted(ProjectRules.AllowedProjectReferences.Keys), Sorted(onDisk));
        }

        [Fact]
        public void The_build_guard_and_the_tests_share_one_layering_table()
        {
            string targets = File.ReadAllText(RepositoryLayout.FullPath("Directory.Build.targets"));
            Dictionary<string, string[]> buildTable = Regex.Matches(targets,
                    "'\\$\\(MSBuildProjectName\\)' == '([^']+)'\"><PuzzleLayeringKnown>true</PuzzleLayeringKnown><PuzzleAllowedProjectReferences>([^<]*)</PuzzleAllowedProjectReferences>")
                .ToDictionary(
                    match => match.Groups[1].Value,
                    match => match.Groups[2].Value.Split(';', StringSplitOptions.RemoveEmptyEntries),
                    StringComparer.Ordinal);

            Assert.Equal(Sorted(ProjectRules.AllowedProjectReferences.Keys), Sorted(buildTable.Keys));
            foreach ((string project, IReadOnlyList<string> allowed) in ProjectRules.AllowedProjectReferences)
            {
                Assert.Equal(Sorted(allowed), Sorted(buildTable[project]));
            }

            foreach (string project in ProjectRules.UnityBoundProjects)
            {
                Assert.Contains("'$(MSBuildProjectName)' == '" + project + "'", targets, StringComparison.Ordinal);
            }
        }

        [Theory]
        [MemberData(nameof(SourceProjects))]
        public void Project_files_reference_only_the_projects_their_layer_allows(string project)
        {
            IEnumerable<string> references = Elements(LoadProject(project), "ProjectReference")
                .SelectMany(element => (Attribute(element, "Include") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
                .Select(include => Path.GetFileNameWithoutExtension(include.Trim().Replace('\\', '/')));

            Assert.Empty(references.Except(ProjectRules.AllowedProjectReferences[project], StringComparer.Ordinal));
        }

        [Fact]
        public void Exactly_the_unity_bound_projects_target_netstandard21()
        {
            IEnumerable<string> netStandardProjects = ProjectRules.AllowedProjectReferences.Keys
                .Where(project => Elements(LoadProject(project), "TargetFramework").Any(element => element.Value.Trim() == "netstandard2.1"));

            Assert.Equal(Sorted(ProjectRules.UnityBoundProjects), Sorted(netStandardProjects));
        }

        [Fact]
        public void The_build_guard_rejects_a_unity_bound_project_with_loosened_settings()
        {
            ProcessResult result = RunDotnet(
                "msbuild", RepositoryLayout.FullPath("src/PuzzleCore/PuzzleCore.csproj"), "-nologo", "-nodeReuse:false",
                "-t:PuzzleGuardProjectRules", "-p:LangVersion=10.0", "-p:CheckForOverflowUnderflow=false",
                "-p:TreatWarningsAsErrors=false", "-p:AllowUnsafeBlocks=true", "-p:Nullable=disable",
                "-p:InvariantGlobalization=true", "-p:NoWarn=CS0219", "-p:WarningsNotAsErrors=CS0219");

            Assert.NotEqual(0, result.ExitCode);
            foreach (string expected in new[]
            {
                "PUZZLE001", "LangVersion must be 9.0", "CheckForOverflowUnderflow must be true", "TreatWarningsAsErrors must be true",
                "AllowUnsafeBlocks must not be true", "Nullable must be enable", "InvariantGlobalization must not be true",
                "NoWarn must stay", "WarningsNotAsErrors must be empty",
            })
            {
                Assert.Contains(expected, result.Output, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_build_guard_accepts_the_unity_bound_projects_as_committed()
        {
            ProcessResult result = RunDotnet(
                "msbuild", RepositoryLayout.FullPath("src/PuzzleCore/PuzzleCore.csproj"), "-nologo", "-nodeReuse:false",
                "-t:PuzzleGuardProjectRules");

            Assert.True(result.ExitCode == 0, result.Output);
        }

        [Fact]
        public void No_extra_msbuild_file_can_override_the_shared_build_rules()
        {
            IReadOnlyList<string> offenders = RepositoryLayout.Files()
                .Where(path => IsMsBuildSettingsFile(Path.GetFileName(path)) && !AllowedMsBuildFiles.Contains(path, StringComparer.Ordinal))
                .ToList();

            Assert.True(offenders.Count == 0,
                "Only the root Directory.Build.props/.targets and Directory.Packages.props may hold build settings:\n" + string.Join("\n", offenders));
        }

        [Theory]
        [MemberData(nameof(UnityBoundProjects))]
        public void Unity_bound_projects_restore_no_packages(string project)
        {
            string assetsPath = RepositoryLayout.FullPath($"src/{project}/obj/project.assets.json");
            using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));

            IEnumerable<string> packages = assets.RootElement.GetProperty("libraries").EnumerateObject()
                .Where(library => library.Value.GetProperty("type").GetString() != "project")
                .Select(library => library.Name);

            Assert.Empty(packages);
        }

        [Theory]
        [MemberData(nameof(SourceProjects))]
        public void Compiled_assemblies_reference_only_the_projects_their_layer_allows(string project)
        {
            using var inspection = new AssemblyInspection(Path.Combine(AppContext.BaseDirectory, ProjectRules.AssemblyFileName(project)));

            IEnumerable<string> forbidden = inspection.ReferencedAssemblyNames()
                .Where(name => name.StartsWith("Puzzle", StringComparison.Ordinal))
                .Except(ProjectRules.AllowedProjectReferences[project], StringComparer.Ordinal);

            Assert.Empty(forbidden);
        }

        [Theory]
        [MemberData(nameof(UnityBoundProjects))]
        public void Unity_bound_assemblies_reference_only_netstandard_and_allowed_siblings(string project)
        {
            using var inspection = new AssemblyInspection(RepositoryLayout.UnityBoundAssemblyPath(project));
            var allowed = new HashSet<string>(ProjectRules.AllowedProjectReferences[project], StringComparer.Ordinal) { "netstandard" };

            IReadOnlyList<string> unexpected = inspection.ReferencedAssemblyNames().Where(name => !allowed.Contains(name)).ToList();

            Assert.True(unexpected.Count == 0, project + " references: " + string.Join(", ", unexpected));
        }

        [Fact]
        public void Every_source_and_test_project_is_part_of_the_solution()
        {
            string solution = File.ReadAllText(RepositoryLayout.FullPath("PuzzleEngine.sln"));
            IEnumerable<string> listed = Regex.Matches(solution, "\"([^\"]+\\.csproj)\"")
                .Select(match => match.Groups[1].Value.Replace('\\', '/'));
            IEnumerable<string> onDisk = RepositoryLayout.Files("src").Concat(RepositoryLayout.Files("tests"))
                .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(Sorted(onDisk), Sorted(listed));
        }

        private static bool IsMsBuildSettingsFile(string fileName) =>
            fileName.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rsp", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("Directory.Packages.", StringComparison.OrdinalIgnoreCase);

        private static XDocument LoadProject(string project) =>
            XDocument.Load(RepositoryLayout.FullPath($"src/{project}/{project}.csproj"));

        /// <summary>MSBuild names are case-insensitive and may carry the legacy XML namespace.</summary>
        private static IEnumerable<XElement> Elements(XDocument document, string localName) =>
            document.Descendants().Where(element => string.Equals(element.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

        private static string? Attribute(XElement element, string localName) => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase))?.Value;

        private static List<string> Sorted(IEnumerable<string> values) => values.OrderBy(value => value, StringComparer.Ordinal).ToList();

        private static ProcessResult RunDotnet(params string[] arguments)
        {
            string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
            var start = new ProcessStartInfo(host)
            {
                WorkingDirectory = RepositoryLayout.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + host);
            System.Threading.Tasks.Task<string> errors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output + errors.Result);
        }

        private sealed record ProcessResult(int ExitCode, string Output);
    }
}

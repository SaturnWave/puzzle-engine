using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace PuzzleCore.Tests.Support
{
    /// <summary>
    /// Locates the repository root (the folder holding PuzzleEngine.sln) and lists the files
    /// git knows about: tracked files plus untracked files that .gitignore does not
    /// exclude. Build output, agent worktrees and other ignored folders are therefore
    /// never inspected, while a new file is checked before anyone commits it.
    /// </summary>
    internal static class RepositoryLayout
    {
        private const string SolutionFileName = "PuzzleEngine.sln";

        private static readonly Lazy<IReadOnlyList<string>> KnownFiles = new(ListFilesKnownToGit);

        public static string Root { get; } = FindRoot();

        public static string FullPath(string relativePath) =>
            Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>Repository-relative paths with '/' separators under the folder, sorted ordinally.</summary>
        public static IReadOnlyList<string> Files(string relativeDirectory = "")
        {
            if (relativeDirectory.Length == 0)
            {
                return KnownFiles.Value;
            }

            string prefix = relativeDirectory.TrimEnd('/') + "/";
            return KnownFiles.Value.Where(path => path.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        }

        public static string Relative(string fullPath) =>
            Path.GetRelativePath(Root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

        public static string UnityBoundAssemblyPath(string assemblyName) =>
            Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");

        private static IReadOnlyList<string> ListFilesKnownToGit()
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // git writes paths as UTF-8; the platform default (a console code page on
                // Windows) would garble non-ASCII names, which build_pdf.py reads as UTF-8.
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "ls-files", "-z", "--cached", "--others", "--exclude-standard" })
            {
                start.ArgumentList.Add(argument);
            }

            using Process git = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start git; the guard tests need git on PATH.");
            string output = git.StandardOutput.ReadToEnd();
            string errors = git.StandardError.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode != 0)
            {
                throw new InvalidOperationException("git ls-files failed in " + Root + ": " + errors);
            }

            // Paths deleted from the working tree but still in the index are skipped.
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(path => File.Exists(FullPath(path)))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
        }

        private static string FindRoot()
        {
            for (DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException(
                $"No {SolutionFileName} above {AppContext.BaseDirectory}; run the tests from inside the repository.");
        }
    }
}

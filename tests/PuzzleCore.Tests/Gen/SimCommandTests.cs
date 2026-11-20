using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PuzzleCore.Gen;
using PuzzleCore.Randomness;
using Xunit;

namespace PuzzleCore.Tests.Gen
{
    /// <summary>
    /// The sim command as a user runs it (Q84): the built CLI in its own process, its routing, its lines and its exit
    /// codes. 2 for bad arguments and an output folder it cannot make; 0 for a finished run.
    /// </summary>
    public sealed class SimCommandTests
    {
        [Fact]
        public void Bad_arguments_exit_2_with_the_usage()
        {
            CliResult missingCount = RunCli("sim", "campaign-jam");
            CliResult badCount = RunCli("sim", "campaign-jam", "many");
            CliResult badPack = RunCli("sim", "Campaign Jam", "1");

            Assert.Equal(2, missingCount.ExitCode);
            Assert.Contains("PuzzleCore.Cli sim <packId> <count> [outDir]", missingCount.Output, StringComparison.Ordinal);
            Assert.Equal((2, "usage: sim <packId> <count> [outDir]\n"), (badCount.ExitCode, badCount.Output));
            Assert.Equal((2, "usage: sim <packId> <count> [outDir]\n"), (badPack.ExitCode, badPack.Output));
        }

        [Fact]
        public void An_output_folder_that_cannot_be_made_exits_2_before_any_slot_is_played()
        {
            // A file where the folder should be: the folder cannot be made, and sim says so before it plays anything.
            string file = Path.Combine(Path.GetTempPath(), "puzzle-sim-cli-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(file, "not a folder");
            try
            {
                CliResult result = RunCli("sim", "campaign-jam", "1", file);

                Assert.Equal(2, result.ExitCode);
                Assert.StartsWith("cannot use the output folder " + file + ": ", result.Output, StringComparison.Ordinal);
                Assert.Single(result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void Sim_prints_a_line_per_slot_and_writes_the_reports_it_plays()
        {
            string directory = Path.Combine(Path.GetTempPath(), "puzzle-sim-cli-" + Guid.NewGuid().ToString("N"));
            try
            {
                CliResult result = RunCli("sim", "campaign-jam", "1", directory);
                SimPack expected = LotSim.Run(PackId.Parse("campaign-jam").Or(default), LotSim.FirstSlots(1), new SimSettings(new WorkerCount(1), WithFailCauses: true));
                string[] lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                Assert.Equal(0, result.ExitCode);
                Assert.Equal(2, lines.Length);
                Assert.StartsWith("   0  --  Greedy=", lines[0], StringComparison.Ordinal);
                Assert.StartsWith("simulated 1/1, ", lines[1], StringComparison.Ordinal);
                Assert.Equal(LotSimCsv.Write(expected), File.ReadAllText(Path.Combine(directory, "campaign-jam.sim.csv"), Encoding.UTF8));
                Assert.Equal(LotSimCsv.WriteFails(expected), File.ReadAllText(Path.Combine(directory, "campaign-jam.sim-fails.csv"), Encoding.UTF8));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>Runs the built CLI, which the test project references, in its own process.</summary>
        private static CliResult RunCli(params string[] arguments)
        {
            string host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";
            var start = new ProcessStartInfo(host)
            {
                WorkingDirectory = Path.GetTempPath(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "PuzzleCore.Cli.dll"));
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + host);
            System.Threading.Tasks.Task<string> errors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new CliResult(process.ExitCode, output + errors.Result);
        }

        private sealed record CliResult(int ExitCode, string Output);
    }
}

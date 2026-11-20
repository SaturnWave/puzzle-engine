using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using PuzzleCore.Bot;
using PuzzleCore.Gen;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "sim &lt;packId&gt; &lt;count&gt; [outDir]" (Q84): plays slots 0..count-1 of a pack with Covered Lot's four bots, 200
    /// runs each (<see cref="LotSim"/>), and prints one line per slot: each profile's Won per mille, then Planner3's
    /// near-miss Yes per mille of its fails and its late fails per mille of its fails. With outDir it writes the two
    /// reports and Planner3's sample records (<see cref="LotSimFiles"/>), creating the folder before the run so a bad path
    /// fails at once. Exit code 1 on an engine fault, 2 on bad arguments, an output folder it cannot create or files it
    /// cannot write; rejected slots are listed and are not faults.
    /// </summary>
    internal static class SimCommand
    {
        public static int Run(string packText, string countText, string? outDirectory, TextWriter output)
        {
            if (!PackId.Parse(packText).TryGet(out PackId pack) || !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                output.Write("usage: sim <packId> <count> [outDir]\n");
                return 2;
            }

            if (!LotGrading.HasRoomForIds(pack, count))
            {
                output.Write("the pack id is too long: level ids {packId}-{slot:0000} have at most 64 characters\n");
                return 2;
            }

            if (outDirectory is not null && !CanCreate(outDirectory, output))
            {
                return 2;
            }

            Stopwatch clock = Stopwatch.StartNew();
            SimPack sim;
            try
            {
                sim = LotSim.Run(pack, LotSim.FirstSlots(count), new SimSettings(WorkerCount.EveryProcessor, WithFailCauses: true));
            }
            catch (InvalidOperationException fault)
            {
                output.Write("engine fault: " + fault.Message + "\n");
                return 1;
            }

            foreach (SimSlot slot in sim.Slots)
            {
                output.Write(Line(slot));
            }

            if (outDirectory is not null && !Wrote(sim, outDirectory, output))
            {
                return 2;
            }

            output.Write(string.Concat(
                "simulated ", Number(sim.Simulated.Count()), "/", Number(count), ", ",
                clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture), " ms\n"));
            return 0;
        }

        /// <summary>Creates the output folder, or says why it cannot.</summary>
        private static bool CanCreate(string directory, TextWriter output)
        {
            try
            {
                Directory.CreateDirectory(directory);
                return true;
            }
            catch (IOException problem)
            {
                return Refused("cannot use the output folder " + directory, problem, output);
            }
            catch (UnauthorizedAccessException problem)
            {
                return Refused("cannot use the output folder " + directory, problem, output);
            }
            catch (ArgumentException problem)
            {
                return Refused("cannot use the output folder " + directory, problem, output);
            }
        }

        /// <summary>Writes the reports and records, or says why it cannot.</summary>
        private static bool Wrote(SimPack sim, string directory, TextWriter output)
        {
            try
            {
                LotSimFiles.Write(sim, directory);
                return true;
            }
            catch (IOException problem)
            {
                return Refused("cannot write the reports to " + directory, problem, output);
            }
            catch (UnauthorizedAccessException problem)
            {
                return Refused("cannot write the reports to " + directory, problem, output);
            }
        }

        /// <summary>One line for the user, without a stack trace; always false.</summary>
        private static bool Refused(string what, Exception problem, TextWriter output)
        {
            output.Write(what + ": " + problem.Message + "\n");
            return false;
        }

        private static string Line(SimSlot slot) => slot switch
        {
            SimSlot.Simulated level => string.Concat(
                Number(level.Slot.Value).PadLeft(4), "  ", level.Built.Deduction.TryGet(out DeductionGrade.Graded? grade) ? ContractNames.Of(grade.Rung) : "--",
                string.Concat(level.Profiles.Select(profile => "  " + profile.Profile.Name + "=" + Number(profile.WonPerMille.Value))),
                Planner3Line(level),
                "\n"),
            SimSlot.Rejected rejected => Number(rejected.Slot.Value).PadLeft(4) + "  REJECTED  " + rejected.Reason.ToString() + "\n",
            _ => throw new ArgumentOutOfRangeException(nameof(slot), "Unknown sim slot."),
        };

        private static string Planner3Line(SimSlot.Simulated level)
        {
            SimProfileRuns planner3 = level.Profiles.Single(profile => profile.Profile.Id == BotProfileId.Planner3);
            return planner3.Fails == 0
                ? "  p3fails=0"
                : string.Concat(
                    "  p3nearMiss=", Number(PerMille.Of(planner3.NearMisses, planner3.Fails).Value),
                    "  p3late=", Number(PerMille.Of(planner3.LateFails, planner3.Fails).Value));
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

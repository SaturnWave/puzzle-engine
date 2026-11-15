using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using PuzzleCore.Gen;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "gen &lt;packId&gt; &lt;count&gt; [outDir]": builds and grades campaign slots 0..count-1 of a pack through
    /// <see cref="LotGrading"/> and prints one line per slot: size, vehicles, bays, PeakPar, slack, the random-play fail
    /// rate (DeadEnd), the greedy-habit win rate and, on covered levels, covers, tags and the deduction grade. With
    /// outDir it writes &lt;levelId&gt;.pc-lot for each level, then &lt;packId&gt;.presentation.json and
    /// &lt;packId&gt;.grades.csv (<see cref="LotPackFiles"/>). Exit code 1 if any slot was rejected.
    /// </summary>
    internal static class GenCommand
    {
        private const int GreedyPlayouts = 200;

        public static int Run(string packText, string countText, string? outDirectory, TextWriter output)
        {
            if (!PackId.Parse(packText).TryGet(out PackId pack) || !int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                output.Write("usage: gen <packId> <count> [outDir]\n");
                return 2;
            }

            if (!LotGrading.HasRoomForIds(pack, count))
            {
                output.Write("the pack id is too long: level ids {packId}-{slot:0000} have at most 64 characters\n");
                return 2;
            }

            var graded = new List<GradedLot>();
            int rejected = 0;
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 0; index < count; index++)
            {
                switch (LotGrading.Grade(pack, new LevelIndex(index)))
                {
                    case LotGradeResult.Graded result:
                        graded.Add(result.Lot);
                        output.Write(Line(result.Lot, pack));
                        break;
                    case LotGradeResult.Rejected failed:
                        rejected++;
                        output.Write(Number(index).PadLeft(4) + "  REJECTED  " + failed.Reason.ToString() + "\n");
                        break;
                }
            }

            if (outDirectory is not null)
            {
                LotPackFiles.Write(new GradedLotPack(pack, graded), outDirectory);
            }

            output.Write(string.Concat(
                "built ", Number(count - rejected), "/", Number(count), ", ",
                clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture), " ms\n"));
            return rejected == 0 ? 0 : 1;
        }

        private static string Line(GradedLot lot, PackId pack)
        {
            LotLevel level = lot.Built.Level;
            string line = string.Concat(
                Number(lot.Slot.Value).PadLeft(4), "  ", level.Size.ToString(), "  n=", Number(level.VehicleCount),
                "  bays=", level.Bays.ToString(), "  peakPar=", lot.Built.PeakPar.ToString(), "  slack=", Number(lot.Built.Slack),
                "  randomFail=", Number(lot.Grade.Plan.DeadEnd.Value / 10), "%",
                "  greedyWin=", Number(LotPlayouts.GreedyWins(level, pack, lot.Slot, GreedyPlayouts).Value / 10), "%");
            if (lot.Grade.Deduction.TryGet(out DeductionGrade.Graded? grade))
            {
                line += string.Concat(
                    "  COVERED covers=", Number(new LotClues(level).HiddenCells.Count), " tags=", Number(level.Clues.Count),
                    " rung=", ContractNames.Of(grade.Rung), " steps=", grade.Steps.ToString());
            }

            return line + "\n";
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

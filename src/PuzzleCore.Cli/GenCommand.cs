using System.Diagnostics;
using System.Globalization;
using System.IO;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Search;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "gen &lt;packId&gt; &lt;count&gt; [outDir]": builds campaign slots 0..count-1 of a pack through
    /// <see cref="LotPackBuilder"/> and prints one line per level: size, vehicles, bays, PeakPar, slack,
    /// the random-play fail rate, the greedy-habit win rate and, on covered levels, covers, tags and the
    /// deduction grade. With outDir it writes each level as &lt;outDir&gt;/&lt;index&gt;.pc-lot.
    /// Exit code 1 if any slot was rejected.
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

            if (outDirectory is not null)
            {
                Directory.CreateDirectory(outDirectory);
            }

            int rejected = 0;
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 0; index < count; index++)
            {
                var slot = new LevelIndex(index);
                switch (LotPackBuilder.Build(pack, slot))
                {
                    case BuildResult.Built built:
                        output.Write(Line(index, built.Level, pack, slot));
                        if (outDirectory is not null)
                        {
                            File.WriteAllText(Path.Combine(outDirectory, Number(index).PadLeft(4, '0') + ".pc-lot"), LotText.Write(built.Level.Level));
                        }

                        break;
                    case BuildResult.Rejected failed:
                        rejected++;
                        output.Write(Number(index).PadLeft(4) + "  REJECTED  " + failed.Reason.ToString() + "\n");
                        break;
                }
            }

            output.Write(string.Concat(
                "built ", Number(count - rejected), "/", Number(count), ", ",
                clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture), " ms\n"));
            return rejected == 0 ? 0 : 1;
        }

        private static string Line(int index, BuiltLevel built, PackId pack, LevelIndex slot)
        {
            LotLevel level = built.Level;
            string line = string.Concat(
                Number(index).PadLeft(4), "  ", level.Size.ToString(), "  n=", Number(level.VehicleCount),
                "  bays=", level.Bays.ToString(), "  par=", built.PeakPar.ToString(), "  slack=", Number(built.Slack),
                "  randomFail=", Number(LotPlayouts.DeadEnd(level, pack, slot, LotPlayouts.DefaultPlayouts).Value / 10), "%",
                "  greedyWin=", Number(LotPlayouts.GreedyWins(level, pack, slot, GreedyPlayouts).Value / 10), "%");
            if (built.Deduction.TryGet(out DeductionGrade.Graded? grade))
            {
                line += string.Concat(
                    "  COVERED covers=", Number(new LotClues(level).HiddenCells.Count), " tags=", Number(level.Clues.Count),
                    " rung=", grade.Rung.ToString(), " steps=", grade.Steps.ToString());
            }

            return line + "\n";
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

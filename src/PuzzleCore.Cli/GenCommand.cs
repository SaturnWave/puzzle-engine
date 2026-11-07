using System.Diagnostics;
using System.Globalization;
using System.IO;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "gen &lt;packId&gt; &lt;count&gt; [outDir]": generates plain levels 0..count-1 of a pack from the content plan
    /// and prints one line per level (size, vehicles, bays, PeakPar, slack, tries). With outDir it writes
    /// each level as &lt;outDir&gt;/&lt;index&gt;.pc-lot. Exit code 1 if any level was rejected.
    /// </summary>
    internal static class GenCommand
    {
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
            int totalTries = 0;
            Stopwatch clock = Stopwatch.StartNew();
            for (int index = 0; index < count; index++)
            {
                var level = new LevelIndex(index);
                LotPlan plan = LotContentPlan.For(level);
                switch (LotGenerator.Generate(pack, level, plan))
                {
                    case GenerateResult.Generated generated:
                        GeneratedLot lot = generated.Lot;
                        totalTries += lot.Tries;
                        output.Write(string.Concat(
                            Number(index).PadLeft(4), "  ", lot.Level.Size.ToString(), "  vehicles=", Number(lot.Level.VehicleCount),
                            "  bays=", lot.Level.Bays.ToString(), "  peakPar=", lot.PeakPar.ToString(), "  slack=", Number(lot.Slack),
                            "  tries=", Number(lot.Tries),
                            "  randomFail=", Number(LotPlayouts.DeadEnd(lot.Level, pack, level, LotPlayouts.DefaultPlayouts).Value / 10), "%",
                            "  greedyWin=", Number(LotPlayouts.GreedyWins(lot.Level, pack, level, 200).Value / 10), "%\n"));
                        if (outDirectory is not null)
                        {
                            File.WriteAllText(Path.Combine(outDirectory, Number(index).PadLeft(4, '0') + ".pc-lot"), LotText.Write(lot.Level));
                        }

                        break;
                    case GenerateResult.Rejected failed:
                        rejected++;
                        totalTries += LotGenerator.TriesPerSlot;
                        output.Write(Number(index).PadLeft(4) + "  REJECTED  " + Histogram(failed.Tries) + "\n");
                        break;
                }
            }

            output.Write(string.Concat(
                "generated ", Number(count - rejected), "/", Number(count), ", tries ", Number(totalTries),
                ", ", clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture), " ms\n"));
            return rejected == 0 ? 0 : 1;
        }

        /// <summary>How many tries failed each gate, for example "TooFewBlocked=12 SlackOutOfBand=8".</summary>
        private static string Histogram(ReadOnlyArray<TryFailure> tries)
        {
            var counts = new int[(int)TryFailure.WitnessUnsolvable + 1];
            foreach (TryFailure failure in tries)
            {
                counts[(int)failure]++;
            }

            var text = new System.Text.StringBuilder();
            for (int reason = 0; reason < counts.Length; reason++)
            {
                if (counts[reason] > 0)
                {
                    text.Append(((TryFailure)reason).ToString()).Append('=').Append(Number(counts[reason])).Append(' ');
                }
            }

            return text.ToString().TrimEnd();
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

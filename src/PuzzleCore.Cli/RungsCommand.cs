using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Search;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "rungs &lt;packPrefix&gt; &lt;packCount&gt; &lt;slotCount&gt;": builds every covered slot below slotCount of the packs
    /// {packPrefix}-00 to {packPrefix}-{packCount - 1} through <see cref="LotPackBuilder"/>. It prints one line
    /// per slot (the planned rung, then the built level's rung, steps, covers, tags and fingerprint, or the
    /// reject reason), then each rung's build rate: built levels out of planned slots, plain rejects counted
    /// as misses. The development plan's P3 sample is "rungs rung-sample 25 100". Exit code 1 if a rung's
    /// rate is below 90%.
    /// </summary>
    internal static class RungsCommand
    {
        private const int RequiredPerMille = 900;

        public static int Run(string prefix, string packCountText, string slotCountText, TextWriter output)
        {
            if (!int.TryParse(packCountText, NumberStyles.None, CultureInfo.InvariantCulture, out int packCount)
                || !int.TryParse(slotCountText, NumberStyles.None, CultureInfo.InvariantCulture, out int slotCount)
                || packCount < 1
                || packCount > 100)
            {
                output.Write("usage: rungs <packPrefix> <packCount 1-100> <slotCount>\n");
                return 2;
            }

            var tallies = new SortedDictionary<DeductionRung, RungTally>();
            Stopwatch clock = Stopwatch.StartNew();
            for (int packNumber = 0; packNumber < packCount; packNumber++)
            {
                string packText = prefix + "-" + packNumber.ToString("00", CultureInfo.InvariantCulture);
                if (!PackId.Parse(packText).TryGet(out PackId pack))
                {
                    output.Write("not a pack id: " + packText + "\n");
                    return 2;
                }

                for (int index = 0; index < slotCount; index++)
                {
                    var slot = new LevelIndex(index);
                    if (!LotContentPlan.CoversFor(slot).TryGet(out CoverPlan? plan))
                    {
                        continue;
                    }

                    BuildResult result = LotPackBuilder.Build(pack, slot);
                    TallyFor(tallies, plan.Rung).Count(result);
                    output.Write(SlotLine(packText, index, plan, result));
                }
            }

            bool everyRungBuilds = true;
            foreach (KeyValuePair<DeductionRung, RungTally> rung in tallies)
            {
                int perMille = rung.Value.Built * 1000 / rung.Value.Planned;
                int coverPerMille = rung.Value.Built * 1000 / (rung.Value.Planned - rung.Value.PlainRejected);
                everyRungBuilds &= perMille >= RequiredPerMille;
                output.Write(string.Concat(
                    rung.Key.ToString(), ": planned ", Number(rung.Value.Planned), ", built ", Number(rung.Value.Built),
                    ", plain rejected ", Number(rung.Value.PlainRejected), ", cover rejected ", Number(rung.Value.CoverRejected),
                    ", rate ", Number(perMille), " per mille (covering alone: ", Number(coverPerMille), " per mille)\n"));
            }

            output.Write(clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms\n");
            return everyRungBuilds ? 0 : 1;
        }

        private static string SlotLine(string pack, int index, CoverPlan plan, BuildResult result)
        {
            string head = string.Concat(pack, " ", index.ToString("0000", CultureInfo.InvariantCulture), " plan=", plan.Rung.ToString());
            switch (result)
            {
                case BuildResult.Built built when built.Level.Deduction.TryGet(out DeductionGrade.Graded? grade):
                    string text = LotText.Write(built.Level.Level);
                    return string.Concat(
                        head, " built rung=", grade.Rung.ToString(), " steps=", grade.Steps.ToString(),
                        " ", LineStartingWith(text, "covers="), " ", LineStartingWith(text, "clues="),
                        " lot=", LotText.Fingerprint(built.Level.Level), "\n");
                case BuildResult.Rejected rejected:
                    return head + " REJECTED " + rejected.Reason.ToString() + "\n";
                default:
                    throw new System.InvalidOperationException("A covered slot built without a deduction grade.");
            }
        }

        private static RungTally TallyFor(SortedDictionary<DeductionRung, RungTally> tallies, DeductionRung rung)
        {
            if (!tallies.TryGetValue(rung, out RungTally? tally))
            {
                tally = new RungTally();
                tallies.Add(rung, tally);
            }

            return tally;
        }

        private static string LineStartingWith(string text, string start)
        {
            foreach (string line in text.Split('\n'))
            {
                if (line.StartsWith(start, System.StringComparison.Ordinal))
                {
                    return line;
                }
            }

            return start;
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>How the planned covered slots of one rung ended.</summary>
        private sealed class RungTally
        {
            public int Planned { get; private set; }

            public int Built { get; private set; }

            public int PlainRejected { get; private set; }

            public int CoverRejected { get; private set; }

            public void Count(BuildResult result)
            {
                Planned++;
                switch (result)
                {
                    case BuildResult.Built:
                        Built++;
                        break;
                    case BuildResult.Rejected { Reason: BuildFailure.PlainRejected }:
                        PlainRejected++;
                        break;
                    case BuildResult.Rejected { Reason: BuildFailure.CoverRejected }:
                        CoverRejected++;
                        break;
                    default:
                        throw new System.InvalidOperationException("Unknown build result.");
                }
            }
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using PuzzleCore;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;

namespace CoveredLot.Playtest.Game
{
    /// <summary>One playable level: its name, the loaded level and its par (fewest bays that win).</summary>
    public sealed record PlaytestLevel(string Name, LotLevel Level, CapacityCount PeakPar);

    /// <summary>
    /// The playtest catalogue: the brief's covered 6×6 example board, then the plain campaign levels
    /// generated from the content plan (pack "campaign-jam"). Rejected generator slots are left out.
    /// Built once and shared; levels are immutable.
    /// </summary>
    public sealed class PlaytestLevels
    {
        public const int CampaignSlots = 80;

        private const string ExampleBoard =
            "pc-lot\nr=1\nsize=6x6\nbays=4\npillars=4\n"
            + "vehicles=B:1:2:N;R:8:2:E;Y:12:2:E;B:14:2:S;B:16:2:E;Y:23:2:N;R:24:2:N;R:27:2:W;Y:33:2:E\n"
            + "covers=3,7\nclues=Not(3,R)";

        private static readonly NodeBudget ParBudget = new NodeBudget(1_000_000);

        public PlaytestLevels()
        {
            var levels = new List<PlaytestLevel>();
            if (LotText.Read(ExampleBoard) is LotTextResult.Read read
                && LotLoader.Load(read.Definition) is LoadResult.Loaded example
                && LotSolver.PeakPar(example.Level, example.Level.Bays.AsCapacity, ParBudget) is PeakParResult.Found examplePar)
            {
                levels.Add(new PlaytestLevel("Example 6×6 (covered)", example.Level, examplePar.Peak));
            }

            PackId pack = PackId.Parse("campaign-jam").Or(default);
            for (int index = 0; index < CampaignSlots; index++)
            {
                var slot = new LevelIndex(index);
                if (LotPackBuilder.Build(pack, slot) is BuildResult.Built built)
                {
                    string name = "Level " + (index + 1).ToString(CultureInfo.InvariantCulture) + " · " + built.Level.Level.Size.ToString();
                    if (built.Level.Deduction.TryGet(out PuzzleCore.Search.DeductionGrade.Graded? grade))
                    {
                        name += " · covered " + grade.Rung.ToString();
                    }

                    levels.Add(new PlaytestLevel(name, built.Level.Level, built.Level.PeakPar));
                }
            }

            All = levels;
        }

        public IReadOnlyList<PlaytestLevel> All { get; }
    }
}

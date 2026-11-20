using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Search;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>One built level of the calibration pack, with its slot.</summary>
    internal sealed record CampaignLevel(int Slot, BuiltLevel Built)
    {
        public LotLevel Level => Built.Level;

        /// <summary>The rung the level's covers are deduced at; None for a plain level.</summary>
        public Option<DeductionRung> Rung =>
            Built.Deduction.TryGet(out DeductionGrade.Graded? grade) ? Option.Some(grade.Rung) : Option.None<DeductionRung>();

        public LevelIndex Index => new(Slot);
    }

    /// <summary>
    /// The pack the bots are measured on (TC12): campaign-jam, slots 0-99, which builds 98 levels (62 and 92 are rejected),
    /// 16 of them covered. Built once for all the bot tests.
    /// </summary>
    internal static class CampaignLevels
    {
        private static readonly Lazy<IReadOnlyList<CampaignLevel>> Built = new(BuildAll);

        public static PackId Pack { get; } = PackId.Parse("campaign-jam").Or(default);

        public static IReadOnlyList<CampaignLevel> All => Built.Value;

        public static IEnumerable<CampaignLevel> Covered => All.Where(level => level.Level.IsCoveredLevel);

        private static IReadOnlyList<CampaignLevel> BuildAll()
        {
            var levels = new List<CampaignLevel>();
            for (int slot = 0; slot < 100; slot++)
            {
                if (LotPackBuilder.Build(Pack, new LevelIndex(slot)) is BuildResult.Built built)
                {
                    levels.Add(new CampaignLevel(slot, built.Level));
                }
            }

            return levels;
        }
    }
}

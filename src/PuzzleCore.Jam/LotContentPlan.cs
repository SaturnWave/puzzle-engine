using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// The campaign ramp: lot size, colours, sets, bays and the slack band by level index, plus which
    /// levels are covered. Starting values to tune from bot and playtest data; every number is an estimate.
    /// </summary>
    public static class LotContentPlan
    {
        /// <summary>About 20 plain levels teach the loop before the first covered level (onboarding).</summary>
        public const int FirstCoveredLevel = 20;

        public static LotPlan For(LevelIndex level)
        {
            int index = level.Value;
            if (index < 5)
            {
                return new LotPlan(5, 5, Colours: 3, Sets: 3, Bays: 5, MinSlack: 1, MaxSlack: 2, VanShare: new PerMille(0), Pillars: 0, MinBlocked: new PerMille(300));
            }

            if (index < 15)
            {
                return new LotPlan(6, 6, Colours: 3, Sets: 4, Bays: 5, MinSlack: 1, MaxSlack: 2, VanShare: new PerMille(150), Pillars: 1, MinBlocked: new PerMille(400));
            }

            if (index < 30)
            {
                return new LotPlan(7, 7, Colours: 4, Sets: 5, Bays: 5, MinSlack: 0, MaxSlack: 1, VanShare: new PerMille(200), Pillars: 2, MinBlocked: new PerMille(500));
            }

            return new LotPlan(8, 8, Colours: 5, Sets: 7, Bays: 5, MinSlack: 0, MaxSlack: 1, VanShare: new PerMille(250), Pillars: 3, MinBlocked: new PerMille(450));
        }

        /// <summary>
        /// About 1 level in 5 is covered, from level 20 on (the sawtooth's "think" levels). The cover count
        /// and the hardest deduction allowed grow with the level.
        /// </summary>
        public static Option<CoverPlan> CoversFor(LevelIndex level)
        {
            int index = level.Value;
            bool isCovered = index >= FirstCoveredLevel && index % 5 == 4;
            if (!isCovered)
            {
                return Option.None<CoverPlan>();
            }

            if (index < 40)
            {
                return Option.Some(new CoverPlan(2, DeductionRung.D1));
            }

            return index < 60
                ? Option.Some(new CoverPlan(3, DeductionRung.D2))
                : Option.Some(new CoverPlan(4, DeductionRung.D3));
        }
    }
}

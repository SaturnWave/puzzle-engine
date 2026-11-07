namespace PuzzleCore.Jam
{
    /// <summary>
    /// The plain campaign's difficulty ramp: lot size, colours, sets, bays and the slack band by
    /// level index. Starting values to tune from bot and playtest data; every number is an estimate.
    /// </summary>
    public static class LotContentPlan
    {
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
    }
}

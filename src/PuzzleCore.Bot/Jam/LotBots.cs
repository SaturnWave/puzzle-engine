using System;
using PuzzleCore.Jam;
using PuzzleCore.Search;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// Covered Lot's four bots (Q73-Q76): their profiles, the move cap of a run, and the policy each plays. They are Covered
    /// Lot's own, so the difficulty pass retunes them under the band rule alone (Q75).
    /// </summary>
    public static class LotBots
    {
        /// <summary>Taps the free vehicle whose colour has the most waiting; reads every cover; no mistakes (Q76).</summary>
        public static BotProfile Greedy { get; } = new(BotProfileId.Greedy, new BotSearch.Habit(), new PerMille(0));

        /// <summary>One move ahead on what single tags say (ceiling D1), with 150‰ mistakes: can a slip-prone novice win?</summary>
        public static BotProfile Planner1 { get; } =
            new(BotProfileId.Planner1, new BotSearch.Limited(new MoveCount(1), DeductionRung.D1), new PerMille(150));

        /// <summary>Three moves ahead on what the tags about each cover say together (ceiling D2), with 50‰ mistakes: the greybox reference.</summary>
        public static BotProfile Planner3 { get; } =
            new(BotProfileId.Planner3, new BotSearch.Limited(new MoveCount(3), DeductionRung.D2), new PerMille(50));

        /// <summary>Plays the level's certified par path, with no search and no mistakes: an engine check (Q74).</summary>
        public static BotProfile Expert { get; } = new(BotProfileId.Expert, new BotSearch.FullSolver(), new PerMille(0));

        /// <summary>The four in their fixed order, the order of the sim files.</summary>
        public static ReadOnlyArray<BotProfile> Profiles { get; } = ReadOnlyArray.Of(Greedy, Planner1, Planner3, Expert);

        /// <summary>
        /// The move cap of a run on the level: three times the par estimate (shared-design.md §2), which for Covered Lot is
        /// one move per vehicle. Every move sends a vehicle out, so a Jam run never reaches it.
        /// </summary>
        public static MoveCount MoveCapFor(LotLevel level) => new(3 * level.VehicleCount);

        /// <summary>A new policy for one run of the profile on the level, chosen by how the profile searches.</summary>
        public static LotBotPolicy PolicyFor(BotProfile profile, BuiltLevel built) => profile.Search switch
        {
            BotSearch.Habit => new GreedyPolicy(built.Level),
            BotSearch.Limited limited => new PlannerPolicy(built.Level, limited.LookaheadPlies, limited.Ceiling),
            BotSearch.FullSolver => new ExpertPolicy(built.ParPath),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), "Unknown bot search."),
        };
    }
}

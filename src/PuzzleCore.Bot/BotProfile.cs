using System;
using PuzzleCore.Search;

namespace PuzzleCore.Bot
{
    /// <summary>
    /// How a bot chooses its moves (shared-design.md §2). Closed set: <see cref="Limited"/> sees what players see,
    /// <see cref="FullSolver"/> plays a solver's winning line, and <see cref="Habit"/> follows a game's habit. How a game's
    /// bots read the board, guess and score a line is that game's own.
    /// </summary>
    public abstract record BotSearch
    {
        private BotSearch()
        {
        }

        /// <summary>Sees what players see: it deduces up to <see cref="Ceiling"/> and looks <see cref="LookaheadPlies"/> moves ahead.</summary>
        public sealed record Limited : BotSearch
        {
            public Limited(MoveCount lookaheadPlies, DeductionRung ceiling)
            {
                if (lookaheadPlies.Value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(lookaheadPlies), lookaheadPlies.Value, "A limited bot looks at least one move ahead.");
                }

                if (ceiling is not (DeductionRung.D1 or DeductionRung.D2 or DeductionRung.D3))
                {
                    throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "Unknown deduction rung.");
                }

                LookaheadPlies = lookaheadPlies;
                Ceiling = ceiling;
            }

            public MoveCount LookaheadPlies { get; }

            public DeductionRung Ceiling { get; }
        }

        /// <summary>
        /// Full information, no mistakes: it plays a solver's winning line, and each game says which (Covered Lot: the
        /// level's certified par path, Q74).
        /// </summary>
        public sealed record FullSolver : BotSearch;

        /// <summary>Full information and no lookahead: the game's own habit picks the move (Covered Lot's Greedy, Q76).</summary>
        public sealed record Habit : BotSearch;
    }

    /// <summary>
    /// A bot: its profile and fixed name, how it searches, and how often it plays a random other legal move instead of
    /// the one it intended (shared-design.md §2).
    /// </summary>
    public sealed record BotProfile
    {
        public BotProfile(BotProfileId id, BotSearch search, PerMille mistakes)
        {
            Name = BotNames.Of(id);
            Id = id;
            Search = search;
            Mistakes = mistakes;
        }

        public BotProfileId Id { get; }

        /// <summary>The profile's fixed name: it seeds the run's Bot stream and names its rows in the sim CSV.</summary>
        public string Name { get; }

        public BotSearch Search { get; }

        public PerMille Mistakes { get; }
    }
}

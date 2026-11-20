using System;
using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// Expert (Q74): plays the level's certified par path (BuiltLevel.ParPath) move by move, with no search and no draws.
    /// It wins every built level by construction, so it checks the engine rather than measuring the level, and it never
    /// guesses.
    /// </summary>
    public sealed class ExpertPolicy : LotBotPolicy
    {
        private readonly ReadOnlyArray<Exit> _parPath;

        public ExpertPolicy(ReadOnlyArray<Exit> parPath) => _parPath = parPath;

        public override Option<GuessTally> Guesses => Option.None<GuessTally>();

        /// <summary>The par path is fixed before the run, so nothing a move reveals changes it.</summary>
        public override void Observe(ReadOnlyArray<PuzzleEvent> events)
        {
        }

        public override Exit Intend(LotState state, IReadOnlyList<Exit> legal, Pcg32 random)
        {
            // Every move sends one vehicle out, so the moves played are the vehicles gone.
            int played = state.ExitedCount;
            return played < _parPath.Count
                ? _parPath[played]
                : throw new InvalidOperationException("The par path ended with the level still in play.");
        }
    }
}

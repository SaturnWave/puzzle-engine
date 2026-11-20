using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// The covers a bot guessed, counted as they lifted: a cover its reading still left two or more colours was a guess,
    /// right when the colour seen was the one the bot believed.
    /// </summary>
    public sealed record GuessTally(int Right, int Wrong);

    /// <summary>
    /// A Covered Lot bot's policy for one run, and what its guesses came to. Closed set: <see cref="GreedyPolicy"/>,
    /// <see cref="PlannerPolicy"/> and <see cref="ExpertPolicy"/>.
    /// </summary>
    public abstract class LotBotPolicy : IBotPolicy<LotState, Exit>
    {
        private protected LotBotPolicy()
        {
        }

        /// <summary>The covers this run guessed, by how they turned out; None for a bot that reads every cover.</summary>
        public abstract Option<GuessTally> Guesses { get; }

        public abstract void Observe(ReadOnlyArray<PuzzleEvent> events);

        public abstract Exit Intend(LotState state, IReadOnlyList<Exit> legal, Pcg32 random);
    }
}

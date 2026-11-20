using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// The covers a bot guessed, counted as they lifted: a cover its reading still left two or more colours was a guess,
    /// right when the colour seen was the one the bot believed. A tally grows one guess at a time from <see cref="None"/>,
    /// so its two counts cannot trade places.
    /// </summary>
    public sealed record GuessTally
    {
        private GuessTally(int right, int wrong)
        {
            Right = right;
            Wrong = wrong;
        }

        public static GuessTally None { get; } = new GuessTally(0, 0);

        public int Right { get; }

        public int Wrong { get; }

        /// <summary>The tally with one more guess, right or wrong.</summary>
        public GuessTally After(bool wasRight) => wasRight ? new GuessTally(Right + 1, Wrong) : new GuessTally(Right, Wrong + 1);
    }

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

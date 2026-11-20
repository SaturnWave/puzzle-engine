using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// Greedy (Q76): taps the free vehicle whose colour has the most already waiting in the bays, any free vehicle when none
    /// waits, with ties drawn from the Bot stream. It reads every cover's colour, which on a certified level is what
    /// deduction gives (ceiling D3), so it never guesses.
    /// </summary>
    public sealed class GreedyPolicy : LotBotPolicy
    {
        private readonly LotLevel _level;
        private readonly List<Exit> _best = new List<Exit>();

        public GreedyPolicy(LotLevel level) => _level = level;

        public override Option<GuessTally> Guesses => Option.None<GuessTally>();

        /// <summary>Greedy knows every colour from the start, so nothing a move reveals changes its play.</summary>
        public override void Observe(ReadOnlyArray<PuzzleEvent> events)
        {
        }

        public override Exit Intend(LotState state, IReadOnlyList<Exit> legal, Pcg32 random)
        {
            BestMoves(state, legal, _best);
            return _best.Count == 1 ? _best[0] : _best[random.NextIndex(_best.Count)];
        }

        /// <summary>The legal moves whose colour has the most waiting in the bays, in the order of <paramref name="legal"/>.</summary>
        public void BestMoves(LotState state, IReadOnlyList<Exit> legal, List<Exit> into)
        {
            LotPosition position = LotPosition.Of(state);
            into.Clear();
            int most = -1;
            for (int index = 0; index < legal.Count; index++)
            {
                Exit move = legal[index];
                int waiting = position.Waiting(_level.VehicleOf(move.Piece).Colour);
                if (waiting > most)
                {
                    most = waiting;
                    into.Clear();
                }

                if (waiting == most)
                {
                    into.Add(move);
                }
            }
        }
    }
}

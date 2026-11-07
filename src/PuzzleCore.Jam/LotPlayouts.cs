using System.Collections.Generic;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// DeadEnd (shared grade field): the per-mille of seeded random legal playouts that end in a fail.
    /// A random player taps any vehicle that can leave, uniformly. A level where random tapping almost
    /// never fails asks nothing of the player. Streams use RandomPurpose.Playout, so the grade is the
    /// same on every machine.
    /// </summary>
    public static class LotPlayouts
    {
        public const int DefaultPlayouts = 1000;

        public static PerMille DeadEnd(LotLevel level, PackId pack, LevelIndex index, int playouts)
        {
            Seed seed = Seed.ForPack(pack);
            var rules = new JamRules(level);
            var moves = new List<Exit>();
            int failed = 0;
            for (int playout = 0; playout < playouts; playout++)
            {
                Pcg32 random = SeedDerivation.Stream(seed, StreamKey.ForPlayout(pack, index, playout));
                LotState state = rules.Start;
                while (rules.Evaluate(state) is Outcome.InPlay)
                {
                    moves.Clear();
                    rules.AppendLegalMoves(state, moves);
                    var applied = (MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, moves[random.NextIndex(moves.Count)]);
                    state = applied.Next;
                }

                if (rules.Evaluate(state) is Outcome.Failed)
                {
                    failed++;
                }
            }

            return PerMille.Of(failed, playouts);
        }

        /// <summary>
        /// GreedyWinPerMille: the win rate of the Greedy habit ("tap a free vehicle whose colour is
        /// already waiting in the bays, preferring the colour with the most waiting; otherwise any free
        /// vehicle"), ties broken by the seeded Playout stream. Hard levels must keep it low: a level
        /// that this habit wins asks for no planning.
        /// </summary>
        public static PerMille GreedyWins(LotLevel level, PackId pack, LevelIndex index, int playouts)
        {
            Seed seed = Seed.ForPack(pack);
            var rules = new JamRules(level);
            var moves = new List<Exit>();
            var best = new List<Exit>();
            int won = 0;
            for (int playout = 0; playout < playouts; playout++)
            {
                Pcg32 random = SeedDerivation.Stream(seed, StreamKey.ForPlayout(pack, index, playout));
                LotState state = rules.Start;
                while (rules.Evaluate(state) is Outcome.InPlay)
                {
                    moves.Clear();
                    rules.AppendLegalMoves(state, moves);
                    int[] waiting = Waiting(state);
                    int bestScore = -1;
                    best.Clear();
                    foreach (Exit move in moves)
                    {
                        int score = waiting[level.VehicleOf(move.Piece).Colour.Value];
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best.Clear();
                        }

                        if (score == bestScore)
                        {
                            best.Add(move);
                        }
                    }

                    var applied = (MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, best[random.NextIndex(best.Count)]);
                    state = applied.Next;
                }

                if (rules.Evaluate(state) is Outcome.Won)
                {
                    won++;
                }
            }

            return PerMille.Of(won, playouts);
        }

        /// <summary>How many of each colour wait in the bays.</summary>
        private static int[] Waiting(LotState state)
        {
            var waiting = new int[ColourId.MaxValue + 1];
            for (int slot = 0; slot < state.OccupiedSlots; slot++)
            {
                if (state.ColourIn(new SlotIndex((byte)slot)).TryGet(out ColourId colour))
                {
                    waiting[colour.Value]++;
                }
            }

            return waiting;
        }
    }
}

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
    }
}

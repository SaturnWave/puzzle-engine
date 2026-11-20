using PuzzleCore.Jam;
using PuzzleCore.Rules;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>How far an attempt got (Q81): the vehicles gone, and cleared_permille.</summary>
    public sealed class LotProgressTests
    {
        [Fact]
        public void Cleared_is_the_vehicles_gone_per_mille_rounded_down_and_1000_only_once_the_lot_is_empty()
        {
            // The free-cars board, nine cars: blues, reds, then yellows win with four bays.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var rules = new JamRules(level);
            LotState state = rules.Start;
            var cleared = new int[10];
            var exited = new int[10];
            int moves = 0;
            cleared[0] = LotProgress.Cleared(level, state).Value;
            foreach (int piece in new[] { 0, 3, 6, 1, 4, 7, 2, 5, 8 })
            {
                state = ((MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, new Exit(new PieceId((byte)piece)))).Next;
                moves++;
                cleared[moves] = LotProgress.Cleared(level, state).Value;
                exited[moves] = state.ExitedCount;
            }

            Assert.Equal(new[] { 0, 111, 222, 333, 444, 555, 666, 777, 888, 1000 }, cleared);
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, exited);
            Assert.IsType<Outcome.Won>(rules.Evaluate(state));
        }
    }
}

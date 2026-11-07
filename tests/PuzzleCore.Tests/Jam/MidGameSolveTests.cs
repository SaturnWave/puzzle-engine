using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>Solving from a position in play, and previewing rewinds: what the fail screen asks the engine.</summary>
    public sealed class MidGameSolveTests
    {
        private static readonly NodeBudget Budget = new(1_000_000);

        [Fact]
        public void Solving_from_the_start_state_finds_the_same_path_as_solving_the_level()
        {
            LotLevel level = Load(SixBySix());

            var fromStart = Assert.IsType<SolveResult<Exit>.Solved>(LotSolver.SolveFrom(level, LotState.Start(level), Budget));
            var whole = Assert.IsType<SolveResult<Exit>.Solved>(LotSolver.Solve(level, level.Bays.AsCapacity, Budget));

            Assert.Equal(whole.Path, fromStart.Path);
        }

        [Fact]
        public void A_full_row_is_unsolvable_and_one_spare_bay_makes_it_winnable_again()
        {
            LotLevel level = Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 3);

            Assert.IsType<SolveResult<Exit>.Unsolvable>(LotSolver.SolveFrom(level, attempt.Current, Budget));
            Assert.True(new JamRules(level).TryWithOneSpare(attempt.Current).TryGet(out LotState spared));
            var rescue = Assert.IsType<SolveResult<Exit>.Solved>(LotSolver.SolveFrom(level, spared, Budget));
            Assert.Equal(2, rescue.Path.Count);
        }

        [Fact]
        public void A_mid_game_path_holds_only_the_remaining_exits_and_finishes_the_win()
        {
            LotLevel level = Load(SixBySix());
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 3);

            var solved = Assert.IsType<SolveResult<Exit>.Solved>(LotSolver.SolveFrom(level, attempt.Current, Budget));

            Assert.Equal(7, solved.Path.Count);
            foreach (Exit exit in solved.Path)
            {
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(exit));
            }

            Assert.IsType<Outcome.Won>(attempt.Outcome);
        }

        [Fact]
        public void Peek_rewind_changes_nothing_and_predicts_the_rewind_exactly()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 4);
            attempt.TakeSpare();
            attempt.Move(new Exit(new PieceId(5)));
            LotState before = attempt.Current;

            var predictions = new List<LotState>();
            for (int moves = 1; moves <= 5; moves++)
            {
                if (attempt.PeekRewind(new MoveCount(moves)).TryGet(out LotState predicted))
                {
                    predictions.Add(predicted);
                }
            }

            Assert.Equal(before, attempt.Current);
            Assert.Equal(5, predictions.Count);
            Assert.False(attempt.PeekRewind(new MoveCount(6)).HasValue);
            Assert.All(predictions, state => Assert.Equal(5, state.Capacity.Value));

            attempt.Rewind(new MoveCount(3));
            Assert.Equal(predictions[2], attempt.Current);
        }

        private static Attempt<LotState, Exit, ExitRejection> Play(LotLevel level, params int[] pieces)
        {
            var rules = new JamRules(level);
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, rules.Start, PlayMode.Campaign);
            foreach (int piece in pieces)
            {
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(new Exit(new PieceId((byte)piece))));
            }

            return attempt;
        }
    }
}

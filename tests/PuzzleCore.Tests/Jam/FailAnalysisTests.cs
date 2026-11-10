using System;
using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The shared fail analysis on Covered Lot (brief: "Near-miss specifics"; shared engine §4
    /// "Near-miss soundness"): the near miss behind +1 bay and the rewind reach behind the rewind.
    /// </summary>
    public sealed class FailAnalysisTests
    {
        /// <summary>The playtest's offer budget, used until bench sets the default.</summary>
        private static readonly NodeBudget Budget = new(500_000);

        /// <summary>A budget far above the state space of the levels checked here, for the reference answers.</summary>
        private static readonly NodeBudget Generous = new(20_000_000);

        private static readonly ElapsedMs Ms = new(500);

        [Fact]
        public void A_full_row_that_one_more_bay_rescues_is_a_near_miss_from_one_solve_of_the_spared_state()
        {
            LotLevel level = Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 3); // B B R R fill the bays
            var solver = new RecordingSolver(level);

            Assert.Equal(new NearMiss.Yes(new MoveCount(2)), FailAnalysis.NearMissOf(attempt, solver, Budget));
            Assert.True(attempt.PeekSpare().TryGet(out LotState spared));
            Assert.Equal(new[] { spared }, solver.Asked);
        }

        [Fact]
        public void A_full_row_that_one_more_bay_cannot_rescue_is_no_near_miss()
        {
            LotLevel level = Load(ThirdsBehindYellows());
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 2, 1, 3); // B B R R; only yellows can leave next

            Assert.Equal(new NearMiss.No(), FailAnalysis.NearMissOf(attempt, new LotPositionSolver(level), Budget));
        }

        [Fact]
        public void The_near_miss_ignores_the_mode()
        {
            LotLevel level = Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red));
            var rules = new JamRules(level);
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, rules.Start, PlayMode.DailyProof);
            PlayOn(attempt, 0, 1, 2, 3);

            // The daily sells nothing, but its fails still log a near miss (brief: "Near-miss specifics").
            Assert.Equal(new NearMiss.Yes(new MoveCount(2)), FailAnalysis.NearMissOf(attempt, new LotPositionSolver(level), Budget));
        }

        [Fact]
        public void After_the_spare_a_second_fail_has_no_near_miss_but_can_still_rewind_behind_the_spare()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var rules = new JamRules(level);
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, rules.Start, PlayMode.Campaign);
            PlayOn(attempt, 0, 1, 2, 4);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
            PlayOn(attempt, 5); // five cars in five bays: full again
            Assert.IsType<Outcome.Failed>(attempt.Outcome);
            var solver = new RecordingSolver(level);

            Assert.Equal(new NearMiss.SpareUnavailable(), FailAnalysis.NearMissOf(attempt, solver, Budget));
            Assert.Empty(solver.Asked);

            // The longest rewind goes back to the start, behind the spare, which it adds back: five bays, all cars free.
            Assert.Equal(new MoveCount(5), attempt.RewindLimit);
            Assert.Equal(RewindReach.Winnable, FailAnalysis.RewindReachOf(attempt, solver, Budget));
            Assert.True(attempt.PeekRewind(new MoveCount(5)).TryGet(out LotState longest));
            Assert.Equal(5, longest.Capacity.Value);
            ReadOnlyArray<Exit> path = Assert.IsType<SolveResult<Exit>.Solved>(solver.SolveFrom(longest, Budget)).Path;
            var record = new List<RecordAction<Exit>> { MoveOf(0), MoveOf(1), MoveOf(2), MoveOf(4), new RecordAction<Exit>.TakeSpare(Ms), MoveOf(5), new RecordAction<Exit>.Rewind(new MoveCount(5), Ms) };
            AddMoves(record, path);
            Assert.Equal(
                new ReplayVerdict.Valid(new Outcome.Won(), new MoveCount(path.Count), SpareUsed: true),
                Replay.Run(rules, rules.Start, PlayMode.Campaign, ReadOnlyArray.CopyOf(record)).Verdict);
        }

        [Fact]
        public void When_no_spare_fits_the_near_miss_is_spare_unavailable_and_the_spare_is_refused()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var attempt = new Attempt<LotState, Exit, ExitRejection>(new NoRoomJamRules(level), LotState.Start(level), PlayMode.Campaign);
            PlayOn(attempt, 0, 1, 2, 4);
            var solver = new RecordingSolver(level);

            Assert.False(attempt.PeekSpare().HasValue);
            Assert.Equal(new NearMiss.SpareUnavailable(), FailAnalysis.NearMissOf(attempt, solver, Budget));
            Assert.Empty(solver.Asked);
            Assert.Equal(new StepResult<ExitRejection>.Refused(InvalidReason.SecondSpare), attempt.TakeSpare());
        }

        [Fact]
        public void A_solve_that_runs_out_of_budget_is_unknown_with_the_solvers_node_count()
        {
            LotLevel level = Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 3);
            var solver = new LotPositionSolver(level);
            var tiny = new NodeBudget(1);
            Assert.True(attempt.PeekSpare().TryGet(out LotState spared));
            var overrun = Assert.IsType<SolveResult<Exit>.BudgetExceeded>(solver.SolveFrom(spared, tiny));

            Assert.Equal(new NearMiss.Unknown(overrun.NodesUsed), FailAnalysis.NearMissOf(attempt, solver, tiny));
            Assert.Equal(RewindReach.Unknown, FailAnalysis.RewindReachOf(attempt, solver, tiny));
        }

        [Fact]
        public void The_reach_solves_only_the_longest_allowed_rewind()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 4);
            var solver = new RecordingSolver(level);

            Assert.Equal(RewindReach.Winnable, FailAnalysis.RewindReachOf(attempt, solver, Budget));
            Assert.True(attempt.PeekRewind(new MoveCount(4)).TryGet(out LotState longest));
            Assert.Equal(new[] { longest }, solver.Asked);
        }

        [Fact]
        public void Under_the_reveal_fence_the_reach_is_winnable_when_only_the_longest_allowed_rewind_wins()
        {
            // Piece 1 lifts its cover on move 2, so the limit is 2. Rewind 1 leaves B R Y in four bays, where
            // every next car fills the row; rewind 2 leaves B R, which wins.
            LotLevel level = FreeCarsWithCoversOn(1);
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 4);
            var solver = new RecordingSolver(level);

            Assert.Equal(new MoveCount(2), attempt.RewindLimit);
            Assert.True(attempt.PeekRewind(new MoveCount(1)).TryGet(out LotState shorter));
            Assert.IsType<SolveResult<Exit>.Unsolvable>(solver.SolveFrom(shorter, Generous));
            solver.Asked.Clear();

            Assert.Equal(RewindReach.Winnable, FailAnalysis.RewindReachOf(attempt, solver, Budget));
            Assert.True(attempt.PeekRewind(new MoveCount(2)).TryGet(out LotState longest));
            Assert.Equal(new[] { longest }, solver.Asked);
        }

        [Fact]
        public void Under_the_reveal_fence_a_longest_rewind_that_cannot_win_is_not_winnable()
        {
            // Piece 2 lifts its cover on move 3, so the limit is 1, and rewind 1 leaves B R Y in four bays.
            LotLevel level = FreeCarsWithCoversOn(2);
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 4);
            var solver = new RecordingSolver(level);

            Assert.Equal(new MoveCount(1), attempt.RewindLimit);
            Assert.Equal(RewindReach.NotWinnable, FailAnalysis.RewindReachOf(attempt, solver, Budget));
            Assert.Single(solver.Asked);
        }

        [Fact]
        public void At_rewind_limit_zero_the_reach_is_not_winnable_without_any_solve()
        {
            // Under the reveal fence a cover lifted by the failing move leaves no rewind at all.
            LotLevel level = FreeCarsWithCoversOn(4);
            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 4);
            var solver = new RecordingSolver(level);
            Assert.Equal(new MoveCount(0), attempt.RewindLimit);

            Assert.Equal(RewindReach.NotWinnable, FailAnalysis.RewindReachOf(attempt, solver, Budget));
            Assert.Empty(solver.Asked);
        }

        [Fact]
        public void Asking_on_an_attempt_that_has_not_failed_is_a_caller_bug()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var solver = new LotPositionSolver(level);
            Attempt<LotState, Exit, ExitRejection> inPlay = Play(level, 0, 1);
            Attempt<LotState, Exit, ExitRejection> won = Play(level, 0, 3, 6, 1, 4, 7, 2, 5, 8);
            Attempt<LotState, Exit, ExitRejection> rescued = Play(level, 0, 1, 2, 4);
            Assert.IsType<StepResult<ExitRejection>.Done>(rescued.TakeSpare());

            Assert.IsType<Outcome.Won>(won.Outcome);
            Assert.IsType<Outcome.InPlay>(rescued.Outcome);
            foreach (Attempt<LotState, Exit, ExitRejection> attempt in new[] { inPlay, won, rescued })
            {
                Assert.Throws<InvalidOperationException>(() => FailAnalysis.NearMissOf(attempt, solver, Budget));
                Assert.Throws<InvalidOperationException>(() => FailAnalysis.RewindReachOf(attempt, solver, Budget));
            }
        }

        [Fact]
        public void Peek_spare_is_the_rules_spare_of_the_current_state_in_any_outcome_and_changes_nothing()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var rules = new JamRules(level);
            Attempt<LotState, Exit, ExitRejection> inPlay = Play(level, 0, 1);
            Assert.Equal(rules.TryWithOneSpare(inPlay.Current), inPlay.PeekSpare());

            Attempt<LotState, Exit, ExitRejection> attempt = Play(level, 0, 1, 2, 4);
            LotState failed = attempt.Current;
            Option<LotState> predicted = attempt.PeekSpare();
            Assert.Equal(rules.TryWithOneSpare(failed), predicted);
            Assert.Equal(failed, attempt.Current);
            Assert.False(attempt.SpareUsed);

            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
            Assert.Equal(predicted, Option.Some(attempt.Current));
            Assert.False(attempt.PeekSpare().HasValue);
        }

        [Fact]
        public void On_generated_levels_every_answer_holds_through_the_validator_and_a_reference_search()
        {
            // Seeded random lines on generated slots 0-29, checked at the first fail and, after taking the
            // spare, at a second fail. A Yes wins through the replay validator with the spare and the
            // solver's path, and its count is that path's length; a No is unsolvable with a generous budget;
            // after the spare the near miss is SpareUnavailable. The reach equals a check of every allowed
            // rewind length, rewinds behind the spare included, which also tests upward closure; a Winnable
            // reach wins through the validator after the longest rewind.
            PackId pack = PackId.Parse("fail-analysis").Or(default);
            var seen = new CasesSeen();
            for (int index = 0; index < 30; index++)
            {
                if (!(LotPackBuilder.Build(pack, new LevelIndex(index)) is BuildResult.Built built))
                {
                    continue;
                }

                LotLevel level = built.Level.Level;
                for (int run = 0; run < 4; run++)
                {
                    CheckLine(level, new JamRules(level), Pcg32.Seeded((ulong)index, (ulong)(10 + run)), seen);
                }
            }

            Assert.True(seen.Yes > 0, "The property never met a near miss.");
            Assert.True(seen.No > 0, "The property never met a fail that one more bay cannot rescue.");
            Assert.True(seen.Winnable > 0, "The property never met a winnable rewind.");
            Assert.True(seen.NotWinnable > 0, "The property never met a fail that no allowed rewind rescues.");
            Assert.True(seen.SecondFails > 0, "The property never met a second fail after the spare.");
            Assert.True(seen.BehindSpare > 0, "The property never checked a rewind that reaches behind the spare.");
        }

        [Fact]
        public void Under_the_reveal_fence_every_reach_holds_on_the_covered_six_by_six_board()
        {
            // The same checks on the hand-made covered board (covers on pieces 3 and 7) under the fence, so
            // the fenced cases do not depend on which generated slots the content plan covers.
            LotLevel level = Load(SixBySix());
            var seen = new CasesSeen();
            for (int run = 0; run < 60; run++)
            {
                CheckLine(level, new JamRules(level), Pcg32.Seeded((ulong)run, 23UL), seen);
            }

            Assert.True(seen.FencedAboveZero > 0, "The fence never shortened a rewind to a length above zero.");
        }

        /// <summary>
        /// Plays a seeded random line to its first fail and checks both analyses there; then takes the
        /// spare, plays on, and at a second fail checks them again.
        /// </summary>
        private static void CheckLine(LotLevel level, IPuzzleRules<LotState, Exit, ExitRejection> rules, Pcg32 random, CasesSeen seen)
        {
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, LotState.Start(level), PlayMode.Campaign);
            var record = new List<RecordAction<Exit>>();
            if (!PlaysToAFail(attempt, rules, random, record))
            {
                return;
            }

            var solver = new LotPositionSolver(level);
            CheckNearMiss(level, rules, attempt, record, solver, seen);
            CheckRewindReach(level, rules, attempt, record, solver, seen);

            int spareAt = attempt.Moves.Value;
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
            record.Add(new RecordAction<Exit>.TakeSpare(Ms));
            if (!PlaysToAFail(attempt, rules, random, record))
            {
                return;
            }

            seen.SecondFails++;
            Assert.Equal(new NearMiss.SpareUnavailable(), FailAnalysis.NearMissOf(attempt, solver, Budget));
            CheckRewindReach(level, rules, attempt, record, solver, seen);
            seen.BehindSpare += attempt.Moves.Value - attempt.RewindLimit.Value < spareAt ? 1 : 0;
        }

        private static bool PlaysToAFail(
            Attempt<LotState, Exit, ExitRejection> attempt, IPuzzleRules<LotState, Exit, ExitRejection> rules, Pcg32 random, List<RecordAction<Exit>> record)
        {
            while (attempt.Outcome is Outcome.InPlay)
            {
                var moves = new List<Exit>();
                rules.AppendLegalMoves(attempt.Current, moves);
                Exit move = moves[random.NextIndex(moves.Count)];
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(move));
                record.Add(new RecordAction<Exit>.Move(move, Ms));
            }

            return attempt.Outcome is Outcome.Failed;
        }

        private static void CheckNearMiss(
            LotLevel level, IPuzzleRules<LotState, Exit, ExitRejection> rules, Attempt<LotState, Exit, ExitRejection> attempt,
            List<RecordAction<Exit>> record, LotPositionSolver solver, CasesSeen seen)
        {
            NearMiss nearMiss = FailAnalysis.NearMissOf(attempt, solver, Budget);
            Assert.True(attempt.PeekSpare().TryGet(out LotState spared), "Below the packing limit an unused spare always fits.");
            switch (nearMiss)
            {
                case NearMiss.Yes yes:
                    ReadOnlyArray<Exit> path = Assert.IsType<SolveResult<Exit>.Solved>(solver.SolveFrom(spared, Budget)).Path;
                    Assert.Equal(path.Count, yes.MovesFromWin.Value);
                    var rescue = new List<RecordAction<Exit>>(record) { new RecordAction<Exit>.TakeSpare(Ms) };
                    AddMoves(rescue, path);
                    Assert.Equal(
                        new ReplayVerdict.Valid(new Outcome.Won(), new MoveCount(attempt.Moves.Value + path.Count), SpareUsed: true),
                        Replay.Run(rules, LotState.Start(level), PlayMode.Campaign, ReadOnlyArray.CopyOf(rescue)).Verdict);
                    seen.Yes++;
                    break;
                case NearMiss.No:
                    Assert.IsType<SolveResult<Exit>.Unsolvable>(solver.SolveFrom(spared, Generous));
                    seen.No++;
                    break;
                default:
                    Assert.Fail("Unexpected near miss on a level this small: " + nearMiss);
                    break;
            }
        }

        private static void CheckRewindReach(
            LotLevel level, IPuzzleRules<LotState, Exit, ExitRejection> rules, Attempt<LotState, Exit, ExitRejection> attempt,
            List<RecordAction<Exit>> record, LotPositionSolver solver, CasesSeen seen)
        {
            RewindReach reach = FailAnalysis.RewindReachOf(attempt, solver, Budget);
            int limit = attempt.RewindLimit.Value;
            bool anyWins = false;
            bool shorterWins = false;
            for (int back = 1; back <= limit; back++)
            {
                Assert.True(attempt.PeekRewind(new MoveCount(back)).TryGet(out LotState rewound));
                bool wins = solver.SolveFrom(rewound, Generous) is SolveResult<Exit>.Solved;
                Assert.False(shorterWins && !wins, "A shorter rewind wins but a longer one does not.");
                shorterWins = wins;
                anyWins |= wins;
            }

            Assert.Equal(anyWins ? RewindReach.Winnable : RewindReach.NotWinnable, reach);
            bool fenceShortened = limit < Math.Min(Attempt<LotState, Exit, ExitRejection>.MaxRewind, attempt.Moves.Value);
            seen.FencedAboveZero += fenceShortened && limit > 0 ? 1 : 0;
            if (!anyWins)
            {
                seen.NotWinnable += limit > 0 ? 1 : 0;
                return;
            }

            seen.Winnable++;
            Assert.True(attempt.PeekRewind(attempt.RewindLimit).TryGet(out LotState longest));
            ReadOnlyArray<Exit> path = Assert.IsType<SolveResult<Exit>.Solved>(solver.SolveFrom(longest, Budget)).Path;
            var rescue = new List<RecordAction<Exit>>(record) { new RecordAction<Exit>.Rewind(attempt.RewindLimit, Ms) };
            AddMoves(rescue, path);
            Assert.Equal(
                new ReplayVerdict.Valid(new Outcome.Won(), new MoveCount(attempt.Moves.Value - limit + path.Count), attempt.SpareUsed),
                Replay.Run(rules, LotState.Start(level), PlayMode.Campaign, ReadOnlyArray.CopyOf(rescue)).Verdict);
        }

        private static void AddMoves(List<RecordAction<Exit>> record, ReadOnlyArray<Exit> path)
        {
            foreach (Exit exit in path)
            {
                record.Add(new RecordAction<Exit>.Move(exit, Ms));
            }
        }

        private static RecordAction<Exit> MoveOf(int piece) => new RecordAction<Exit>.Move(new Exit(new PieceId((byte)piece)), Ms);

        private static Attempt<LotState, Exit, ExitRejection> Play(LotLevel level, params int[] pieces)
        {
            var rules = new JamRules(level);
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, rules.Start, PlayMode.Campaign);
            PlayOn(attempt, pieces);
            return attempt;
        }

        private static void PlayOn(Attempt<LotState, Exit, ExitRejection> attempt, params int[] pieces)
        {
            foreach (int piece in pieces)
            {
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(new Exit(new PieceId((byte)piece))));
            }
        }

        /// <summary>How often a property met each case, so it cannot pass without exercising them all.</summary>
        private sealed class CasesSeen
        {
            public int Yes { get; set; }

            public int No { get; set; }

            public int Winnable { get; set; }

            /// <summary>NotWinnable answers that came from a solve, so limit-0 fails are not counted.</summary>
            public int NotWinnable { get; set; }

            public int SecondFails { get; set; }

            public int BehindSpare { get; set; }

            public int FencedAboveZero { get; set; }
        }

        /// <summary>A position solver that records every state it is asked to solve, then solves it for real.</summary>
        private sealed class RecordingSolver : IPositionSolver<LotState, Exit>
        {
            private readonly LotPositionSolver _inner;

            public RecordingSolver(LotLevel level) => _inner = new LotPositionSolver(level);

            public List<LotState> Asked { get; } = new List<LotState>();

            public SolveResult<Exit> SolveFrom(in LotState state, NodeBudget budget)
            {
                Asked.Add(state);
                return _inner.SolveFrom(state, budget);
            }
        }

        /// <summary>The Jam rules with no room for a spare: the packing-limit case that no valid level reaches.</summary>
        private sealed class NoRoomJamRules : IPuzzleRules<LotState, Exit, ExitRejection>
        {
            private readonly JamRules _jam;

            public NoRoomJamRules(LotLevel level) => _jam = new JamRules(level);

            public RewindFence RewindFence => _jam.RewindFence;

            public void AppendLegalMoves(in LotState state, List<Exit> into) => _jam.AppendLegalMoves(state, into);

            public MoveResult<LotState, ExitRejection> TryApply(in LotState state, Exit move) => _jam.TryApply(state, move);

            public Outcome Evaluate(in LotState state) => _jam.Evaluate(state);

            public Option<LotState> TryWithOneSpare(in LotState state) => Option.None<LotState>();

            public CapacityCount CapacityOf(in LotState state) => _jam.CapacityOf(state);
        }
    }
}

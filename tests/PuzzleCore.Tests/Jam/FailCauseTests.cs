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
    /// The fail cause on Covered Lot (shared-design.md §2, "Fail cause for analytics"): the losing move,
    /// how far back from the fail it was, and whether it sent out a covered vehicle not yet seen.
    /// </summary>
    public sealed class FailCauseTests
    {
        /// <summary>A budget far above the state space of the levels checked here.</summary>
        private static readonly NodeBudget Generous = new(20_000_000);

        [Fact]
        public void An_open_losing_exit_is_capacity()
        {
            // B R can still win; Y makes B R Y, where every next car fills the row. So the losing move is two back.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(new JamRules(level), level, 0, 1, 2, 4);

            Assert.Equal(new FailCause.Capacity(new MoveCount(2)), CauseOf(attempt, level, Generous));
        }

        [Fact]
        public void The_losing_exit_of_an_unseen_covered_vehicle_is_a_deduction_although_it_lifts_its_own_cover()
        {
            // The same line with piece 2 covered. Its exit lifts its cover, so only the SeenSet from before the exit shows it was hidden.
            LotLevel level = FreeCarsWithCoversOn(2);
            Attempt<LotState, Exit, ExitRejection> attempt = Play(new JamRules(level), level, 0, 1, 2, 4);
            Assert.True(attempt.Seen.Contains(new HiddenCell(2)));

            Assert.Equal(new FailCause.Deduction(new MoveCount(2)), CauseOf(attempt, level, Generous));
        }

        [Fact]
        public void An_insight_before_the_losing_exit_makes_it_capacity()
        {
            LotLevel level = FreeCarsWithCoversOn(2);
            var attempt = new Attempt<LotState, Exit, ExitRejection>(new JamRules(level), LotState.Start(level), PlayMode.Campaign);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(2)));
            PlayOn(attempt, 0, 1, 2, 4);

            Assert.Equal(new FailCause.Capacity(new MoveCount(2)), CauseOf(attempt, level, Generous));
        }

        [Fact]
        public void Another_cover_lifted_earlier_does_not_make_the_losing_vehicle_seen()
        {
            LotLevel level = FreeCarsWithCoversOn(0, 2);
            Attempt<LotState, Exit, ExitRejection> attempt = Play(new JamRules(level), level, 0, 1, 2, 4); // piece 0 lifts first, then piece 2 loses

            Assert.Equal(new FailCause.Deduction(new MoveCount(2)), CauseOf(attempt, level, Generous));
        }

        [Fact]
        public void After_a_rewind_the_cause_judges_the_moves_now_on_the_line()
        {
            // Unfenced, a rewind may undo piece 2's lift. The losing move is then piece 5, open, played in its place.
            LotLevel level = FreeCarsWithCoversOn(2);
            Attempt<LotState, Exit, ExitRejection> attempt = Play(new UnfencedJamRules(level), level, 0, 1, 2, 4);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(2))); // back to B R
            PlayOn(attempt, 5, 4); // B R Y R: full again

            Assert.Equal(new FailCause.Capacity(new MoveCount(2)), CauseOf(attempt, level, Generous));
        }

        [Fact]
        public void A_fail_right_after_the_spare_needs_no_solve()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(new JamRules(level), level, 0, 1, 2, 4);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
            PlayOn(attempt, 5); // five cars in five bays: full again
            var solver = new RecordingSolver(level);

            Assert.Equal(new FailCause.Capacity(new MoveCount(1)), FailAnalysis.CauseOf(attempt, solver, new LotHiddenMoveCheck(level), Generous));
            Assert.Empty(solver.Asked);
        }

        [Fact]
        public void An_overrun_gives_unknown_which_has_no_losing_move()
        {
            // The losing move is two back, so the scan must solve at least one entry; one node is never enough.
            // The search checks the middle entry first, two moves in, and reports that solve's node count.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            Attempt<LotState, Exit, ExitRejection> attempt = Play(new JamRules(level), level, 0, 1, 2, 4);
            var tiny = new NodeBudget(1);
            LotState middle = Play(new JamRules(level), level, 0, 1).Current;
            var overrun = Assert.IsType<SolveResult<Exit>.BudgetExceeded>(new LotPositionSolver(level).SolveFrom(middle, tiny));

            Assert.Equal(new FailCause.Unknown(overrun.NodesUsed), CauseOf(attempt, level, tiny));
        }

        [Fact]
        public void No_other_ending_gets_a_cause()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var rules = new JamRules(level);
            Attempt<LotState, Exit, ExitRejection> inPlay = Play(rules, level, 0, 1);
            Attempt<LotState, Exit, ExitRejection> won = Play(rules, level, 0, 3, 6, 1, 4, 7, 2, 5, 8);
            Attempt<LotState, Exit, ExitRejection> conceded = Play(rules, level, 0, 1);
            Assert.IsType<StepResult<ExitRejection>.Done>(conceded.Concede());
            Attempt<LotState, Exit, ExitRejection> rescued = Play(rules, level, 0, 1, 2, 4);
            Assert.IsType<StepResult<ExitRejection>.Done>(rescued.TakeSpare()); // back in play with five bays

            Assert.IsType<Outcome.Won>(won.Outcome);
            foreach (Attempt<LotState, Exit, ExitRejection> attempt in new[] { inPlay, won, conceded, rescued })
            {
                Assert.Throws<InvalidOperationException>(() => CauseOf(attempt, level, Generous));
            }
        }

        [Fact]
        public void Every_fail_gets_the_cause_that_a_full_scan_of_the_line_finds()
        {
            // Seeded random lines on generated slots 0-29 and on the covered 6×6 board, each to a first fail,
            // with an Insight on the way on some covered levels; then, after the spare on a near miss or after
            // a rewind that can win, to a second fail. At each fail the cause must equal a reference: every
            // entry between the scan's floor and the fail solved one by one, and the losing vehicle judged by
            // a SeenSet this test keeps itself. The search must stay within its bound of solves and solve only
            // entries between the floor and the fail, so never one behind the spare.
            var seen = new CasesSeen();
            PackId pack = PackId.Parse("fail-cause").Or(default);
            for (int index = 0; index < 30; index++)
            {
                if (!(LotPackBuilder.Build(pack, new LevelIndex(index)) is BuildResult.Built built))
                {
                    continue;
                }

                for (int run = 0; run < 4; run++)
                {
                    CheckRun(built.Level.Level, Pcg32.Seeded((ulong)index, (ulong)(40 + run)), seen);
                }
            }

            // On the 6×6 board an open car almost always loses, so free cars with three covered ones add deductions.
            LotLevel board = Load(SixBySix());
            LotLevel freeCovered = FreeCarsWithCoversOn(1, 2, 5);
            for (int run = 0; run < 60; run++)
            {
                CheckRun(board, Pcg32.Seeded((ulong)run, 41UL), seen);
                CheckRun(freeCovered, Pcg32.Seeded((ulong)run, 42UL), seen);
            }

            Assert.True(seen.Capacity > 0, "The property never met a capacity fail.");
            Assert.True(seen.Deduction > 0, "The property never met a deduction fail.");
            Assert.True(seen.AfterSpare > 0, "The property never met a second fail after the spare.");
            Assert.True(seen.AfterRewind > 0, "The property never met a second fail after a rewind.");
            Assert.True(seen.WithInsight > 0, "The property never met a fail after an Insight.");
        }

        private static void CheckRun(LotLevel level, Pcg32 random, CasesSeen seen)
        {
            var rules = new JamRules(level);
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, LotState.Start(level), PlayMode.Campaign);
            var line = new LineModel(level, attempt.Current);
            int insightAfterMoves = level.IsCoveredLevel ? random.NextIndex(6) : -1;
            if (!PlaysToAFail(attempt, rules, random, line, insightAfterMoves))
            {
                return;
            }

            CheckCause(level, attempt, line, seen);
            var solver = new LotPositionSolver(level);
            if (FailAnalysis.NearMissOf(attempt, solver, Generous) is NearMiss.Yes)
            {
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
                line.SpareTaken(attempt.Current);
                if (PlaysToAFail(attempt, rules, random, line, -1))
                {
                    CheckCause(level, attempt, line, seen);
                    seen.AfterSpare++;
                }

                return;
            }

            if (FailAnalysis.RewindReachOf(attempt, solver, Generous) == RewindReach.Winnable)
            {
                MoveCount back = attempt.RewindLimit;
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(back));
                line.Rewound(back.Value);
                if (PlaysToAFail(attempt, rules, random, line, -1))
                {
                    CheckCause(level, attempt, line, seen);
                    seen.AfterRewind++;
                }
            }
        }

        /// <summary>Plays random legal moves until the attempt ends; uses one Insight when the line reaches that many moves.</summary>
        private static bool PlaysToAFail(
            Attempt<LotState, Exit, ExitRejection> attempt, JamRules rules, Pcg32 random, LineModel line, int insightAfterMoves)
        {
            while (attempt.Outcome is Outcome.InPlay)
            {
                if (attempt.Moves.Value == insightAfterMoves && !attempt.InsightUsed)
                {
                    UseInsightOnACoveredVehicle(attempt, random, line);
                }

                var moves = new List<Exit>();
                rules.AppendLegalMoves(attempt.Current, moves);
                Exit move = moves[random.NextIndex(moves.Count)];
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(move));
                line.Moved(move, attempt.Current);
            }

            return attempt.Outcome is Outcome.Failed;
        }

        private static void UseInsightOnACoveredVehicle(Attempt<LotState, Exit, ExitRejection> attempt, Pcg32 random, LineModel line)
        {
            var covered = new List<PieceId>();
            foreach (Vehicle vehicle in line.Level.Vehicles)
            {
                if (vehicle.StartsCovered && !attempt.Current.HasExited(vehicle.Id))
                {
                    covered.Add(vehicle.Id);
                }
            }

            if (covered.Count == 0)
            {
                return;
            }

            PieceId piece = covered[random.NextIndex(covered.Count)];
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(piece.Value)));
            line.Seen(piece);
        }

        private static void CheckCause(LotLevel level, Attempt<LotState, Exit, ExitRejection> attempt, LineModel line, CasesSeen seen)
        {
            Assert.Equal(line.Last, attempt.Current);
            var solver = new RecordingSolver(level);

            FailCause cause = FailAnalysis.CauseOf(attempt, solver, new LotHiddenMoveCheck(level), Generous);

            Assert.Equal(ReferenceCause(level, line), cause);
            Assert.True(solver.Asked.Count <= CeilingLog2(line.Moves - line.Floor), "The search used more solves than a binary search needs.");
            foreach (LotState asked in solver.Asked)
            {
                Assert.True(line.IsBetweenFloorAndFail(asked), "The scan solved an entry outside the floor and the fail.");
            }

            seen.Capacity += cause is FailCause.Capacity ? 1 : 0;
            seen.Deduction += cause is FailCause.Deduction ? 1 : 0;
            seen.WithInsight += attempt.InsightUsed ? 1 : 0;
        }

        /// <summary>
        /// The losing move by a linear scan: solve each entry from the fail back to the floor, which must
        /// itself be winnable, and judge the losing vehicle by the test's own SeenSet before that move.
        /// </summary>
        private static FailCause ReferenceCause(LotLevel level, LineModel line)
        {
            var solver = new LotPositionSolver(level);
            Assert.IsType<SolveResult<Exit>.Solved>(solver.SolveFrom(line.Entry(line.Floor), Generous));
            int lastWinnable = line.Floor;
            for (int entry = line.Moves - 1; entry > line.Floor; entry--)
            {
                if (solver.SolveFrom(line.Entry(entry), Generous) is SolveResult<Exit>.Solved)
                {
                    lastWinnable = entry;
                    break;
                }
            }

            Exit losingMove = line.MoveInto(lastWinnable + 1);
            bool wasHidden = level.VehicleOf(losingMove.Piece).StartsCovered && !line.WasSeenBefore(lastWinnable + 1, losingMove.Piece);
            var back = new MoveCount(line.Moves - lastWinnable);
            return wasHidden ? new FailCause.Deduction(back) : new FailCause.Capacity(back);
        }

        private static int CeilingLog2(int count)
        {
            int log = 0;
            while ((1 << log) < count)
            {
                log++;
            }

            return log;
        }

        private static FailCause CauseOf(Attempt<LotState, Exit, ExitRejection> attempt, LotLevel level, NodeBudget budget) =>
            FailAnalysis.CauseOf(attempt, new LotPositionSolver(level), new LotHiddenMoveCheck(level), budget);

        private static Attempt<LotState, Exit, ExitRejection> Play(IPuzzleRules<LotState, Exit, ExitRejection> rules, LotLevel level, params int[] pieces)
        {
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, LotState.Start(level), PlayMode.Campaign);
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

        /// <summary>
        /// The test's own record of the current line: its entries, the move into each, and the pieces seen
        /// before each move, kept from the moves and the Insight alone, without reading the attempt.
        /// </summary>
        private sealed class LineModel
        {
            private readonly List<LotState> _entries = new List<LotState>();
            private readonly List<Exit> _moves = new List<Exit>();
            private readonly List<HashSet<byte>> _seenBeforeMove = new List<HashSet<byte>>();
            private readonly HashSet<byte> _seen = new HashSet<byte>();

            public LineModel(LotLevel level, LotState start)
            {
                Level = level;
                _entries.Add(start);
            }

            public LotLevel Level { get; }

            /// <summary>The spare's entry once the spare is taken, otherwise the start.</summary>
            public int Floor { get; private set; }

            public int Moves => _entries.Count - 1;

            public LotState Last => _entries[_entries.Count - 1];

            public LotState Entry(int moves) => _entries[moves];

            public Exit MoveInto(int moves) => _moves[moves - 1];

            public bool WasSeenBefore(int moves, PieceId piece) => _seenBeforeMove[moves - 1].Contains(piece.Value);

            public bool IsBetweenFloorAndFail(LotState state)
            {
                for (int entry = Floor + 1; entry < Moves; entry++)
                {
                    if (_entries[entry].Equals(state))
                    {
                        return true;
                    }
                }

                return false;
            }

            public void Moved(Exit move, LotState after)
            {
                _seenBeforeMove.Add(new HashSet<byte>(_seen));
                _moves.Add(move);
                _entries.Add(after);
                if (Level.VehicleOf(move.Piece).StartsCovered)
                {
                    _seen.Add(move.Piece.Value);
                }
            }

            public void Seen(PieceId piece) => _seen.Add(piece.Value);

            public void SpareTaken(LotState spared)
            {
                _entries[_entries.Count - 1] = spared;
                Floor = Moves;
            }

            /// <summary>Drops the last moves; only called without a spare, so no entry needs the spare added back.</summary>
            public void Rewound(int back)
            {
                _entries.RemoveRange(_entries.Count - back, back);
                _moves.RemoveRange(_moves.Count - back, back);
                _seenBeforeMove.RemoveRange(_seenBeforeMove.Count - back, back);
            }
        }

        /// <summary>How often the property met each case, so it cannot pass without exercising them all.</summary>
        private sealed class CasesSeen
        {
            public int Capacity { get; set; }

            public int Deduction { get; set; }

            public int AfterSpare { get; set; }

            public int AfterRewind { get; set; }

            public int WithInsight { get; set; }
        }
    }
}

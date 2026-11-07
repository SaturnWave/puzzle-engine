using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Rules;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>The Jam golden rule tests listed in CoveredLot.md ("Jam golden tests").</summary>
    public sealed class JamRulesGoldenTests
    {
        [Fact]
        public void The_stated_line_on_the_six_by_six_board_wins_with_four_bays_and_a_peak_of_three()
        {
            var rules = new JamRules(Load(SixBySix()));
            LotState state = rules.Start;
            var events = new List<PuzzleEvent>();
            int peak = 0;

            foreach (int piece in new[] { 0, 3, 4, 2, 8, 5, 1, 6, 7 })
            {
                var applied = Assert.IsType<MoveResult<LotState, ExitRejection>.Applied>(rules.TryApply(state, new Exit(new PieceId((byte)piece))));
                peak = System.Math.Max(peak, ParkedSlot(applied.Events) + 1);
                events.AddRange(applied.Events);
                state = applied.Next;
            }

            Assert.IsType<Outcome.Won>(rules.Evaluate(state));
            Assert.Equal(3, peak);
            Assert.Equal(
                new PuzzleEvent[]
                {
                    Exited(0, 0),
                    Exited(3, 1), new PuzzleEvent.CoverLifted(new PieceId(3), Blue),
                    Exited(4, 2), Cleared(Blue, 0, 1, 2),
                    Exited(2, 0),
                    Exited(8, 1),
                    Exited(5, 2), Cleared(Yellow, 0, 1, 2),
                    Exited(1, 0),
                    Exited(6, 1),
                    Exited(7, 2), new PuzzleEvent.CoverLifted(new PieceId(7), Red), Cleared(Red, 0, 1, 2),
                    new PuzzleEvent.Won(),
                },
                events);
        }

        [Fact]
        public void A_blocked_tap_is_rejected_with_the_first_blocker_and_changes_nothing()
        {
            var rules = new JamRules(Load(SixBySix()));

            MoveResult<LotState, ExitRejection> result = rules.TryApply(rules.Start, new Exit(new PieceId(5)));

            var illegal = Assert.IsType<MoveResult<LotState, ExitRejection>.Illegal>(result);
            Assert.Equal(new ExitRejection.Blocked(new Blocker.Piece(new PieceId(4))), illegal.Reason);
        }

        [Fact]
        public void Only_four_of_the_nine_vehicles_are_blocked_at_the_start()
        {
            var rules = new JamRules(Load(SixBySix()));
            var moves = new List<Exit>();

            rules.AppendLegalMoves(rules.Start, moves);

            Assert.Equal(new[] { 0, 1, 3, 4, 8 }, moves.ConvertAll(move => (int)move.Piece.Value));
        }

        [Fact]
        public void A_pillar_on_a_vehicles_path_is_rejected_at_load()
        {
            var rejected = Assert.IsType<LoadResult.Rejected>(LotLoader.Load(SixBySix(pillar: 5)));

            Assert.Equal(LoadProblem.PillarBlocksPath, rejected.Problem);
            Assert.Equal(Option.Some(new PieceId(5)), rejected.Piece);
        }

        [Fact]
        public void An_ahead_tag_on_a_clear_path_is_rejected_at_load()
        {
            LotDefinition board = SixBySix() with
            {
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Ahead(new PieceId(3), AheadRelation.Same)),
            };

            var rejected = Assert.IsType<LoadResult.Rejected>(LotLoader.Load(board));

            Assert.Equal(LoadProblem.AheadWithoutBlocker, rejected.Problem);
        }

        [Fact]
        public void An_ahead_tag_on_a_blocked_covered_vehicle_loads_and_keeps_its_start_reference()
        {
            LotDefinition board = SixBySix() with
            {
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Ahead(new PieceId(7), AheadRelation.Same)),
            };

            var loaded = Assert.IsType<LoadResult.Loaded>(LotLoader.Load(board));

            Assert.Equal(Option.Some(new PieceId(6)), loaded.Level.FirstOnPathAtStart(new PieceId(7)));
        }

        [Fact]
        public void Full_bays_fail_even_when_the_next_exit_would_complete_a_set()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red)));

            LotState state = Play(rules, 0, 1, 2, 3);

            Assert.Equal(new Outcome.Failed(FailKind.SlotsFull), rules.Evaluate(state));
            var illegal = Assert.IsType<MoveResult<LotState, ExitRejection>.Illegal>(rules.TryApply(state, new Exit(new PieceId(4))));
            Assert.IsType<ExitRejection.NotInPlay>(illegal.Reason);
        }

        [Fact]
        public void The_fourth_park_into_four_bays_reports_the_fail_event()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red)));
            LotState state = Play(rules, 0, 1, 2);

            var applied = Assert.IsType<MoveResult<LotState, ExitRejection>.Applied>(rules.TryApply(state, new Exit(new PieceId(3))));

            Assert.Equal(new PuzzleEvent.Failed(FailKind.SlotsFull), applied.Events[applied.Events.Count - 1]);
        }

        [Fact]
        public void One_spare_bay_reopens_a_full_row_and_the_set_can_then_clear()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Blue, Red, Red, Blue, Red)));
            LotState full = Play(rules, 0, 1, 2, 3);

            Assert.True(rules.TryWithOneSpare(full).TryGet(out LotState spared));
            LotState afterBlue = Play(rules, spared, 4);

            Assert.IsType<Outcome.InPlay>(rules.Evaluate(afterBlue));
            Assert.Equal(2, afterBlue.OccupiedSlots);
            Assert.Equal(Option.Some(Red), afterBlue.ColourIn(new SlotIndex(0)));
        }

        [Fact]
        public void Sets_clear_by_colour_count_whatever_the_bay_order()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Red, Blue, Blue, Red, Red)));
            LotState state = Play(rules, 0, 1, 2);

            var applied = Assert.IsType<MoveResult<LotState, ExitRejection>.Applied>(rules.TryApply(state, new Exit(new PieceId(3))));

            Assert.Contains(Cleared(Blue, 0, 2, 3), applied.Events);
            Assert.Equal(1, applied.Next.OccupiedSlots);
            Assert.Equal(Option.Some(Red), applied.Next.ColourIn(new SlotIndex(0)));
        }

        [Fact]
        public void Seven_bays_plus_the_spare_fit_the_eight_slot_packing_and_no_more()
        {
            var rules = new JamRules(Load(FreeCars(7, Blue, Red, Blue, Red, Blue, Red)));

            Assert.True(rules.TryWithOneSpare(rules.Start).TryGet(out LotState eight));
            Assert.Equal(8, eight.Capacity.Value);
            Assert.False(rules.TryWithOneSpare(eight).HasValue);
        }

        [Fact]
        public void The_same_exited_set_in_another_bay_order_shares_the_canonical_key_but_not_the_exact_key()
        {
            var rules = new JamRules(Load(FreeCars(5, Blue, Red, Blue, Red, Blue, Red)));

            LotState blueFirst = Play(rules, 0, 1);
            LotState redFirst = Play(rules, 1, 0);

            Assert.Equal(blueFirst.Canonical, redFirst.Canonical);
            Assert.NotEqual(blueFirst.Exact, redFirst.Exact);
            Assert.NotEqual(blueFirst, redFirst);
        }

        [Fact]
        public void A_vehicle_that_has_left_or_never_existed_is_not_in_the_lot()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Red, Blue, Red, Blue, Red)));
            LotState state = Play(rules, 0);

            Assert.IsType<ExitRejection.NotInLot>(Rejection(rules.TryApply(state, new Exit(new PieceId(0)))));
            Assert.IsType<ExitRejection.NotInLot>(Rejection(rules.TryApply(state, new Exit(new PieceId(9)))));
        }

        [Fact]
        public void Covers_lift_when_the_vehicle_leaves()
        {
            var rules = new JamRules(Load(SixBySix()));
            LotState state = rules.Start;

            Assert.True(rules.IsCovered(state, new PieceId(3)));
            state = Play(rules, state, 3);
            Assert.False(rules.IsCovered(state, new PieceId(3)));
            Assert.True(rules.IsCovered(state, new PieceId(7)));
        }

        private static LotState Play(JamRules rules, params int[] pieces) => Play(rules, rules.Start, pieces);

        private static LotState Play(JamRules rules, LotState state, params int[] pieces)
        {
            foreach (int piece in pieces)
            {
                state = Assert.IsType<MoveResult<LotState, ExitRejection>.Applied>(rules.TryApply(state, new Exit(new PieceId((byte)piece)))).Next;
            }

            return state;
        }

        private static ExitRejection Rejection(MoveResult<LotState, ExitRejection> result) =>
            Assert.IsType<MoveResult<LotState, ExitRejection>.Illegal>(result).Reason;

        private static int ParkedSlot(ReadOnlyArray<PuzzleEvent> events)
        {
            foreach (PuzzleEvent item in events)
            {
                if (item is PuzzleEvent.Exited exited)
                {
                    return exited.Slot.Value;
                }
            }

            return -1;
        }

        private static PuzzleEvent Exited(int piece, int slot) => new PuzzleEvent.Exited(new PieceId((byte)piece), new SlotIndex((byte)slot));

        private static PuzzleEvent Cleared(ColourId colour, params int[] slots)
        {
            var indices = new List<SlotIndex>();
            foreach (int slot in slots)
            {
                indices.Add(new SlotIndex((byte)slot));
            }

            return new PuzzleEvent.SetCleared(colour, ReadOnlyArray.CopyOf(indices));
        }
    }
}

using System;
using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The rewind limit each game supplies through its RewindFence (Sealed Vials review, 2026-11-08),
    /// checked through the shared Attempt and the replay validator. Covered Lot has no fence yet, so
    /// the fenced cases run the Jam rules with the AtReveal fence switched on.
    /// </summary>
    public sealed class RewindLimitTests
    {
        private static readonly StepResult<ExitRejection> OutOfRange = new StepResult<ExitRejection>.Refused(InvalidReason.RewindOutOfRange);

        [Fact]
        public void Covered_lot_has_no_rewind_fence()
        {
            Assert.Equal(RewindFence.None, new JamRules(Load(SixBySix())).RewindFence);
        }

        [Fact]
        public void Without_a_fence_the_limit_is_five_moves_capped_by_the_line()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new JamRules(level), level);
            Assert.Equal(new MoveCount(0), attempt.RewindLimit);

            // Each colour's three cars in a row clear together, so this line never fills the bays.
            int[] line = { 0, 3, 6, 1, 4, 7 };
            for (int played = 1; played <= line.Length; played++)
            {
                Play(attempt, line[played - 1]);
                Assert.Equal(new MoveCount(Math.Min(5, played)), attempt.RewindLimit);
            }
        }

        [Fact]
        public void Without_a_fence_a_rewind_may_go_back_past_a_lifted_cover_which_stays_lifted()
        {
            LotLevel level = FreeCarsWithCoversOn(1);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new JamRules(level), level);
            Play(attempt, 0, 1, 2, 4); // the bays fill: B R Y R

            Assert.Equal(new MoveCount(4), attempt.RewindLimit);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(3)));
            Assert.False(attempt.Current.HasExited(new PieceId(1)));
            Assert.True(attempt.Seen.Contains(new HiddenCell(1)));
        }

        [Fact]
        public void Under_the_reveal_fence_only_the_moves_since_the_last_reveal_can_be_rewound()
        {
            LotLevel level = FreeCarsWithCoversOn(1);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new FencedJamRules(level), level);
            Play(attempt, 0, 1); // piece 1 lifts its cover
            LotState afterReveal = attempt.Current;
            Play(attempt, 2, 4); // the bays fill: B R Y R
            Assert.IsType<Outcome.Failed>(attempt.Outcome);

            Assert.Equal(new MoveCount(2), attempt.RewindLimit);
            Assert.False(attempt.PeekRewind(new MoveCount(3)).HasValue);
            Assert.Equal(OutOfRange, attempt.Rewind(new MoveCount(3)));
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(2)));
            Assert.Equal(afterReveal, attempt.Current);
            Assert.Equal(new MoveCount(0), attempt.RewindLimit);
        }

        [Fact]
        public void Under_the_reveal_fence_a_reveal_on_the_failing_move_leaves_no_rewind()
        {
            LotLevel level = FreeCarsWithCoversOn(4);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new FencedJamRules(level), level);
            Play(attempt, 0, 1, 2, 4); // the last car lifts its cover and fills the bays

            Assert.IsType<Outcome.Failed>(attempt.Outcome);
            Assert.Equal(new MoveCount(0), attempt.RewindLimit);
            Assert.False(attempt.PeekRewind(new MoveCount(1)).HasValue);
            Assert.Equal(OutOfRange, attempt.Rewind(new MoveCount(1)));
        }

        [Fact]
        public void Under_the_reveal_fence_the_spare_does_not_move_the_fence_and_survives_a_rewind_behind_it()
        {
            LotLevel level = FreeCarsWithCoversOn(1);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new FencedJamRules(level), level);
            Play(attempt, 0, 1, 2, 4);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
            Play(attempt, 5); // five cars in five bays: full again
            Assert.IsType<Outcome.Failed>(attempt.Outcome);

            Assert.Equal(new MoveCount(3), attempt.RewindLimit);
            Assert.Equal(OutOfRange, attempt.Rewind(new MoveCount(4)));
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(3)));
            Assert.Equal(5, attempt.Current.Capacity.Value);
            Assert.True(attempt.Current.HasExited(new PieceId(1)));
        }

        [Fact]
        public void Under_the_reveal_fence_the_validator_refuses_a_recorded_rewind_across_a_reveal()
        {
            LotLevel level = FreeCarsWithCoversOn(1);
            var rules = new FencedJamRules(level);
            var fail = new List<RecordAction<Exit>> { MoveOf(0), MoveOf(1), MoveOf(2), MoveOf(4) };

            ReplayReport across = Replay.Run(rules, LotState.Start(level), PlayMode.Campaign, ReadOnlyArray.CopyOf(new List<RecordAction<Exit>>(fail) { RewindOf(3) }));
            ReplayReport within = Replay.Run(rules, LotState.Start(level), PlayMode.Campaign, ReadOnlyArray.CopyOf(new List<RecordAction<Exit>>(fail) { RewindOf(2) }));

            Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(4), InvalidReason.RewindOutOfRange), across.Verdict);
            Assert.Equal(new ReplayVerdict.Valid(new Outcome.InPlay(), new MoveCount(2), SpareUsed: false), within.Verdict);
        }

        [Fact]
        public void Under_the_reveal_fence_the_limit_counts_from_the_latest_reveal()
        {
            LotLevel level = FreeCarsWithCoversOn(0, 2);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new FencedJamRules(level), level);
            Play(attempt, 0, 1, 2, 4); // pieces 0 and 2 lift their covers; the bays fill: B R Y R

            Assert.Equal(new MoveCount(1), attempt.RewindLimit);
            Assert.False(attempt.PeekRewind(new MoveCount(2)).HasValue);
            Assert.Equal(OutOfRange, attempt.Rewind(new MoveCount(2)));
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(1)));
            Assert.True(attempt.Current.HasExited(new PieceId(2)));
        }

        [Fact]
        public void Under_the_reveal_fence_insight_does_not_move_the_fence()
        {
            LotLevel level = FreeCarsWithCoversOn(8);
            var rules = new FencedJamRules(level);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(rules, level);
            Play(attempt, 0, 1);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(8)));
            Play(attempt, 2, 4); // the bays fill and no cover lifts

            Assert.Equal(new MoveCount(4), attempt.RewindLimit);
            ReplayReport report = Replay.Run(rules, LotState.Start(level), PlayMode.Campaign,
                ReadOnlyArray.Of(MoveOf(0), MoveOf(1), InsightOn(8), MoveOf(2), MoveOf(4), RewindOf(3)));
            Assert.Equal(new ReplayVerdict.Valid(new Outcome.InPlay(), new MoveCount(1), SpareUsed: false), report.Verdict);
        }

        [Fact]
        public void Under_the_reveal_fence_a_move_that_lifts_a_cover_already_shown_by_insight_still_moves_the_fence()
        {
            LotLevel level = FreeCarsWithCoversOn(1);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new FencedJamRules(level), level);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(1)));
            Play(attempt, 0, 1, 2, 4);

            Assert.Equal(new MoveCount(2), attempt.RewindLimit);
        }

        [Fact]
        public void The_five_move_cap_alone_refuses_a_longer_rewind_on_a_long_line()
        {
            // The blues clear (0, 3, 6), then R Y R Y fill the bays at move 7, so the line holds six moves to rewind.
            LotLevel plain = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var rules = new JamRules(plain);
            var line = new List<RecordAction<Exit>> { MoveOf(0), MoveOf(3), MoveOf(6), MoveOf(1), MoveOf(2), MoveOf(4), MoveOf(5) };

            ReplayReport six = Replay.Run(rules, rules.Start, PlayMode.Campaign, ReadOnlyArray.CopyOf(new List<RecordAction<Exit>>(line) { RewindOf(6) }));
            ReplayReport five = Replay.Run(rules, rules.Start, PlayMode.Campaign, ReadOnlyArray.CopyOf(new List<RecordAction<Exit>>(line) { RewindOf(5) }));

            Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(7), InvalidReason.RewindOutOfRange), six.Verdict);
            Assert.Equal(new ReplayVerdict.Valid(new Outcome.InPlay(), new MoveCount(2), SpareUsed: false), five.Verdict);

            // Under the fence, piece 0 lifts its cover on the first move, so six moves follow the reveal and the cap decides.
            LotLevel covered = FreeCarsWithCoversOn(0);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(new FencedJamRules(covered), covered);
            Play(attempt, 0, 3, 6, 1, 2, 4, 5);
            Assert.IsType<Outcome.Failed>(attempt.Outcome);
            Assert.Equal(new MoveCount(5), attempt.RewindLimit);
            Assert.False(attempt.PeekRewind(new MoveCount(6)).HasValue);
            Assert.Equal(OutOfRange, attempt.Rewind(new MoveCount(6)));
        }

        [Fact]
        public void Under_the_reveal_fence_no_accepted_rewind_undoes_a_reveal()
        {
            // Property over seeded random lines on the 6×6 board (covers on pieces 3 and 7), with up to
            // four fails per attempt, each followed by a random allowed rewind.
            LotLevel level = Load(SixBySix());
            var rules = new FencedJamRules(level);
            int rewinds = 0;
            int fenceBites = 0;
            int rewindsAfterALift = 0;
            for (int run = 0; run < 200; run++)
            {
                Pcg32 random = Pcg32.Seeded((ulong)run, 11UL);
                Attempt<LotState, Exit, ExitRejection> attempt = Begin(rules, level);
                var keys = new List<ExactKey> { attempt.Current.Exact };
                var revealed = new List<bool> { false }; // revealed[k]: move k lifted a cover; entry 0 is the start
                int fails = 0;
                while (fails < 4 && !(attempt.Outcome is Outcome.Won))
                {
                    if (attempt.Outcome is Outcome.InPlay)
                    {
                        var moves = new List<Exit>();
                        rules.AppendLegalMoves(attempt.Current, moves);
                        var done = Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(moves[random.NextIndex(moves.Count)]));
                        keys.Add(attempt.Current.Exact);
                        revealed.Add(LiftsACover(done.Events));
                        continue;
                    }

                    fails++;
                    int limit = attempt.RewindLimit.Value;
                    int lastReveal = revealed.LastIndexOf(true);
                    int played = revealed.Count - 1;
                    Assert.Equal(Math.Min(5, played - Math.Max(lastReveal, 0)), limit);
                    fenceBites += limit < Math.Min(5, played) ? 1 : 0;
                    Assert.Equal(OutOfRange, attempt.Rewind(new MoveCount(limit + 1)));
                    if (limit == 0)
                    {
                        break;
                    }

                    ulong seenBefore = attempt.Seen.Mask;
                    List<PieceId> liftedAndGone = CoveredCarsGone(level, attempt.Current);
                    int back = 1 + random.NextIndex(limit);
                    Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(back)));
                    foreach (PieceId piece in liftedAndGone)
                    {
                        Assert.True(attempt.Current.HasExited(piece), "A rewind brought back a car whose cover had lifted.");
                    }

                    keys.RemoveRange(keys.Count - back, back);
                    revealed.RemoveRange(revealed.Count - back, back);
                    Assert.Equal(keys[keys.Count - 1], attempt.Current.Exact);
                    Assert.Equal(seenBefore, attempt.Seen.Mask);
                    rewinds++;
                    rewindsAfterALift += liftedAndGone.Count > 0 ? 1 : 0;
                }
            }

            Assert.True(rewinds > 0, "The property never rewound.");
            Assert.True(fenceBites > 0, "The fence never shortened a rewind.");
            Assert.True(rewindsAfterALift > 0, "No rewind came after a cover had lifted, so the state check never ran.");
        }

        /// <summary>A free-cars board (B R Y three times, four bays) with these cars under covers.</summary>
        private static LotLevel FreeCarsWithCoversOn(params int[] pieces)
        {
            LotDefinition plain = FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow);
            var vehicles = new List<VehicleSpec>(plain.Vehicles);
            foreach (int piece in pieces)
            {
                vehicles[piece] = vehicles[piece] with { StartsCovered = true };
            }

            return Load(plain with { Vehicles = ReadOnlyArray.CopyOf(vehicles) });
        }

        /// <summary>The covered cars that have left the lot in this state, so their covers have lifted.</summary>
        private static List<PieceId> CoveredCarsGone(LotLevel level, LotState state)
        {
            var gone = new List<PieceId>();
            foreach (Vehicle vehicle in level.Vehicles)
            {
                if (vehicle.StartsCovered && state.HasExited(vehicle.Id))
                {
                    gone.Add(vehicle.Id);
                }
            }

            return gone;
        }

        private static Attempt<LotState, Exit, ExitRejection> Begin(IPuzzleRules<LotState, Exit, ExitRejection> rules, LotLevel level) =>
            new(rules, LotState.Start(level), PlayMode.Campaign);

        private static void Play(Attempt<LotState, Exit, ExitRejection> attempt, params int[] pieces)
        {
            foreach (int piece in pieces)
            {
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(new Exit(new PieceId((byte)piece))));
            }
        }

        private static bool LiftsACover(ReadOnlyArray<PuzzleEvent> events)
        {
            foreach (PuzzleEvent item in events)
            {
                if (item is PuzzleEvent.CoverLifted)
                {
                    return true;
                }
            }

            return false;
        }

        private static RecordAction<Exit> MoveOf(int piece) => new RecordAction<Exit>.Move(new Exit(new PieceId((byte)piece)), new ElapsedMs(500));

        private static RecordAction<Exit> RewindOf(int moves) => new RecordAction<Exit>.Rewind(new MoveCount(moves), new ElapsedMs(500));

        private static RecordAction<Exit> InsightOn(int cell) => new RecordAction<Exit>.UseInsight(new HiddenCell((byte)cell), new ElapsedMs(500));

        /// <summary>The Jam rules with the AtReveal fence switched on: Covered Lot as it would play if it adopted the fence.</summary>
        private sealed class FencedJamRules : IPuzzleRules<LotState, Exit, ExitRejection>
        {
            private readonly JamRules _jam;

            public FencedJamRules(LotLevel level) => _jam = new JamRules(level);

            public RewindFence RewindFence => RewindFence.AtReveal;

            public void AppendLegalMoves(in LotState state, List<Exit> into) => _jam.AppendLegalMoves(state, into);

            public MoveResult<LotState, ExitRejection> TryApply(in LotState state, Exit move) => _jam.TryApply(state, move);

            public Outcome Evaluate(in LotState state) => _jam.Evaluate(state);

            public Option<LotState> TryWithOneSpare(in LotState state) => _jam.TryWithOneSpare(state);

            public CapacityCount CapacityOf(in LotState state) => _jam.CapacityOf(state);
        }
    }
}

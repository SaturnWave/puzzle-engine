using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Bot;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>
    /// The shared bot runner on toy rules (shared-design.md §2, TC23): how a run ends, the mistake roll and its draws, and
    /// the near miss only after a fail. Jam's runs never reach Cycle, StepCap or NoLegalMove, so the toy game covers them.
    /// </summary>
    public sealed class BotRunnerTests
    {
        private static readonly ToyState Start = new(0);
        private static readonly MoveCount RoomyCap = new(50);

        [Fact]
        public void A_run_plays_until_the_attempt_is_won_and_records_each_move()
        {
            var policy = ScriptedPolicy.First();
            Pcg32 random = Pcg32.Seeded(1, 1);
            Pcg32State before = random.Snapshot();

            var run = BotRunner.Run(ToyRules.CountToThree(), Start, Profile(0), policy, random, RoomyCap);

            Assert.Equal(BotEndReason.Won, run.EndReason);
            Assert.Equal(Enumerable.Repeat(Intended(1), 3), run.Steps);
            Assert.Equal(new MoveCount(3), run.Attempt.Moves);
            Assert.Equal(3, policy.Observed);
            Assert.Equal(before, random.Snapshot());
            Assert.False(run.Attempt.SpareUsed || run.Attempt.InsightUsed || run.Attempt.Conceded);
        }

        [Theory]
        [InlineData(FailKind.SlotsFull, BotEndReason.SlotsFull)]
        [InlineData(FailKind.NoLegalMove, BotEndReason.NoLegalMove)]
        public void A_failed_attempt_ends_the_run_with_the_reason_of_the_same_name(FailKind kind, BotEndReason reason)
        {
            var rules = new ToyRules(value => new[] { 1 }, value => value >= 2 ? new Outcome.Failed(kind) : ToyRules.InPlay);

            var run = BotRunner.Run(rules, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap);

            Assert.Equal(reason, run.EndReason);
            Assert.Equal(new MoveCount(2), run.Attempt.Moves);
        }

        [Fact]
        public void A_position_that_comes_back_ends_the_run_as_a_cycle()
        {
            // 0 -> 1 -> 0: the start comes back on the second move.
            var rules = new ToyRules(value => value == 0 ? new[] { 1 } : new[] { -1 }, value => ToyRules.InPlay);

            var run = BotRunner.Run(rules, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap);

            Assert.Equal(BotEndReason.Cycle, run.EndReason);
            Assert.Equal(2, run.Steps.Count);
        }

        [Fact]
        public void A_run_that_never_ends_or_repeats_stops_after_exactly_its_move_cap()
        {
            var rules = new ToyRules(value => new[] { 1 }, value => ToyRules.InPlay);

            var run = BotRunner.Run(rules, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), new MoveCount(5));

            Assert.Equal(BotEndReason.StepCap, run.EndReason);
            Assert.Equal(5, run.Steps.Count);
        }

        [Fact]
        public void A_win_on_the_move_that_reaches_the_cap_is_a_win()
        {
            var rules = new ToyRules(value => new[] { 1 }, value => value >= 5 ? ToyRules.Won : ToyRules.InPlay);

            var run = BotRunner.Run(rules, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), new MoveCount(5));

            Assert.Equal(BotEndReason.Won, run.EndReason);
        }

        [Fact]
        public void At_a_rate_of_1000_the_bot_never_plays_the_move_it_intended()
        {
            var rules = new ToyRules(value => new[] { 1, 2, 3 }, value => ToyRules.InPlay);

            var run = BotRunner.Run(rules, Start, Profile(1000), ScriptedPolicy.Intending(1), Pcg32.Seeded(5, 9), new MoveCount(30));

            Assert.Equal(30, run.Steps.Count);
            Assert.All(run.Steps, step => Assert.True(step.WasMistake && step.Move.Step != 1));
            Assert.Contains(run.Steps, step => step.Move.Step == 2);
            Assert.Contains(run.Steps, step => step.Move.Step == 3);
        }

        [Fact]
        public void The_policy_draws_first_then_the_mistake_roll_then_the_mistaken_move()
        {
            var rules = new ToyRules(value => new[] { 1, 2, 3 }, value => ToyRules.InPlay);
            var policy = new ScriptedPolicy((legal, random) => legal[random.NextIndex(legal.Count)]);

            var run = BotRunner.Run(rules, Start, Profile(500), policy, Pcg32.Seeded(11, 3), new MoveCount(40));

            Pcg32 replay = Pcg32.Seeded(11, 3);
            var expected = new List<BotStep<ToyMove>>();
            for (int move = 0; move < 40; move++)
            {
                int intended = 1 + replay.NextIndex(3);
                bool isMistake = replay.NextBelow(1000) < 500;
                int[] others = new[] { 1, 2, 3 }.Where(step => step != intended).ToArray();
                int played = isMistake ? others[replay.NextIndex(others.Length)] : intended;
                expected.Add(new BotStep<ToyMove>(new ToyMove(played), isMistake));
            }

            Assert.Equal(expected, run.Steps);
            Assert.Contains(run.Steps, step => step.WasMistake);
            Assert.Contains(run.Steps, step => !step.WasMistake);
        }

        [Fact]
        public void A_mistake_is_a_roll_below_the_rate()
        {
            // The first roll on this stream is read from an independent copy: a rate equal to it makes no mistake on the
            // first move, a rate one above it does. Any move wins, so each run has one move.
            int roll = (int)Pcg32.Seeded(21, 4).NextBelow(1000);
            Assert.InRange(roll, 1, 998);
            var rules = new ToyRules(value => new[] { 1, 2, 3 }, value => value >= 1 ? ToyRules.Won : ToyRules.InPlay);

            var atRoll = BotRunner.Run(rules, Start, Profile(roll), ScriptedPolicy.Intending(1), Pcg32.Seeded(21, 4), RoomyCap);
            var aboveRoll = BotRunner.Run(rules, Start, Profile(roll + 1), ScriptedPolicy.Intending(1), Pcg32.Seeded(21, 4), RoomyCap);

            Assert.Equal(Intended(1), Assert.Single(atRoll.Steps));
            Assert.True(Assert.Single(aboveRoll.Steps).WasMistake);
        }

        [Fact]
        public void With_two_legal_moves_a_mistake_plays_the_other_one_without_another_draw()
        {
            var rules = new ToyRules(value => new[] { 1, 2 }, value => ToyRules.InPlay);
            Pcg32 random = Pcg32.Seeded(7, 7);

            var run = BotRunner.Run(rules, Start, Profile(1000), ScriptedPolicy.Intending(1), random, new MoveCount(10));

            Pcg32 replay = Pcg32.Seeded(7, 7);
            for (int move = 0; move < 10; move++)
            {
                replay.NextBelow(1000);
            }

            Assert.Equal(replay.Snapshot(), random.Snapshot());
            Assert.All(run.Steps, step => Assert.Equal(Mistaken(2), step));
        }

        [Fact]
        public void At_a_rate_of_0_and_on_forced_moves_the_runner_draws_nothing()
        {
            Pcg32 atZero = Pcg32.Seeded(3, 3);
            Pcg32 forced = Pcg32.Seeded(3, 3);
            Pcg32State before = atZero.Snapshot();
            var choices = new ToyRules(value => new[] { 1, 2, 3 }, value => value >= 9 ? ToyRules.Won : ToyRules.InPlay);

            BotRunner.Run(choices, Start, Profile(0), ScriptedPolicy.Intending(1), atZero, RoomyCap);
            var forcedRun = BotRunner.Run(ToyRules.CountToThree(), Start, Profile(1000), ScriptedPolicy.First(), forced, RoomyCap);

            Assert.Equal(before, atZero.Snapshot());
            Assert.Equal(before, forced.Snapshot());
            Assert.All(forcedRun.Steps, step => Assert.False(step.WasMistake));
        }

        [Fact]
        public void An_illegal_intention_no_legal_move_in_play_or_a_refused_legal_move_is_an_engine_fault()
        {
            var noMoves = new ToyRules(value => Array.Empty<int>(), value => ToyRules.InPlay);

            Assert.Throws<InvalidOperationException>(() => BotRunner.Run(ToyRules.CountToThree(), Start, Profile(0), ScriptedPolicy.Intending(5), Pcg32.Seeded(1, 1), RoomyCap));
            Assert.Throws<InvalidOperationException>(() => BotRunner.Run(noMoves, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap));
            Assert.Throws<InvalidOperationException>(() => BotRunner.Run(new RefusingRules(), Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap));
        }

        [Fact]
        public void A_near_miss_is_solved_only_for_a_run_that_ended_on_failed()
        {
            var solver = new CountingSolver();
            var budget = new NodeBudget(1000);
            var failing = new ToyRules(value => new[] { 1 }, value => value >= 2 ? new Outcome.Failed(FailKind.SlotsFull) : ToyRules.InPlay);
            var cycling = new ToyRules(value => value == 0 ? new[] { 1 } : new[] { -1 }, value => ToyRules.InPlay);

            var won = BotRunner.Run(ToyRules.CountToThree(), Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap);
            var cycle = BotRunner.Run(cycling, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap);
            Assert.False(won.NearMissWith(solver, budget).HasValue);
            Assert.False(cycle.NearMissWith(solver, budget).HasValue);
            Assert.Equal(0, solver.Calls);

            var failed = BotRunner.Run(failing, Start, Profile(0), ScriptedPolicy.First(), Pcg32.Seeded(1, 1), RoomyCap);
            Assert.Equal(Option.Some<NearMiss>(new NearMiss.No()), failed.NearMissWith(solver, budget));
            Assert.Equal(1, solver.Calls);
        }

        [Fact]
        public void A_run_stream_is_the_pack_seeded_bot_stream_under_its_fixed_key()
        {
            PackId pack = PackId.Parse("campaign-jam").Or(default);
            var profile = new BotProfile(BotProfileId.Planner3, new BotSearch.Limited(new MoveCount(3), DeductionRung.D2), new PerMille(50));

            Pcg32 stream = BotStreams.For(pack, new LevelIndex(24), profile, 17);

            Pcg32 expected = SeedDerivation.Stream(Seed.ForPack(pack), StreamKey.ForBot(pack, new LevelIndex(24), "Planner3", 17));
            Assert.Equal(expected.NextUInt32(), stream.NextUInt32());
        }

        [Fact]
        public void A_step_hashes_any_move_whatever_its_hash()
        {
            // A move's hash may be any int; mixing it must wrap rather than throw under the checked build.
            var steps = new[] { int.MaxValue, int.MinValue, 1 << 30, -(1 << 30) - 1 }
                .SelectMany(hash => new[] { new BotStep<ToyMove>(new ToyMove(hash), wasMistake: false), new BotStep<ToyMove>(new ToyMove(hash), wasMistake: true) })
                .ToList();

            var set = new HashSet<BotStep<ToyMove>>(steps);

            Assert.Equal(steps.Count, set.Count);
            Assert.Equal(unchecked(int.MaxValue * 2), Intended(int.MaxValue).GetHashCode());
        }

        private static BotProfile Profile(int mistakes) => new(BotProfileId.Greedy, new BotSearch.Habit(), new PerMille(mistakes));

        private static BotStep<ToyMove> Intended(int step) => new(new ToyMove(step), wasMistake: false);

        private static BotStep<ToyMove> Mistaken(int step) => new(new ToyMove(step), wasMistake: true);
    }
}

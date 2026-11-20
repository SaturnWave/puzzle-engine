using System;
using PuzzleCore.Play;
using PuzzleCore.Rules;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    /// <summary>The shared endings of a scripted run (shared-design.md §2): a repeated position, then the move cap.</summary>
    public sealed class RunGuardTests
    {
        private static readonly ExactKey Start = new(0UL, 0UL);

        [Fact]
        public void A_position_seen_before_in_the_run_is_a_cycle_the_start_included()
        {
            var guard = new RunGuard(Start, new MoveCount(10));

            Assert.Equal(Option.None<RunStop>(), guard.After(Key(1)));
            Assert.Equal(Option.None<RunStop>(), guard.After(Key(2)));
            Assert.Equal(Option.Some(RunStop.Cycle), guard.After(Start));
        }

        [Fact]
        public void A_run_stops_when_its_moves_reach_the_cap()
        {
            var guard = new RunGuard(Start, new MoveCount(3));

            Assert.Equal(Option.None<RunStop>(), guard.After(Key(1)));
            Assert.Equal(Option.None<RunStop>(), guard.After(Key(2)));
            Assert.Equal(Option.Some(RunStop.StepCap), guard.After(Key(3)));
        }

        [Fact]
        public void A_repeat_on_the_capped_move_is_reported_as_a_cycle()
        {
            var guard = new RunGuard(Start, new MoveCount(2));
            guard.After(Key(1));

            Assert.Equal(Option.Some(RunStop.Cycle), guard.After(Key(1)));
        }

        [Fact]
        public void A_cap_below_one_move_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunGuard(Start, new MoveCount(0)));
        }

        private static ExactKey Key(ulong low) => new(0UL, low);
    }
}

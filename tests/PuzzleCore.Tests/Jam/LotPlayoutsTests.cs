using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    public sealed class LotPlayoutsTests
    {
        private static readonly PackId Pack = PackId.Parse("playout-test").Or(default);

        [Fact]
        public void Random_play_never_fails_when_one_colour_can_never_crowd_the_bays()
        {
            // Three blue cars and seven bays: no order can fill the row.
            LotLevel level = Load(FreeCars(7, Blue, Blue, Blue));

            Assert.Equal(new PerMille(0), LotPlayouts.DeadEnd(level, Pack, new LevelIndex(0), 200));
        }

        [Fact]
        public void Random_play_sometimes_fails_on_four_bays_with_three_mixed_colours()
        {
            // Nine free cars, three colours, four bays: three different colours plus a fourth fill the row.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));

            PerMille deadEnd = LotPlayouts.DeadEnd(level, Pack, new LevelIndex(0), 400);

            Assert.InRange(deadEnd.Value, 100, 999);
        }

        [Fact]
        public void The_greedy_habit_always_wins_free_cars_by_finishing_each_colour_it_started()
        {
            // Greedy keeps tapping the colour already waiting, so with every car free it never mixes three colours.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));

            Assert.Equal(new PerMille(1000), LotPlayouts.GreedyWins(level, Pack, new LevelIndex(0), 200));
        }

        [Fact]
        public void The_greedy_grade_is_deterministic()
        {
            LotLevel level = Load(SixBySix());

            Assert.Equal(LotPlayouts.GreedyWins(level, Pack, new LevelIndex(5), 200), LotPlayouts.GreedyWins(level, Pack, new LevelIndex(5), 200));
        }

        [Fact]
        public void The_dead_end_grade_is_deterministic()
        {
            LotLevel level = Load(SixBySix());

            Assert.Equal(LotPlayouts.DeadEnd(level, Pack, new LevelIndex(3), 300), LotPlayouts.DeadEnd(level, Pack, new LevelIndex(3), 300));
        }
    }
}

using System;
using System.Linq;
using PuzzleCore.Jam;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>The Jam-only CSV features (Q62, Q63), worked out by hand on boards small enough to count.</summary>
    public sealed class LotFeaturesTests
    {
        [Fact]
        public void Branching_counts_every_free_vehicle_before_each_move_the_start_included()
        {
            // Nine free cars: 9, 8, ..., 1 can leave before the nine moves, 45 in all.
            BuiltLevel built = Along(Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow)), 0, 3, 6, 1, 4, 7, 2, 5, 8);

            Assert.Equal(new Thousandths(5000), LotFeatures.Branching(built));
        }

        [Fact]
        public void Branching_counts_a_vehicle_only_once_its_path_is_clear_and_rounds_down()
        {
            // 7, 7, 7, 6, 5, 4, 3, 2, 1: pieces 5 and 6 come free as the yellows in front of them leave. 42 / 9 = 4.666...
            BuiltLevel built = Along(Load(ThirdsBehindYellows()), 4, 7, 8, 0, 2, 5, 1, 3, 6);

            Assert.Equal(new Thousandths(4666), LotFeatures.Branching(built));
        }

        [Fact]
        public void Branching_counts_covered_vehicles_like_open_ones()
        {
            BuiltLevel built = Along(FreeCarsWithCoversOn(1, 4), 0, 3, 6, 1, 4, 7, 2, 5, 8);

            Assert.Equal(new Thousandths(5000), LotFeatures.Branching(built));
        }

        [Fact]
        public void Branching_refuses_a_par_path_that_is_illegal_or_does_not_win()
        {
            LotLevel level = Load(ThirdsBehindYellows());

            // Piece 5 still waits behind piece 4.
            Assert.Throws<ArgumentException>(() => LotFeatures.Branching(Along(level, 5, 4, 7, 8, 0, 2, 1, 3, 6)));

            // Three colours and a fourth car fill the four bays.
            Assert.Throws<ArgumentException>(() => LotFeatures.Branching(Along(level, 0, 1, 4, 2, 3, 7, 8, 5, 6)));

            // Eight of the nine cars.
            Assert.Throws<ArgumentException>(() => LotFeatures.Branching(Along(level, 4, 7, 8, 0, 2, 5, 1, 3)));
        }

        [Fact]
        public void Buried_third_takes_the_third_shallowest_vehicle_of_each_colour()
        {
            // Blue depths 0, 0, 1 and red 0, 0, 1; yellow 0, 0, 0. (1 + 1 + 0) / 3 = 0.666...
            Assert.Equal(new Thousandths(666), LotFeatures.BuriedThird(Load(ThirdsBehindYellows())));
        }

        [Fact]
        public void Exit_depth_counts_every_vehicle_on_the_path_and_those_behind_them()
        {
            // Blue piece 0 waits for pieces 1 and 2, and piece 1 for piece 3: depth 3. Blues 3, 1, 0; reds 0, 0, 0.
            Assert.Equal(new Thousandths(1500), LotFeatures.BuriedThird(Load(BuriedBehindTwo())));
        }

        [Fact]
        public void A_colour_of_two_sets_contributes_its_3rd_and_6th_shallowest_vehicles()
        {
            // Depths 3, 2, 1, 0, 0, 0 rank as 0, 0, 0, 1, 2, 3: the sets end at 0 and 3.
            Assert.Equal(new Thousandths(1500), LotFeatures.BuriedThird(Load(SixBluesInAChain())));
        }

        [Fact]
        public void A_colour_of_two_sets_counts_only_its_3rd_and_6th_shallowest_vehicles()
        {
            // Depths 2, 1, 0, 0, 0, 0 rank as 0, 0, 0, 0, 1, 2: the sets end at 0 and 2, so the mean is 1. The 4th and
            // 5th cars, at 0 and 1, count for nothing.
            Assert.Equal(new Thousandths(1000), LotFeatures.BuriedThird(Load(SixBluesInAShortChain())));
        }

        [Fact]
        public void Every_colour_up_to_the_highest_colour_id_counts()
        {
            // ThirdsBehindYellows with its yellows in colour 15, the highest: still (1 + 1 + 0) / 3.
            LotDefinition board = Repainted(ThirdsBehindYellows(), from: Yellow, to: new ColourId(ColourId.MaxValue));

            Assert.Equal(new Thousandths(666), LotFeatures.BuriedThird(Load(board)));
        }

        [Fact]
        public void A_lot_where_every_vehicle_can_leave_at_once_buries_nothing()
        {
            Assert.Equal(new Thousandths(0), LotFeatures.BuriedThird(Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow))));
        }

        [Fact]
        public void A_mean_in_thousandths_rounds_down_and_is_never_negative()
        {
            Assert.Equal(new Thousandths(333), Thousandths.MeanOf(new[] { 1, 0, 0 }));
            Assert.Equal(new Thousandths(6666), Thousandths.MeanOf(new[] { 7, 7, 6 }));
            Assert.Equal(new Thousandths(24000), Thousandths.MeanOf(new[] { 48, 0 }));
            Assert.Throws<ArgumentException>(() => Thousandths.MeanOf(Array.Empty<int>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => Thousandths.MeanOf(new[] { -1, 2, 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => Thousandths.MeanOf(new[] { 2, -1, 2 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Thousandths(-1));
            Assert.Equal("5380", new Thousandths(5380).ToString());
        }

        [Fact]
        public void A_mean_keeps_its_sum_in_64_bits()
        {
            // 1,001 counts of 2,147,483 sum past int.MaxValue, yet their mean, 2,147,483,000 thousandths, fits an int.
            Assert.Equal(new Thousandths(2_147_483_000), Thousandths.MeanOf(Enumerable.Repeat(2_147_483, 1001).ToArray()));
        }

        /// <summary>A built level whose par path is the given order; the features read only the level and the path.</summary>
        private static BuiltLevel Along(LotLevel level, params int[] order) => new(
            level,
            new CapacityCount(3),
            ReadOnlyArray.CopyOf(order.Select(piece => new Exit(new PieceId((byte)piece)))),
            Option.None<DeductionGrade.Graded>());
    }
}

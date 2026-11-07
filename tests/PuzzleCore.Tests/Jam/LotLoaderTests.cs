using PuzzleCore.Jam;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    public sealed class LotLoaderTests
    {
        [Fact]
        public void The_six_by_six_board_loads_with_its_covers_and_clue()
        {
            var loaded = Assert.IsType<LoadResult.Loaded>(LotLoader.Load(SixBySix()));

            Assert.Equal(9, loaded.Level.VehicleCount);
            Assert.True(loaded.Level.IsCoveredLevel);
            Assert.Equal((1U << 3) | (1U << 7), loaded.Level.CoveredAtStartMask);
        }

        [Fact]
        public void Two_cars_facing_each_other_in_one_row_form_a_blocking_cycle()
        {
            LotDefinition board = FreeCars(4, Red, Red, Red) with
            {
                Vehicles = ReadOnlyArray.Of(Car(Red, 0, Facing.East), Car(Red, 3, Facing.West), Car(Red, 20, Facing.West)),
            };

            Assert.Equal(LoadProblem.BlockingCycle, Problem(board));
        }

        [Fact]
        public void Overlapping_vehicles_and_vehicles_on_pillars_are_rejected()
        {
            LotDefinition overlap = FreeCars(4, Red, Red, Red) with
            {
                Vehicles = ReadOnlyArray.Of(Car(Red, 0, Facing.South), Car(Red, 5, Facing.West), Car(Red, 20, Facing.West)),
            };
            LotDefinition onPillar = FreeCars(4, Red, Red, Red) with { Pillars = ReadOnlyArray.Of(1) };

            Assert.Equal(LoadProblem.PiecesOverlap, Problem(overlap));
            Assert.Equal(LoadProblem.PiecesOverlap, Problem(onPillar));
        }

        [Fact]
        public void A_vehicle_running_off_the_lot_is_rejected()
        {
            LotDefinition board = FreeCars(4, Red, Red, Red) with
            {
                Vehicles = ReadOnlyArray.Of(Car(Red, 4, Facing.East), Car(Red, 10, Facing.West), Car(Red, 20, Facing.West)),
            };

            Assert.Equal(LoadProblem.PieceOutOfLot, Problem(board));
        }

        [Fact]
        public void Vehicles_must_be_listed_in_ascending_anchor_order()
        {
            LotDefinition board = FreeCars(4, Red, Red, Red) with
            {
                Vehicles = ReadOnlyArray.Of(Car(Red, 5, Facing.West), Car(Red, 0, Facing.West), Car(Red, 20, Facing.West)),
            };

            Assert.Equal(LoadProblem.PiecesNotInAnchorOrder, Problem(board));
        }

        [Fact]
        public void Colour_totals_must_be_multiples_of_three()
        {
            Assert.Equal(LoadProblem.ColourTotalNotMultipleOfThree, Problem(FreeCars(4, Red, Red, Blue)));
        }

        [Theory]
        [InlineData(3)]
        [InlineData(8)]
        public void A_level_starts_with_four_to_seven_bays(int bays)
        {
            Assert.Equal(LoadProblem.BayCountOutOfRange, Problem(FreeCars(bays, Red, Red, Red)));
        }

        [Fact]
        public void A_lot_side_outside_five_to_eight_is_rejected()
        {
            Assert.Equal(LoadProblem.SizeOutOfRange, Problem(FreeCars(4, Red, Red, Red) with { Width = 9 }));
        }

        [Fact]
        public void A_clue_on_an_open_vehicle_is_rejected()
        {
            LotDefinition board = SixBySix() with
            {
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(0), Red)),
            };

            Assert.Equal(LoadProblem.ClueOnOpenVehicle, Problem(board));
        }

        private static LoadProblem Problem(LotDefinition board) =>
            Assert.IsType<LoadResult.Rejected>(LotLoader.Load(board)).Problem;
    }
}

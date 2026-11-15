using System;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Search;
using Xunit;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>Each grade field of a built campaign level (shared-design.md §2, "Grade fields"; Q64).</summary>
    public sealed class LotGradesTests
    {
        private static readonly PackId Pack = PackId.Parse("grades-test").Or(default);

        [Theory]
        [InlineData(0)]
        [InlineData(17)]
        public void A_plain_level_is_a_plan_level_without_a_deduction_grade(int slot)
        {
            var index = new LevelIndex(slot);
            BuiltLevel built = Build(index);

            LevelGrade grade = LotGrades.Of(built, Pack, index);

            Assert.False(built.Level.IsCoveredLevel);
            Assert.Equal(LevelKind.Plan, grade.Kind);
            Assert.False(grade.Deduction.HasValue);
            AssertPlanFields(built, index, grade.Plan);
        }

        [Fact]
        public void A_covered_level_is_a_think_level_with_its_deduction_grade()
        {
            var index = new LevelIndex(24);
            BuiltLevel built = Build(index);

            LevelGrade grade = LotGrades.Of(built, Pack, index);

            Assert.True(built.Level.IsCoveredLevel);
            Assert.Equal(LevelKind.Think, grade.Kind);
            Assert.Equal(built.Deduction, grade.Deduction);
            AssertPlanFields(built, index, grade.Plan);
        }

        /// <summary>The kind follows the covers, not the deduction grade, so a mismatch is refused, never graded (Q64).</summary>
        [Fact]
        public void A_built_level_whose_covers_and_deduction_grade_disagree_is_refused()
        {
            var plainSlot = new LevelIndex(0);
            var coveredSlot = new LevelIndex(24);
            BuiltLevel plain = Build(plainSlot);
            BuiltLevel covered = Build(coveredSlot);

            Assert.Throws<ArgumentException>(() => LotGrades.Of(covered with { Deduction = Option.None<DeductionGrade.Graded>() }, Pack, coveredSlot));
            Assert.Throws<ArgumentException>(() => LotGrades.Of(plain with { Deduction = covered.Deduction }, Pack, plainSlot));
        }

        /// <summary>Par is the vehicle count (the par path's length); DeadEnd 1,000 playouts of this slot; no spare; slack; PeakPar.</summary>
        private static void AssertPlanFields(BuiltLevel built, LevelIndex index, PlanGrade plan)
        {
            Assert.Equal(new MoveCount(built.Level.VehicleCount), plan.Par);
            Assert.Equal(built.ParPath.Count, plan.Par.Value);
            Assert.Equal(LotPlayouts.DeadEnd(built.Level, Pack, index, 1000), plan.DeadEnd);
            Assert.Equal(new CapacityCount(0), plan.SpareNeed);
            Assert.Equal(new CapacityCount(built.Level.Bays.Value - built.PeakPar.Value), plan.MinFreeCapacity);
            Assert.Equal(Option.Some(built.PeakPar), plan.PeakPar);
        }

        private static BuiltLevel Build(LevelIndex index) => LotPackBuilder.Build(Pack, index) is BuildResult.Built built
            ? built.Level
            : throw new InvalidOperationException("grades-test slot " + index.ToString() + " was rejected.");
    }
}

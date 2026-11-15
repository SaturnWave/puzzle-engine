using System;
using PuzzleCore.Records;
using PuzzleCore.Search;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    /// <summary>The shared grade types (shared-design.md §2) and the contract names of their closed sets.</summary>
    public sealed class GradeTests
    {
        private static readonly PlanGrade Plan = new(
            Par: new MoveCount(15),
            DeadEnd: new PerMille(892),
            SpareNeed: new CapacityCount(0),
            MinFreeCapacity: new CapacityCount(1),
            PeakPar: Option.Some(new CapacityCount(4)));

        private static readonly DeductionGrade.Graded Deduction = new(DeductionRung.D1, new StepCount(2));

        [Fact]
        public void A_think_level_carries_its_deduction_grade_and_a_plan_level_none()
        {
            var think = new LevelGrade(Plan, Option.Some(Deduction), LevelKind.Think);
            var plan = new LevelGrade(Plan, Option.None<DeductionGrade.Graded>(), LevelKind.Plan);

            Assert.Equal((Plan, Option.Some(Deduction), LevelKind.Think), (think.Plan, think.Deduction, think.Kind));
            Assert.Equal((Plan, Option.None<DeductionGrade.Graded>(), LevelKind.Plan), (plan.Plan, plan.Deduction, plan.Kind));
        }

        [Fact]
        public void A_level_grade_refuses_a_think_level_without_a_deduction_or_a_plan_level_with_one()
        {
            ArgumentException thinkWithout = Assert.Throws<ArgumentException>(() => new LevelGrade(Plan, Option.None<DeductionGrade.Graded>(), LevelKind.Think));
            ArgumentException planWith = Assert.Throws<ArgumentException>(() => new LevelGrade(Plan, Option.Some(Deduction), LevelKind.Plan));

            Assert.StartsWith("A Think level needs a deduction grade.", thinkWithout.Message, StringComparison.Ordinal);
            Assert.StartsWith("A Plan level has no deduction grade.", planWith.Message, StringComparison.Ordinal);
            Assert.Equal(("deduction", "deduction"), (thinkWithout.ParamName, planWith.ParamName));
        }

        [Fact]
        public void A_level_grade_refuses_a_kind_or_a_deduction_rung_outside_its_enum()
        {
            var unknownRung = new DeductionGrade.Graded((DeductionRung)3, new StepCount(2));

            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelGrade(Plan, Option.None<DeductionGrade.Graded>(), (LevelKind)2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LevelGrade(Plan, Option.Some(unknownRung), LevelKind.Think));
        }

        [Fact]
        public void Level_kinds_and_deduction_rungs_have_fixed_contract_names()
        {
            Assert.Equal("Plan", ContractNames.Of(LevelKind.Plan));
            Assert.Equal("Think", ContractNames.Of(LevelKind.Think));
            Assert.Equal("D1", ContractNames.Of(DeductionRung.D1));
            Assert.Equal("D2", ContractNames.Of(DeductionRung.D2));
            Assert.Equal("D3", ContractNames.Of(DeductionRung.D3));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContractNames.Of((LevelKind)2));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContractNames.Of((DeductionRung)3));
        }

        [Fact]
        public void Contract_names_read_back_exactly_and_nothing_else_does()
        {
            foreach (LevelKind kind in new[] { LevelKind.Plan, LevelKind.Think })
            {
                Assert.Equal(Option.Some(kind), ContractNames.KindNamed(ContractNames.Of(kind)));
            }

            foreach (DeductionRung rung in new[] { DeductionRung.D1, DeductionRung.D2, DeductionRung.D3 })
            {
                Assert.Equal(Option.Some(rung), ContractNames.RungNamed(ContractNames.Of(rung)));
            }

            Assert.False(ContractNames.KindNamed("think").HasValue);
            Assert.False(ContractNames.KindNamed(string.Empty).HasValue);
            Assert.False(ContractNames.RungNamed("d1").HasValue);
            Assert.False(ContractNames.RungNamed("D4").HasValue);
        }
    }
}

using PuzzleCore.Randomness;
using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// The grades of a built campaign level, field by field as shared-design.md §2 defines them ("Grade fields").
    /// A covered level is a Think level and a plain level a Plan level: the kind follows the covers alone (Q64).
    /// </summary>
    public static class LotGrades
    {
        /// <summary>
        /// Par is the vehicle count, because every win sends each vehicle out once. DeadEnd comes from
        /// <see cref="LotPlayouts.DefaultPlayouts"/> seeded playouts of this pack slot. SpareNeed is 0, because a built
        /// level wins with its own bays. MinFreeCapacity is the slack, and PeakPar the fewest bays that win.
        /// </summary>
        public static LevelGrade Of(BuiltLevel built, PackId pack, LevelIndex slot)
        {
            var plan = new PlanGrade(
                Par: new MoveCount(built.Level.VehicleCount),
                DeadEnd: LotPlayouts.DeadEnd(built.Level, pack, slot, LotPlayouts.DefaultPlayouts),
                SpareNeed: new CapacityCount(0),
                MinFreeCapacity: new CapacityCount(built.Slack),
                PeakPar: Option.Some(built.PeakPar));
            LevelKind kind = built.IsCovered ? LevelKind.Think : LevelKind.Plan;
            return new LevelGrade(plan, built.Deduction, kind);
        }
    }
}

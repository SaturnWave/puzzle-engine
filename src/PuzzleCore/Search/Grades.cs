using System;

namespace PuzzleCore.Search
{
    /// <summary>
    /// The depth a level asks for, read by the sawtooth: Plan levels test capacity planning, Think levels deduction.
    /// A Think level is a clue level (Sort: sealed vials; Jam: covers, Q64) and always has a deduction grade.
    /// Names are a public contract (ContractNames); add new kinds at the end only.
    /// </summary>
    public enum LevelKind
    {
        Plan,
        Think,
    }

    /// <summary>
    /// The game-neutral plan grade; each field is defined once, in shared-design.md §2, "Grade fields". Build it with
    /// named arguments: SpareNeed and MinFreeCapacity are both capacity counts.
    /// </summary>
    public sealed record PlanGrade(
        MoveCount Par,
        PerMille DeadEnd,
        CapacityCount SpareNeed,
        CapacityCount MinFreeCapacity,
        Option<CapacityCount> PeakPar);

    /// <summary>
    /// A level's grades: its plan grade, the deduction grade of a Think level, and its kind. A Think level always
    /// carries a deduction grade and a Plan level never does. Only Graded levels ship, so the deduction is never
    /// NeedsGuess or BudgetExceeded.
    /// </summary>
    public sealed record LevelGrade
    {
        public LevelGrade(PlanGrade plan, Option<DeductionGrade.Graded> deduction, LevelKind kind)
        {
            bool isThink = kind switch
            {
                LevelKind.Plan => false,
                LevelKind.Think => true,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown level kind."),
            };

            if (isThink != deduction.HasValue)
            {
                string rule = isThink ? "A Think level needs a deduction grade." : "A Plan level has no deduction grade.";
                throw new ArgumentException(rule, nameof(deduction));
            }

            Plan = plan;
            Deduction = deduction;
            Kind = kind;
        }

        public PlanGrade Plan { get; }

        public Option<DeductionGrade.Graded> Deduction { get; }

        public LevelKind Kind { get; }
    }
}

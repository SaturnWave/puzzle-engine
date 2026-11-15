using PuzzleCore.Gen;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Gen
{
    /// <summary>
    /// A graded pack built by hand: a plain level at slot 3 and a covered one at slot 24. The grades are chosen, not
    /// computed, and the numbers of neighbouring columns differ (the plain level's par is 7 beside 6 vehicles, and its
    /// lot is 5 wide and 6 high), so a column written from the wrong value shows.
    /// </summary>
    internal static class HandMadeGrades
    {
        public static readonly PackId TestPack = PackId.Parse("csv-test").Or(default);

        /// <summary>The grading report of <see cref="Graded"/>: the header, the plain level's line, the covered level's line.</summary>
        public const string Csv =
            LotGradingCsv.Header + "\n"
            + "csv-test-0003,3,Plan,5,6,6,1,4,3,1,0,7,640,3500,1500,0,0,,\n"
            + "csv-test-0024,24,Think,6,6,9,3,4,4,0,2,9,892,4666,666,2,1,D2,3\n";

        public static GradedLotPack Graded() => new(TestPack, new[]
        {
            new GradedLot(
                Id: IdOf(3),
                Slot: new LevelIndex(3),
                Fingerprint: LevelFingerprint.Parse(new string('a', 64)).Or(default),
                Built: Built(Load(SixBluesInAChain() with { Height = 6 }), Option.None<DeductionGrade.Graded>()),
                Grade: new LevelGrade(
                    new PlanGrade(Par: new MoveCount(7), DeadEnd: new PerMille(640), SpareNeed: new CapacityCount(0), MinFreeCapacity: new CapacityCount(1), PeakPar: Option.Some(new CapacityCount(3))),
                    Option.None<DeductionGrade.Graded>(),
                    LevelKind.Plan),
                Branching: new Thousandths(3500),
                BuriedThird: new Thousandths(1500)),
            new GradedLot(
                Id: IdOf(24),
                Slot: new LevelIndex(24),
                Fingerprint: LevelFingerprint.Parse(new string('b', 64)).Or(default),
                Built: Built(Load(SixBySix()), Option.Some(new DeductionGrade.Graded(DeductionRung.D2, new StepCount(3)))),
                Grade: new LevelGrade(
                    new PlanGrade(Par: new MoveCount(9), DeadEnd: new PerMille(892), SpareNeed: new CapacityCount(2), MinFreeCapacity: new CapacityCount(0), PeakPar: Option.Some(new CapacityCount(4))),
                    Option.Some(new DeductionGrade.Graded(DeductionRung.D2, new StepCount(3))),
                    LevelKind.Think),
                Branching: new Thousandths(4666),
                BuriedThird: new Thousandths(666)),
        });

        public static LevelId IdOf(int slot) => LevelId.For(TestPack, new LevelIndex(slot)).Or(default);

        private static BuiltLevel Built(LotLevel level, Option<DeductionGrade.Graded> deduction) =>
            new(level, new CapacityCount(3), ReadOnlyArray.Empty<Exit>(), deduction);
    }
}

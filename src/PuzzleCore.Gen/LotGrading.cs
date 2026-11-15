using System;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// A built and graded campaign level: its id, slot and fingerprint, the built level, its grades and the Jam-only
    /// CSV features. One row of the grading report and one level of the presentation file.
    /// </summary>
    public sealed record GradedLot(
        LevelId Id,
        LevelIndex Slot,
        LevelFingerprint Fingerprint,
        BuiltLevel Built,
        LevelGrade Grade,
        Thousandths Branching,
        Thousandths BuriedThird);

    /// <summary>Graded(level) or Rejected(the pack builder's reason). Closed set.</summary>
    public abstract record LotGradeResult
    {
        private LotGradeResult()
        {
        }

        public sealed record Graded(GradedLot Lot) : LotGradeResult;

        public sealed record Rejected(BuildFailure Reason) : LotGradeResult;
    }

    /// <summary>Builds and grades campaign slots through <see cref="LotPackBuilder"/>, the path the CLI and the playtest share.</summary>
    public static class LotGrading
    {
        /// <summary>
        /// Builds the pack's slot and grades it. DeadEnd runs 1,000 playouts, so a level takes about a tenth of a
        /// second. The pack id must leave room for the slot's level id (<see cref="LevelId.For"/>).
        /// </summary>
        public static LotGradeResult Grade(PackId pack, LevelIndex slot)
        {
            if (!LevelId.For(pack, slot).TryGet(out LevelId id))
            {
                throw new ArgumentException("The pack id leaves no room for the level id of slot " + slot.ToString() + ".", nameof(pack));
            }

            return LotPackBuilder.Build(pack, slot) switch
            {
                BuildResult.Built built => new LotGradeResult.Graded(Graded(id, pack, slot, built.Level)),
                BuildResult.Rejected rejected => new LotGradeResult.Rejected(rejected.Reason),
                _ => throw new NotSupportedException("BuildResult is a closed set."),
            };
        }

        /// <summary>
        /// Whether slots 0 to slotCount - 1 of the pack all have level ids (<see cref="LevelId.For"/>). Ids grow with
        /// the slot's digits, so the last slot's id is the longest.
        /// </summary>
        public static bool HasRoomForIds(PackId pack, int slotCount)
        {
            if (slotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "A slot count is never negative.");
            }

            return slotCount == 0 || LevelId.For(pack, new LevelIndex(slotCount - 1)).HasValue;
        }

        /// <summary>The fingerprint of the level's canonical text, in the form records and presentation files carry.</summary>
        internal static LevelFingerprint FingerprintOf(LotLevel level) =>
            LevelFingerprint.Parse(LotText.Fingerprint(level)).TryGet(out LevelFingerprint fingerprint)
                ? fingerprint
                : throw new InvalidOperationException("LotText.Fingerprint always gives 64 lowercase hex digits.");

        private static GradedLot Graded(LevelId id, PackId pack, LevelIndex slot, BuiltLevel built) => new(
            Id: id,
            Slot: slot,
            Fingerprint: FingerprintOf(built.Level),
            Built: built,
            Grade: LotGrades.Of(built, pack, slot),
            Branching: LotFeatures.Branching(built),
            BuriedThird: LotFeatures.BuriedThird(built.Level));
    }
}

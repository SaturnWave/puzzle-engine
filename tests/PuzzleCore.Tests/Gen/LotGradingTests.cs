using System;
using PuzzleCore.Gen;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using Xunit;

namespace PuzzleCore.Tests.Gen
{
    /// <summary>Grading a campaign slot: the same build as LotPackBuilder, with its id, grades and CSV features.</summary>
    public sealed class LotGradingTests
    {
        private static readonly PackId Pack = PackId.Parse("grades-test").Or(default);

        [Theory]
        [InlineData(0, "grades-test-0000")]
        [InlineData(24, "grades-test-0024")]
        public void A_built_slot_is_graded_with_its_id_fingerprint_grades_and_features(int slotNumber, string id)
        {
            var slot = new LevelIndex(slotNumber);
            BuiltLevel built = ((BuildResult.Built)LotPackBuilder.Build(Pack, slot)).Level;

            GradedLot graded = Assert.IsType<LotGradeResult.Graded>(LotGrading.Grade(Pack, slot)).Lot;

            Assert.Equal(LevelId.Parse(id), Option.Some(graded.Id));
            Assert.Equal(slot, graded.Slot);
            Assert.Equal(LotText.Fingerprint(built.Level), graded.Fingerprint.Hex);
            Assert.Equal(LotText.Write(built.Level), LotText.Write(graded.Built.Level));
            Assert.Equal((built.PeakPar, built.ParPath, built.Deduction), (graded.Built.PeakPar, graded.Built.ParPath, graded.Built.Deduction));
            Assert.Equal(LotGrades.Of(built, Pack, slot), graded.Grade);
            Assert.Equal(LotFeatures.Branching(built), graded.Branching);
            Assert.Equal(LotFeatures.BuriedThird(built.Level), graded.BuriedThird);
        }

        [Fact]
        public void A_rejected_slot_keeps_the_pack_builder_s_reason()
        {
            PackId pack = PackId.Parse("rung-sample-01").Or(default);

            Assert.Equal(new LotGradeResult.Rejected(BuildFailure.PlainRejected), LotGrading.Grade(pack, new LevelIndex(39)));
        }

        [Fact]
        public void A_pack_id_with_no_room_for_the_slot_s_level_id_is_refused()
        {
            PackId pack = PackId.Parse(new string('a', 60)).Or(default);

            Assert.Throws<ArgumentException>(() => LotGrading.Grade(pack, new LevelIndex(0)));
        }
    }
}

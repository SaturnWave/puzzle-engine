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

        /// <summary>
        /// Slot 44 of cover-hunt-111160 is a D2 slot whose coverer gives up after its 20 tries: the first such slot a
        /// scan of 16,064 covered slots found (P4).
        /// </summary>
        [Theory]
        [InlineData("rung-sample-01", 39, BuildFailure.PlainRejected)]
        [InlineData("cover-hunt-111160", 44, BuildFailure.CoverRejected)]
        public void A_rejected_slot_keeps_the_pack_builder_s_reason(string packText, int slot, BuildFailure reason)
        {
            PackId pack = PackId.Parse(packText).Or(default);

            Assert.Equal(new LotGradeResult.Rejected(reason), LotGrading.Grade(pack, new LevelIndex(slot)));
        }

        [Fact]
        public void A_pack_id_with_no_room_for_the_slot_s_level_id_is_refused()
        {
            PackId pack = PackId.Parse(new string('a', 60)).Or(default);

            Assert.Throws<ArgumentException>(() => LotGrading.Grade(pack, new LevelIndex(0)));
        }

        /// <summary>A 59-character pack id leaves room for "-0000" to "-9999", and no more.</summary>
        [Theory]
        [InlineData(59, 1, true)]
        [InlineData(60, 1, false)]
        [InlineData(59, 10_000, true)]
        [InlineData(59, 10_001, false)]
        [InlineData(64, 0, true)]
        public void A_pack_has_room_for_ids_while_the_last_slot_id_has_at_most_64_characters(int packLength, int slotCount, bool hasRoom)
        {
            PackId pack = PackId.Parse(new string('a', packLength)).Or(default);

            Assert.Equal(hasRoom, LotGrading.HasRoomForIds(pack, slotCount));
        }

        /// <summary>The method's own refusal, not the level index's or an overflow of slotCount - 1.</summary>
        [Theory]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        public void A_negative_slot_count_is_refused(int slotCount)
        {
            ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(() => LotGrading.HasRoomForIds(Pack, slotCount));

            Assert.Equal("slotCount", refusal.ParamName);
        }
    }
}

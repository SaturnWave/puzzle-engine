using System;
using System.Globalization;
using System.Linq;
using PuzzleCore.Packs;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    /// <summary>The presentation file (pc-presentation v1, Q67): its canonical text, its round trip and its strict reader.</summary>
    public sealed class PresentationJsonTests
    {
        private const string A16 = "aaaaaaaaaaaaaaaa";
        private const string B16 = "bbbbbbbbbbbbbbbb";
        private const string PlainPrint = A16 + A16 + A16 + A16;
        private const string CoveredPrint = B16 + B16 + B16 + B16;

        /// <summary>A plain level at slot 3 and a covered one at slot 24, in canonical form: compact, members sorted.</summary>
        private const string JamText =
            "{\"format\":\"pc-presentation\",\"formatVersion\":1,\"game\":\"Jam\",\"levels\":["
            + "{\"fingerprint\":\"" + PlainPrint + "\",\"kind\":\"Plan\",\"levelId\":\"grade-test-0003\","
            + "\"plan\":{\"deadEnd\":640,\"minFreeCapacity\":2,\"par\":12,\"peakPar\":3,\"spareNeed\":0},\"slot\":3},"
            + "{\"deduction\":{\"rung\":\"D2\",\"steps\":3},\"fingerprint\":\"" + CoveredPrint + "\",\"kind\":\"Think\","
            + "\"levelId\":\"grade-test-0024\",\"plan\":{\"deadEnd\":892,\"minFreeCapacity\":1,\"par\":15,\"peakPar\":4,\"spareNeed\":0},\"slot\":24}"
            + "],\"packId\":\"grade-test\"}";

        private static readonly PackId Pack = PackId.Parse("grade-test").Or(default);

        [Fact]
        public void A_presentation_file_is_written_in_canonical_form_without_a_deduction_on_a_plan_level()
        {
            Assert.Equal(JamText, PresentationJson.Write(JamFile()));
        }

        [Fact]
        public void The_canonical_text_reads_back_as_the_file()
        {
            Assert.Equal(new PresentationReadResult.Read(JamFile()), PresentationJson.Read(JamText));
        }

        [Fact]
        public void A_game_without_a_peak_par_writes_none_and_reads_back_without_one()
        {
            var sort = new PresentationFile(GameKind.Sort, Pack, ReadOnlyArray.Of(
                Level(7, PlainPrint, new LevelGrade(PlanOf(par: 20, deadEnd: 500, minFreeCapacity: 0, peakPar: Option.None<CapacityCount>()), Option.None<DeductionGrade.Graded>(), LevelKind.Plan))));

            string text = PresentationJson.Write(sort);

            Assert.DoesNotContain("peakPar", text, StringComparison.Ordinal);
            Assert.DoesNotContain("null", text, StringComparison.Ordinal);
            Assert.Equal(new PresentationReadResult.Read(sort), PresentationJson.Read(text));
        }

        [Fact]
        public void A_pack_with_no_levels_reads_back_empty()
        {
            var empty = new PresentationFile(GameKind.Jam, Pack, ReadOnlyArray.Empty<PresentationLevel>());

            Assert.Equal(new PresentationReadResult.Read(empty), PresentationJson.Read(PresentationJson.Write(empty)));
        }

        /// <summary>Each fault is reported once, at its path, and nothing else is.</summary>
        [Theory]
        [InlineData(",\"packId\":\"grade-test\"}", ",\"packId\":\"grade-test\",\"tier\":1}", "$.tier: unknown field")]
        [InlineData("\"slot\":3}", "\"slot\":3,\"label\":\"easy\"}", "$.levels[0].label: unknown field")]
        [InlineData("\"spareNeed\":0},\"slot\":3", "\"spareNeed\":0,\"greedyWin\":5},\"slot\":3", "$.levels[0].plan.greedyWin: unknown field")]
        [InlineData("\"steps\":3", "\"steps\":3,\"hint\":1", "$.levels[1].deduction.hint: unknown field")]
        [InlineData("\"slot\":3}", "\"slot\":-3}", "$.levels[0].slot: -3 is outside 0..2147483647")]
        [InlineData("\"par\":12", "\"par\":-1", "$.levels[0].plan.par: -1 is outside 0..2147483647")]
        [InlineData("\"deadEnd\":640", "\"deadEnd\":-1", "$.levels[0].plan.deadEnd: -1 is outside 0..1000")]
        [InlineData("\"deadEnd\":640", "\"deadEnd\":1001", "$.levels[0].plan.deadEnd: 1001 is outside 0..1000")]
        [InlineData("\"spareNeed\":0},\"slot\":3", "\"spareNeed\":-1},\"slot\":3", "$.levels[0].plan.spareNeed: -1 is outside 0..2147483647")]
        [InlineData("\"minFreeCapacity\":2", "\"minFreeCapacity\":-1", "$.levels[0].plan.minFreeCapacity: -1 is outside 0..2147483647")]
        [InlineData("\"peakPar\":3", "\"peakPar\":-1", "$.levels[0].plan.peakPar: -1 is outside 0..2147483647")]
        [InlineData("\"peakPar\":3", "\"peakPar\":null", "$.levels[0].plan.peakPar: expected an integer, found null")]
        [InlineData("\"kind\":\"Plan\"", "\"kind\":\"plan\"", "$.levels[0].kind: unknown level kind")]
        [InlineData("\"deduction\":{\"rung\":\"D2\",\"steps\":3},", "", "$.levels[1].deduction: a Think level needs a deduction grade")]
        [InlineData("\"kind\":\"Think\"", "\"kind\":\"Plan\"", "$.levels[1].deduction: a Plan level has no deduction grade")]
        [InlineData("\"rung\":\"D2\"", "\"rung\":\"D4\"", "$.levels[1].deduction.rung: unknown deduction rung")]
        [InlineData("\"steps\":3", "\"steps\":-1", "$.levels[1].deduction.steps: -1 is outside 0..2147483647")]
        [InlineData("\"levelId\":\"grade-test-0003\"", "\"levelId\":\"Grade-test-0003\"", "$.levels[0].levelId: expected lowercase letters, digits and '-'")]
        [InlineData("\"levelId\":\"grade-test-0003\"", "\"levelId\":\"grade-test-3\"", "$.levels[0].levelId: expected \"grade-test-0003\"")]
        [InlineData(PlainPrint, A16 + A16 + A16 + "AAAAAAAAAAAAAAAA", "$.levels[0].fingerprint: expected 64 lowercase hex digits")]
        [InlineData("\"game\":\"Jam\"", "\"game\":\"Chess\"", "$.game: unknown game")]
        [InlineData("\"packId\":\"grade-test\"", "\"packId\":\"Grade\"", "$.packId: expected lowercase letters, digits and '-'")]
        [InlineData("\"format\":\"pc-presentation\"", "\"format\":\"pc-record\"", "$.format: expected \"pc-presentation\"")]
        public void Reading_is_strict_and_names_the_path_of_each_problem(string find, string replacement, string problem)
        {
            Assert.Equal(new[] { problem }, Problems(JamText.Replace(find, replacement, StringComparison.Ordinal)));
        }

        [Fact]
        public void Each_slot_must_be_above_the_slot_just_before_it()
        {
            // Slots 3, 24, 10: the third level is above the first but below the second.
            const string SlotTen =
                ",{\"fingerprint\":\"" + PlainPrint + "\",\"kind\":\"Plan\",\"levelId\":\"grade-test-0010\","
                + "\"plan\":{\"deadEnd\":640,\"minFreeCapacity\":2,\"par\":12,\"peakPar\":3,\"spareNeed\":0},\"slot\":10}";
            string text = JamText.Replace("],\"packId\"", SlotTen + "],\"packId\"", StringComparison.Ordinal);

            Assert.Equal(new[] { "$.levels[2].slot: slots must be in strictly ascending order" }, Problems(text));
        }

        [Fact]
        public void Every_count_reads_back_at_both_ends_of_its_range()
        {
            var file = new PresentationFile(GameKind.Jam, Pack, ReadOnlyArray.Of(
                Level(0, PlainPrint, new LevelGrade(
                    new PlanGrade(Par: new MoveCount(0), DeadEnd: new PerMille(0), SpareNeed: new CapacityCount(0), MinFreeCapacity: new CapacityCount(0), PeakPar: Option.Some(new CapacityCount(0))),
                    Option.Some(new DeductionGrade.Graded(DeductionRung.D1, new StepCount(0))),
                    LevelKind.Think)),
                Level(int.MaxValue, CoveredPrint, new LevelGrade(
                    new PlanGrade(Par: new MoveCount(int.MaxValue), DeadEnd: new PerMille(PerMille.Whole), SpareNeed: new CapacityCount(int.MaxValue), MinFreeCapacity: new CapacityCount(int.MaxValue), PeakPar: Option.Some(new CapacityCount(int.MaxValue))),
                    Option.Some(new DeductionGrade.Graded(DeductionRung.D3, new StepCount(int.MaxValue))),
                    LevelKind.Think))));

            Assert.Equal(new PresentationReadResult.Read(file), PresentationJson.Read(PresentationJson.Write(file)));
        }

        [Theory]
        [InlineData(2, "grade-test-0002")]
        [InlineData(3, "grade-test-0003")]
        public void Levels_out_of_slot_order_are_invalid(int slot, string levelId)
        {
            string text = JamText
                .Replace("\"levelId\":\"grade-test-0024\"", "\"levelId\":\"" + levelId + "\"", StringComparison.Ordinal)
                .Replace("\"slot\":24}", "\"slot\":" + slot.ToString(CultureInfo.InvariantCulture) + "}", StringComparison.Ordinal);

            Assert.Equal(new[] { "$.levels[1].slot: slots must be in strictly ascending order" }, Problems(text));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void A_format_version_other_than_1_is_unsupported(int version)
        {
            string text = JamText.Replace("\"formatVersion\":1", "\"formatVersion\":" + version.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

            Assert.IsType<PresentationReadResult.UnsupportedVersion>(PresentationJson.Read(text));
        }

        [Theory]
        [InlineData("\"formatVersion\":1,", "", "$.formatVersion: missing field")]
        [InlineData("\"formatVersion\":1", "\"formatVersion\":\"1\"", "$.formatVersion: expected an integer, found a string")]
        [InlineData("\"formatVersion\":1", "\"formatVersion\":-1", "$.formatVersion: -1 is outside 0..2147483647")]
        public void A_missing_or_bad_format_version_is_invalid_not_unsupported(string find, string replacement, string problem)
        {
            Assert.Equal(new[] { problem }, Problems(JamText.Replace(find, replacement, StringComparison.Ordinal)));
        }

        [Fact]
        public void A_file_of_another_format_is_invalid_whatever_its_version()
        {
            string text = JamText
                .Replace("\"format\":\"pc-presentation\"", "\"format\":\"pc-record\"", StringComparison.Ordinal)
                .Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal);

            Assert.Equal(new[] { "$.format: expected \"pc-presentation\"" }, Problems(text));
        }

        [Theory]
        [InlineData("not json", "$: not valid JSON")]
        [InlineData("[]", "$: expected an object, found an array")]
        public void Text_that_is_not_a_json_object_is_invalid(string text, string problem)
        {
            Assert.Equal(new[] { problem }, Problems(text));
        }

        [Fact]
        public void A_presentation_file_refuses_levels_out_of_slot_order_or_with_another_id()
        {
            PresentationLevel three = JamFile().Levels[0];
            PresentationLevel twentyFour = JamFile().Levels[1];

            Assert.Throws<ArgumentException>(() => new PresentationFile(GameKind.Jam, Pack, ReadOnlyArray.Of(twentyFour, three)));
            Assert.Throws<ArgumentException>(() => new PresentationFile(GameKind.Jam, Pack, ReadOnlyArray.Of(three, three)));
            Assert.Throws<ArgumentException>(() => new PresentationFile(GameKind.Jam, PackId.Parse("other-pack").Or(default), ReadOnlyArray.Of(three)));
        }

        /// <summary>The writer could not write these; every file the constructor accepts can be written.</summary>
        [Fact]
        public void A_presentation_file_refuses_an_unknown_game_an_empty_pack_id_or_a_level_without_a_fingerprint()
        {
            PresentationLevel three = JamFile().Levels[0];

            Assert.Throws<ArgumentOutOfRangeException>(() => new PresentationFile((GameKind)2, Pack, ReadOnlyArray.Of(three)));
            Assert.Throws<ArgumentException>(() => new PresentationFile(GameKind.Jam, default, ReadOnlyArray.Empty<PresentationLevel>()));
            Assert.Throws<ArgumentException>(() => new PresentationFile(GameKind.Jam, Pack, ReadOnlyArray.Of(three with { Fingerprint = default })));
        }

        private static PresentationFile JamFile() => new(GameKind.Jam, Pack, ReadOnlyArray.Of(
            Level(3, PlainPrint, new LevelGrade(
                PlanOf(par: 12, deadEnd: 640, minFreeCapacity: 2, peakPar: Option.Some(new CapacityCount(3))),
                Option.None<DeductionGrade.Graded>(),
                LevelKind.Plan)),
            Level(24, CoveredPrint, new LevelGrade(
                PlanOf(par: 15, deadEnd: 892, minFreeCapacity: 1, peakPar: Option.Some(new CapacityCount(4))),
                Option.Some(new DeductionGrade.Graded(DeductionRung.D2, new StepCount(3))),
                LevelKind.Think))));

        private static PresentationLevel Level(int slot, string fingerprint, LevelGrade grade)
        {
            var index = new LevelIndex(slot);
            return new PresentationLevel(LevelId.For(Pack, index).Or(default), index, LevelFingerprint.Parse(fingerprint).Or(default), grade);
        }

        private static PlanGrade PlanOf(int par, int deadEnd, int minFreeCapacity, Option<CapacityCount> peakPar) => new(
            Par: new MoveCount(par),
            DeadEnd: new PerMille(deadEnd),
            SpareNeed: new CapacityCount(0),
            MinFreeCapacity: new CapacityCount(minFreeCapacity),
            PeakPar: peakPar);

        private static string[] Problems(string text) => PresentationJson.Read(text) is PresentationReadResult.Invalid invalid
            ? invalid.Problems.Select(problem => problem.ToString()).ToArray()
            : throw new InvalidOperationException("Expected an invalid file.");
    }
}

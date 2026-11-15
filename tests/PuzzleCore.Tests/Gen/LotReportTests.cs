using System;
using System.IO;
using System.Linq;
using System.Text;
using PuzzleCore.Gen;
using PuzzleCore.Jam;
using PuzzleCore.Packs;
using PuzzleCore.Records;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Gen
{
    /// <summary>The grading report's columns (Q68) and the files gen writes (Q66-Q68), on the hand-made graded pack.</summary>
    public sealed class LotReportTests
    {
        [Fact]
        public void The_grading_report_has_a_header_and_one_line_per_level_with_every_column()
        {
            Assert.Equal(HandMadeGrades.Csv, LotGradingCsv.Write(HandMadeGrades.Graded()));
        }

        [Fact]
        public void The_grading_report_of_a_pack_with_no_levels_is_its_header()
        {
            Assert.Equal(LotGradingCsv.Header + "\n", LotGradingCsv.Write(new GradedLotPack(HandMadeGrades.TestPack, Array.Empty<GradedLot>())));
        }

        [Fact]
        public void A_graded_pack_s_presentation_file_lists_each_level_s_id_slot_fingerprint_and_grade()
        {
            GradedLotPack pack = HandMadeGrades.Graded();

            Assert.Equal(GameKind.Jam, pack.Presentation.Game);
            Assert.Equal(HandMadeGrades.TestPack, pack.Presentation.Pack);
            Assert.Equal(
                pack.Levels.Select(lot => new PresentationLevel(lot.Id, lot.Slot, lot.Fingerprint, lot.Grade)),
                pack.Presentation.Levels);
        }

        [Fact]
        public void A_graded_pack_refuses_levels_out_of_slot_order()
        {
            GradedLot[] levels = HandMadeGrades.Graded().Levels.ToArray();

            Assert.Throws<ArgumentException>(() => new GradedLotPack(HandMadeGrades.TestPack, levels.Reverse()));
        }

        /// <summary>Gen writes each level's text beside the presentation file, so the fingerprint there must be that text's.</summary>
        [Fact]
        public void A_graded_pack_refuses_a_level_whose_fingerprint_belongs_to_another_text()
        {
            GradedLot[] levels = HandMadeGrades.Graded().Levels.ToArray();
            levels[1] = levels[1] with { Fingerprint = levels[0].Fingerprint };

            Assert.Throws<ArgumentException>(() => new GradedLotPack(HandMadeGrades.TestPack, levels));
        }

        /// <summary>A Jam grade always has a peak par, so the report never has an empty peak_par.</summary>
        [Fact]
        public void A_graded_pack_refuses_a_level_whose_grade_has_no_peak_par()
        {
            GradedLot[] levels = HandMadeGrades.Graded().Levels.ToArray();
            LevelGrade grade = levels[0].Grade;
            levels[0] = levels[0] with { Grade = new LevelGrade(grade.Plan with { PeakPar = Option.None<CapacityCount>() }, grade.Deduction, grade.Kind) };

            Assert.Throws<ArgumentException>(() => new GradedLotPack(HandMadeGrades.TestPack, levels));
        }

        /// <summary>The level text has letters for colours 1-6 only, so a level in colour 15 has no text and no fingerprint.</summary>
        [Fact]
        public void A_graded_pack_refuses_a_level_its_text_cannot_write()
        {
            GradedLot[] levels = HandMadeGrades.Graded().Levels.ToArray();
            LotLevel colourFifteen = Load(Repainted(SixBluesInAChain() with { Height = 6 }, from: Blue, to: new ColourId(ColourId.MaxValue)));
            levels[0] = levels[0] with { Built = levels[0].Built with { Level = colourFifteen } };

            ArgumentException refusal = Assert.Throws<ArgumentException>(() => new GradedLotPack(HandMadeGrades.TestPack, levels));

            Assert.Equal("levels", refusal.ParamName);
            Assert.IsType<ArgumentOutOfRangeException>(refusal.InnerException);
        }

        [Fact]
        public void Gen_writes_each_level_then_the_presentation_file_and_the_report_as_utf8_without_a_mark()
        {
            GradedLotPack pack = HandMadeGrades.Graded();
            string directory = Path.Combine(Path.GetTempPath(), "puzzle-gen-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                LotPackFiles.Write(pack, directory);

                Assert.Equal(
                    new[] { "csv-test-0003.pc-lot", "csv-test-0024.pc-lot", "csv-test.grades.csv", "csv-test.presentation.json" },
                    Directory.GetFiles(directory).Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal));
                Assert.Equal(Utf8(LotText.Write(pack.Levels[0].Built.Level)), File.ReadAllBytes(Path.Combine(directory, "csv-test-0003.pc-lot")));
                Assert.Equal(Utf8(LotText.Write(pack.Levels[1].Built.Level)), File.ReadAllBytes(Path.Combine(directory, "csv-test-0024.pc-lot")));
                Assert.Equal(Utf8(PresentationJson.Write(pack.Presentation)), File.ReadAllBytes(Path.Combine(directory, "csv-test.presentation.json")));
                Assert.Equal(Utf8(HandMadeGrades.Csv), File.ReadAllBytes(Path.Combine(directory, "csv-test.grades.csv")));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>UTF-8 without a byte-order mark: Encoding.UTF8.GetBytes never adds one.</summary>
        private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
    }
}

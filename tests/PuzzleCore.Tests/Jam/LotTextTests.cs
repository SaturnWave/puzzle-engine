using System.IO;
using PuzzleCore.Jam;
using PuzzleCore.Tests.Support;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    public sealed class LotTextTests
    {
        /// <summary>SHA-256 of the brief's 6×6 canonical text, computed with Python's hashlib (2026-11-07).</summary>
        public const string SixBySixFingerprint = "de8ba3a9518814626c5c5f8055c01f037ef25830e0862bb9ead477c821bf9c6b";

        private const string SixBySixText =
            "pc-lot\nr=1\nsize=6x6\nbays=4\npillars=4\n"
            + "vehicles=B:1:2:N;R:8:2:E;Y:12:2:E;B:14:2:S;B:16:2:E;Y:23:2:N;R:24:2:N;R:27:2:W;Y:33:2:E\n"
            + "covers=3,7\nclues=Not(3,R)";

        [Fact]
        public void The_six_by_six_board_writes_exactly_the_briefs_canonical_text()
        {
            Assert.Equal(SixBySixText, LotText.Write(Load(SixBySix())));
        }

        [Fact]
        public void The_six_by_six_fingerprint_is_pinned()
        {
            Assert.Equal(SixBySixFingerprint, LotText.Fingerprint(Load(SixBySix())));
        }

        [Fact]
        public void The_committed_golden_level_file_is_the_canonical_text()
        {
            string committed = File.ReadAllText(RepositoryLayout.FullPath("packs/jam/golden/six-by-six.pc-lot"));

            Assert.Equal(SixBySixText, committed);
        }

        [Fact]
        public void Reading_the_canonical_text_gives_back_the_same_level()
        {
            var read = Assert.IsType<LotTextResult.Read>(LotText.Read(SixBySixText));

            Assert.Equal(SixBySixText, LotText.Write(Load(read.Definition)));
        }

        [Fact]
        public void Every_clue_family_round_trips_in_canonical_order()
        {
            string text = SixBySixText.Replace("clues=Not(3,R)", "clues=Touch(7,Y,1);Not(3,R);Ahead(7,Same);Not(3,Y)", System.StringComparison.Ordinal);

            var read = Assert.IsType<LotTextResult.Read>(LotText.Read(text));

            Assert.EndsWith("clues=Not(3,R);Not(3,Y);Ahead(7,Same);Touch(7,Y,1)", LotText.Write(Load(read.Definition)), System.StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("pc-lots\nr=1", 1, LotTextProblem.WrongHeader)]
        [InlineData("pc-lot\nr=2\nsize=6x6\nbays=4\npillars=\nvehicles=R:0:2:W\ncovers=\nclues=", 2, LotTextProblem.UnsupportedRulesVersion)]
        [InlineData("pc-lot\nr=1\nbays=4", 3, LotTextProblem.MissingOrMisplacedLine)]
        [InlineData("pc-lot\nr=1\nsize=6by6\nbays=4\npillars=\nvehicles=R:0:2:W\ncovers=\nclues=", 3, LotTextProblem.BadSize)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=04\npillars=\nvehicles=R:0:2:W\ncovers=\nclues=", 4, LotTextProblem.BadNumber)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=4\npillars=\nvehicles=X:0:2:W\ncovers=\nclues=", 6, LotTextProblem.BadVehicle)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=4\npillars=\nvehicles=R:0:4:W\ncovers=\nclues=", 6, LotTextProblem.BadVehicle)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=4\npillars=\nvehicles=R:0:2:W\ncovers=1\nclues=", 7, LotTextProblem.BadCover)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=4\npillars=\nvehicles=R:0:2:W;R:6:2:W\ncovers=1,0\nclues=", 7, LotTextProblem.BadCover)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=4\npillars=\nvehicles=R:0:2:W\ncovers=0\nclues=Maybe(0,R)", 8, LotTextProblem.BadClue)]
        [InlineData("pc-lot\nr=1\nsize=6x6\nbays=4\npillars=\nvehicles=R:0:2:W\ncovers=\nclues=\n", 9, LotTextProblem.TrailingText)]
        public void Malformed_text_reports_the_line_and_the_problem(string text, int line, LotTextProblem problem)
        {
            var malformed = Assert.IsType<LotTextResult.Malformed>(LotText.Read(text));

            Assert.Equal(line, malformed.Line);
            Assert.Equal(problem, malformed.Problem);
        }
    }
}

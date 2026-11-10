using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The attempt's SeenSet under Covered Lot's rules: the covers lifted, or shown by Insight, in this
    /// attempt. Within the attempt it only grows, whatever else the player does (shared-design.md §2).
    /// </summary>
    public sealed class SeenSetTests
    {
        [Theory]
        [InlineData(PlayMode.Campaign)]
        [InlineData(PlayMode.DailyProof)]
        [InlineData(PlayMode.Casebook)]
        [InlineData(PlayMode.WeeklyTwist)]
        [InlineData(PlayMode.Competitive)]
        [InlineData(PlayMode.Siege)]
        public void A_lifted_cover_marks_its_own_piece_seen_in_every_mode(PlayMode mode)
        {
            // Piece 2 is yellow, colour id 3, so the piece and its colour stay apart in every mode.
            LotLevel level = FreeCarsWithCoversOn(2);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, mode);
            Play(attempt, 0, 1, 2);

            Assert.Equal(SeenOnly(2), attempt.Seen);
        }

        [Fact]
        public void A_refused_second_insight_reveals_nothing()
        {
            LotLevel level = FreeCarsWithCoversOn(7, 8);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, PlayMode.Campaign);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(7)));

            Assert.Equal(new StepResult<ExitRejection>.Refused(InvalidReason.InsightLimit), attempt.UseInsight(new HiddenCell(8)));
            Assert.Equal(SeenOnly(7), attempt.Seen);
        }

        [Theory]
        [InlineData(PlayMode.DailyProof)]
        [InlineData(PlayMode.Casebook)]
        [InlineData(PlayMode.WeeklyTwist)]
        [InlineData(PlayMode.Competitive)]
        [InlineData(PlayMode.Siege)]
        public void An_insight_outside_campaign_is_refused_and_reveals_nothing(PlayMode mode)
        {
            LotLevel level = FreeCarsWithCoversOn(8);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, mode);

            Assert.Equal(new StepResult<ExitRejection>.Refused(InvalidReason.BoosterInCompetitiveMode), attempt.UseInsight(new HiddenCell(8)));
            Assert.Equal(default(SeenSet), attempt.Seen);
        }

        [Fact]
        public void A_concede_keeps_every_seen_cell()
        {
            LotLevel level = FreeCarsWithCoversOn(0);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, PlayMode.Campaign);
            Play(attempt, 0); // piece 0 lifts its cover
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Concede());

            Assert.Equal(SeenOnly(0), attempt.Seen);
        }

        [Fact]
        public void An_insight_after_a_lift_keeps_both_cells_seen()
        {
            LotLevel level = FreeCarsWithCoversOn(0, 8);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, PlayMode.Campaign);
            Play(attempt, 0); // piece 0 lifts its cover
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(8)));

            Assert.Equal(SeenOnly(0).With(new HiddenCell(8)), attempt.Seen);
        }

        [Fact]
        public void A_bump_leaves_the_seen_set_unchanged()
        {
            LotLevel level = Load(SixBySix());
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, PlayMode.Campaign);
            Play(attempt, 0, 3); // piece 3 lifts its cover
            Assert.IsType<StepResult<ExitRejection>.Illegal>(attempt.Move(new Exit(new PieceId(6)))); // piece 2 still blocks it

            Assert.Equal(SeenOnly(3), attempt.Seen);
        }

        [Fact]
        public void The_spare_and_a_rewind_behind_it_keep_every_seen_cell()
        {
            LotLevel level = FreeCarsWithCoversOn(0);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, PlayMode.Campaign);
            Play(attempt, 0, 1, 2, 4); // piece 0 lifts its cover; the bays fill
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.TakeSpare());
            Assert.Equal(SeenOnly(0), attempt.Seen);

            Play(attempt, 5); // five cars in five bays: full again
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(3))); // behind the spare
            Assert.Equal(SeenOnly(0), attempt.Seen);
        }

        [Fact]
        public void A_rewind_back_past_an_insight_keeps_its_cell_seen()
        {
            LotLevel level = FreeCarsWithCoversOn(8);
            Attempt<LotState, Exit, ExitRejection> attempt = Begin(level, PlayMode.Campaign);
            Play(attempt, 0, 1);
            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.UseInsight(new HiddenCell(8)));
            Play(attempt, 2, 4); // the bays fill and no cover lifts, so the fence still sits at the start

            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(3))); // back to before the insight
            Assert.Equal(SeenOnly(8), attempt.Seen);
        }

        private static SeenSet SeenOnly(int cell) => default(SeenSet).With(new HiddenCell((byte)cell));

        private static Attempt<LotState, Exit, ExitRejection> Begin(LotLevel level, PlayMode mode) =>
            new(new JamRules(level), LotState.Start(level), mode);

        private static void Play(Attempt<LotState, Exit, ExitRejection> attempt, params int[] pieces)
        {
            foreach (int piece in pieces)
            {
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Move(new Exit(new PieceId((byte)piece))));
            }
        }
    }
}

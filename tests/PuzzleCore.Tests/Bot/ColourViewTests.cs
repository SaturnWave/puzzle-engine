using System;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Jam;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>The colours a Planner believes (TC21): open vehicles as they are, covers as guessed and then as seen.</summary>
    public sealed class ColourViewTests
    {
        [Fact]
        public void An_open_vehicle_shows_its_colour_and_a_cover_has_none_until_it_is_guessed()
        {
            // The brief's 6×6: covers 3 (blue) and 7 (red).
            var view = new ColourView(Load(SixBySix()));

            Assert.Equal(Red, view.Of(new PieceId(1)));
            Assert.Throws<InvalidOperationException>(() => view.Of(new PieceId(3)));
        }

        [Fact]
        public void A_cover_takes_its_guess_until_it_lifts_and_then_the_colour_seen()
        {
            var view = new ColourView(Load(SixBySix()));

            view.Guess(new PieceId(3), Red);
            ColourId guessed = view.Of(new PieceId(3));
            view.Guess(new PieceId(3), Yellow);
            ColourId guessedAgain = view.Of(new PieceId(3));
            view.Seen(new PieceId(3), Blue);

            Assert.Equal((Red, Yellow, Blue), (guessed, guessedAgain, view.Of(new PieceId(3))));
        }

        [Fact]
        public void Only_a_cover_still_hidden_can_be_guessed_or_seen()
        {
            var view = new ColourView(Load(SixBySix()));
            view.Seen(new PieceId(7), Red);

            Assert.Throws<InvalidOperationException>(() => view.Guess(new PieceId(1), Blue));
            Assert.Throws<InvalidOperationException>(() => view.Seen(new PieceId(1), Red));
            Assert.Throws<InvalidOperationException>(() => view.Guess(new PieceId(7), Blue));
            Assert.Throws<InvalidOperationException>(() => view.Seen(new PieceId(7), Red));
        }
    }
}

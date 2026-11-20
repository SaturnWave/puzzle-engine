using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>A Planner's guesses (Q71): the arrangements that keep each cover's reading and the ledger's exact counts.</summary>
    public sealed class CoverGuessesTests
    {
        [Fact]
        public void The_arrangements_are_ordered_by_cover_then_colour_and_keep_the_ledgers_counts()
        {
            // Covers 2 (Y), 3 (B), 7 (R), tags "2 not R" and "3 not R". At D1 each cover may be any ledger colour: the six
            // orders of R, B, Y. At D2, 2 and 3 are blue or yellow, so the ledger's one red must be 7's.
            LotLevel level = TwoNotRedTags();
            CoverReading atD1 = LotClues.Read(level, default, DeductionRung.D1);
            CoverReading atD2 = LotClues.Read(level, default, DeductionRung.D2);

            Assert.Equal(6, CoverGuesses.Count(atD1));
            Assert.Equal(
                new[] { "RBY", "RYB", "BRY", "BYR", "YRB", "YBR" },
                Enumerable.Range(0, 6).Select(index => Letters(CoverGuesses.At(atD1, index))));
            Assert.Equal(2, CoverGuesses.Count(atD2));
            Assert.Equal(new[] { "BYR", "YBR" }, Enumerable.Range(0, 2).Select(index => Letters(CoverGuesses.At(atD2, index))));
        }

        [Fact]
        public void A_draw_takes_one_index_from_the_stream_when_two_or_more_arrangements_fit_and_none_otherwise()
        {
            LotLevel level = TwoNotRedTags();
            CoverReading open = LotClues.Read(level, default, DeductionRung.D1);
            CoverReading settled = LotClues.Read(Load(SixBySix()), default, DeductionRung.D1);
            Pcg32 random = Pcg32.Seeded(4, 2);
            Pcg32 replay = Pcg32.Seeded(4, 2);

            ReadOnlyArray<ColourId> drawn = CoverGuesses.Draw(open, random);
            ReadOnlyArray<ColourId> expected = CoverGuesses.At(open, replay.NextIndex(6));
            Pcg32State afterOneDraw = random.Snapshot();
            ReadOnlyArray<ColourId> only = CoverGuesses.Draw(settled, random);

            Assert.Equal(expected, drawn);
            Assert.Equal(replay.Snapshot(), afterOneDraw);
            Assert.Equal(afterOneDraw, random.Snapshot());
            Assert.Equal(new[] { Blue, Red }, only);
        }

        [Fact]
        public void Colours_fit_a_reading_when_each_is_in_its_covers_set_and_the_counts_match_the_ledger()
        {
            CoverReading atD2 = LotClues.Read(TwoNotRedTags(), default, DeductionRung.D2);

            Assert.True(CoverGuesses.Fits(atD2, new[] { Yellow, Blue, Red }));
            Assert.False(CoverGuesses.Fits(atD2, new[] { Red, Blue, Yellow }));
            Assert.False(CoverGuesses.Fits(atD2, new[] { Blue, Blue, Red }));
            Assert.Throws<ArgumentException>(() => CoverGuesses.Fits(atD2, new[] { Yellow, Blue }));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoverGuesses.At(atD2, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => CoverGuesses.At(atD2, -1));
        }

        [Fact]
        public void At_the_start_planner1_guesses_on_the_d2_and_d3_levels_and_planner3_only_on_the_d3_levels()
        {
            // Q71's counts on the calibration pack: one arrangement wherever the ceiling reads every cover, six for
            // Planner1 on each D2 level (three covers, three colours), and 2-12 for both Planners on each D3 level.
            foreach (CampaignLevel campaign in CampaignLevels.Covered)
            {
                int planner1 = CoverGuesses.Count(LotClues.Read(campaign.Level, default, DeductionRung.D1));
                int planner3 = CoverGuesses.Count(LotClues.Read(campaign.Level, default, DeductionRung.D2));
                DeductionRung rung = campaign.Rung.Or(DeductionRung.D3);

                Assert.Equal(rung == DeductionRung.D1, planner1 == 1);
                Assert.Equal(rung != DeductionRung.D3, planner3 == 1);
                Assert.True(rung != DeductionRung.D2 || planner1 == 6, "Planner1 on a D2 level: " + planner1);
                Assert.True(rung != DeductionRung.D3 || (planner1 is >= 2 and <= 12 && planner3 is >= 2 and <= 12), "D3: " + planner1 + ", " + planner3);
            }
        }

        [Fact]
        public void In_play_the_true_colours_are_always_one_of_the_arrangements()
        {
            var legal = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.Covered)
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, 3);
                LotState state = rules.Start;
                SeenSet seen = default;
                while (rules.Evaluate(state) is Outcome.InPlay)
                {
                    foreach (DeductionRung ceiling in new[] { DeductionRung.D1, DeductionRung.D2 })
                    {
                        CoverReading reading = LotClues.Read(level, seen, ceiling);
                        Assert.True(CoverGuesses.Fits(reading, reading.Covers.Select(cover => level.VehicleOf(cover.Cover).Colour).ToList()));
                        Assert.True(CoverGuesses.Count(reading) >= 1);
                    }

                    legal.Clear();
                    rules.AppendLegalMoves(state, legal);
                    Exit move = legal[random.NextIndex(legal.Count)];
                    seen = level.VehicleOf(move.Piece).StartsCovered ? seen.With(new HiddenCell(move.Piece.Value)) : seen;
                    state = LookaheadTests.Applied(rules, state, move);
                }
            }
        }

        /// <summary>The brief's 6×6 with covers 2 (Y), 3 (B) and 7 (R), and tags "2 not R" and "3 not R".</summary>
        internal static LotLevel TwoNotRedTags() => Load(SixBySix() with
        {
            Vehicles = ReadOnlyArray.CopyOf(SixBySix().Vehicles.Select((spec, index) => spec with { StartsCovered = index is 2 or 3 or 7 })),
            Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(2), Red), new LotClue.Not(new PieceId(3), Red)),
        });

        private static string Letters(IEnumerable<ColourId> colours) =>
            string.Concat(colours.Select(colour => colour == Red ? "R" : colour == Blue ? "B" : colour == Yellow ? "Y" : "?"));
    }
}

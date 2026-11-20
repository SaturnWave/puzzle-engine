using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The bots' reading of the covers in play (LotClues.Read; Q71, Q72, Q78): the ladder's steps up to a ceiling, with
    /// the covers seen counted as open vehicles. Grade is untouched; LotCluesTests and the covered-sample pin check it.
    /// </summary>
    public sealed class CoverReadingTests
    {
        private static SeenSet NothingSeen => default;

        public static TheoryData<string> CoveredLevelPacks => new() { "campaign-jam", "rung-sample-00", "rung-sample-01" };

        [Theory]
        [MemberData(nameof(CoveredLevelPacks))]
        public void At_the_start_a_ceiling_fixes_every_cover_exactly_when_the_level_rung_is_within_it(string packText)
        {
            List<(int Slot, LotLevel Level, DeductionRung Rung)> levels = CoveredLevels(packText).ToList();
            Assert.Equal(new[] { DeductionRung.D1, DeductionRung.D2, DeductionRung.D3 }, levels.Select(level => level.Rung).Distinct().OrderBy(rung => rung));
            foreach ((_, LotLevel level, DeductionRung rung) in levels)
            {
                CoverReading atD1 = LotClues.Read(level, NothingSeen, DeductionRung.D1);
                CoverReading atD2 = LotClues.Read(level, NothingSeen, DeductionRung.D2);

                Assert.Equal(rung == DeductionRung.D1, FixesEvery(atD1));
                Assert.Equal(rung != DeductionRung.D3, FixesEvery(atD2));
                AssertTrueTo(level, NothingSeen, atD1);
                AssertTrueTo(level, NothingSeen, atD2);
            }
        }

        [Fact]
        public void In_play_a_reading_holds_the_truth_and_what_a_ceiling_read_in_full_at_the_start_it_reads_in_full_all_game()
        {
            // Seeing a cover tells at least what deducing it did, and a step never stops applying when more is known, so
            // a ceiling that fixed every cover at the start fixes every cover still hidden after any moves.
            foreach ((int slot, LotLevel level, DeductionRung rung) in CoveredLevels("campaign-jam"))
            {
                var rules = new JamRules(level);
                var moves = new List<Exit>();
                for (int playout = 0; playout < 20; playout++)
                {
                    Pcg32 random = Pcg32.Seeded((ulong)slot, (ulong)playout);
                    LotState state = rules.Start;
                    while (rules.Evaluate(state) is Outcome.InPlay)
                    {
                        SeenSet seen = CoversOut(level, state);
                        CoverReading atD1 = LotClues.Read(level, seen, DeductionRung.D1);
                        CoverReading atD2 = LotClues.Read(level, seen, DeductionRung.D2);
                        AssertTrueTo(level, seen, atD1);
                        AssertTrueTo(level, seen, atD2);
                        Assert.True(rung != DeductionRung.D1 || FixesEvery(atD1));
                        Assert.True(rung == DeductionRung.D3 || FixesEvery(atD2));
                        moves.Clear();
                        rules.AppendLegalMoves(state, moves);
                        state = ((MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, moves[random.NextIndex(moves.Count)])).Next;
                    }
                }
            }
        }

        [Fact]
        public void The_tag_of_a_cover_seen_says_nothing_more_and_the_ledger_counts_the_covers_left()
        {
            // The brief's 6×6: covers 3 (B) and 7 (R), tag "3 not R". With 3 seen, only 7 is hidden, the ledger holds one
            // red, and the ledger alone fixes 7 (ledger completion).
            LotLevel level = Load(SixBySix());

            CoverReading reading = LotClues.Read(level, Seen(3), DeductionRung.D1);

            Assert.Equal(new[] { Fixed(7, Red) }, reading.Covers);
            Assert.Equal((1, 0), (reading.LedgerCount(Red), reading.LedgerCount(Blue)));
        }

        [Fact]
        public void An_ahead_link_through_a_cover_seen_is_a_tag_about_one_cover()
        {
            // Covers 3 (B), 6 (R), 7 (R); tags "6 not B" and "7 same as ahead", whose reference is 6. At the start the link
            // works only through 6, a cover deduced earlier (D3), so a D2 reader fixes 6 and nothing more. Once 6 is seen,
            // the link reads against an open vehicle: 7 is red at D1, and the ledger leaves 3 blue.
            LotLevel level = Load(SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 3, 6, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(6), Blue), new LotClue.Ahead(new PieceId(7), AheadRelation.Same)),
            });

            CoverReading atStart = LotClues.Read(level, NothingSeen, DeductionRung.D2);
            CoverReading afterSix = LotClues.Read(level, Seen(6), DeductionRung.D1);

            Assert.Equal(new[] { Open(3, Red, Blue), Fixed(6, Red), Open(7, Red, Blue) }, atStart.Covers);
            Assert.Equal(new[] { Fixed(3, Blue), Fixed(7, Red) }, afterSix.Covers);
        }

        [Fact]
        public void An_ahead_tag_whose_own_vehicle_was_seen_first_is_a_tag_about_its_reference()
        {
            // Covers 3 (B), 6 (R), 7 (R); tag "7 same as ahead", whose reference is 6. Seeing 7 (red) while 6 is still
            // covered, as Insight can, leaves the relation about 6 alone: red at D1, and the ledger leaves 3 blue.
            LotLevel level = Load(SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 3, 6, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Ahead(new PieceId(7), AheadRelation.Same)),
            });

            CoverReading reading = LotClues.Read(level, Seen(7), DeductionRung.D1);

            Assert.Equal(new[] { Fixed(3, Blue), Fixed(6, Red) }, reading.Covers);
        }

        [Fact]
        public void A_touch_count_over_two_covers_is_about_the_other_one_once_one_is_seen()
        {
            // Covers 5 (Y), 7 (R), 8 (Y); tag 7 "touches 0 R" over its covered neighbours 5 and 8. At the start that is a
            // count over two covers (D3), so a D2 reader fixes nothing. Once 8 is seen (yellow), it is a tag about 5 alone:
            // not red, so yellow (D1), and the ledger leaves 7 red.
            LotLevel level = Load(SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 5, 7, 8),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Touch(new PieceId(7), Red, 0)),
            });

            CoverReading atStart = LotClues.Read(level, NothingSeen, DeductionRung.D2);
            CoverReading afterEight = LotClues.Read(level, Seen(8), DeductionRung.D1);

            Assert.All(atStart.Covers, cover => Assert.Equal(2, cover.Colours.Count));
            Assert.Equal(new[] { Fixed(5, Yellow), Fixed(7, Red) }, afterEight.Covers);
        }

        [Fact]
        public void The_touch_tag_of_a_vehicle_that_has_left_still_counts_its_neighbours()
        {
            // Covers 3 (B), 5 (Y), 7 (R), 8 (Y); tag 7 "touches 2 Y". Once 7 (the tag's own vehicle) and 8 have left, the
            // tag still says that 5 is yellow, with 8 counted out (Q78), and the ledger leaves 3 blue.
            LotLevel level = Load(SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 3, 5, 7, 8),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Touch(new PieceId(7), Yellow, 2)),
            });

            CoverReading reading = LotClues.Read(level, Seen(7, 8), DeductionRung.D1);

            Assert.Equal(new[] { Fixed(3, Blue), Fixed(5, Yellow) }, reading.Covers);
        }

        [Fact]
        public void A_d1_reader_keeps_the_colours_the_ledger_has_left_and_a_d2_reader_also_reads_tags_about_one_cover()
        {
            // The brief's D2 example: covers 2 (Y), 3 (B), 7 (R); tags "2 not R", "2 not B", "3 not R". "3 not R" leaves
            // 3 blue or yellow: no D1 step fixes anything, so a D1 reader keeps every ledger colour. A D2 reader takes
            // "2 not R" and "2 not B" together (yellow), then the rest follows.
            LotLevel level = Load(SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 2, 3, 7),
                Clues = ReadOnlyArray.Of<LotClue>(
                    new LotClue.Not(new PieceId(2), Red), new LotClue.Not(new PieceId(2), Blue), new LotClue.Not(new PieceId(3), Red)),
            });

            CoverReading atD1 = LotClues.Read(level, NothingSeen, DeductionRung.D1);
            CoverReading atD2 = LotClues.Read(level, NothingSeen, DeductionRung.D2);

            Assert.Equal(new[] { Open(2, Red, Blue, Yellow), Open(3, Red, Blue, Yellow), Open(7, Red, Blue, Yellow) }, atD1.Covers);
            Assert.Equal(new[] { Fixed(2, Yellow), Fixed(3, Blue), Fixed(7, Red) }, atD2.Covers);
        }

        [Fact]
        public void A_d2_reader_narrows_a_cover_by_the_tags_about_it_alone_and_never_counts_over_several_covers()
        {
            // Covers 2 (Y), 3 (B), 7 (R); tags "2 not R" and "3 not R". Each tag leaves its cover blue or yellow, so no
            // step fixes anything. Only counting the ledger's one red over all three covers (D3) would make 7 red.
            LotLevel level = Load(SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 2, 3, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(2), Red), new LotClue.Not(new PieceId(3), Red)),
            });

            CoverReading atD1 = LotClues.Read(level, NothingSeen, DeductionRung.D1);
            CoverReading atD2 = LotClues.Read(level, NothingSeen, DeductionRung.D2);

            Assert.Equal(new[] { Open(2, Red, Blue, Yellow), Open(3, Red, Blue, Yellow), Open(7, Red, Blue, Yellow) }, atD1.Covers);
            Assert.Equal(new[] { Open(2, Blue, Yellow), Open(3, Blue, Yellow), Open(7, Red, Blue, Yellow) }, atD2.Covers);
        }

        [Fact]
        public void A_bot_reads_at_d1_or_d2_and_tags_that_contradict_what_was_seen_are_an_engine_fault()
        {
            LotLevel contradicting = Load(SixBySix() with
            {
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), Red), new LotClue.Not(new PieceId(3), Blue)),
            });

            Assert.Throws<ArgumentOutOfRangeException>(() => LotClues.Read(Load(SixBySix()), NothingSeen, DeductionRung.D3));
            Assert.Throws<InvalidOperationException>(() => LotClues.Read(contradicting, NothingSeen, DeductionRung.D1));
        }

        /// <summary>The reading lists the covers still hidden in order, its ledger counts their true colours, and each set holds the true colour.</summary>
        private static void AssertTrueTo(LotLevel level, SeenSet seen, CoverReading reading)
        {
            IEnumerable<Vehicle> hidden = level.Vehicles.Where(vehicle => vehicle.StartsCovered && !seen.Contains(new HiddenCell(vehicle.Id.Value)));
            Assert.Equal(hidden.Select(vehicle => vehicle.Id), reading.Covers.Select(cover => cover.Cover));
            foreach (CoverColours cover in reading.Covers)
            {
                Assert.Contains(level.VehicleOf(cover.Cover).Colour, cover.Colours);
            }

            for (byte colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                Assert.Equal(hidden.Count(vehicle => vehicle.Colour.Value == colour), reading.LedgerCount(new ColourId(colour)));
            }
        }

        private static bool FixesEvery(CoverReading reading) => reading.Covers.All(cover => cover.Colours.Count == 1);

        /// <summary>The covered levels built in the pack's slots 0-99, with their slots and graded rungs.</summary>
        private static IEnumerable<(int Slot, LotLevel Level, DeductionRung Rung)> CoveredLevels(string packText)
        {
            PackId pack = PackId.Parse(packText).Or(default);
            for (int slot = 0; slot < 100; slot++)
            {
                if (LotPackBuilder.Build(pack, new LevelIndex(slot)) is BuildResult.Built built && built.Level.Deduction.TryGet(out DeductionGrade.Graded? grade))
                {
                    yield return (slot, built.Level.Level, grade.Rung);
                }
            }
        }

        /// <summary>The covers whose vehicles have left the lot: their colours were seen when the covers lifted.</summary>
        private static SeenSet CoversOut(LotLevel level, LotState state)
        {
            SeenSet seen = NothingSeen;
            foreach (Vehicle vehicle in level.Vehicles)
            {
                if (vehicle.StartsCovered && state.HasExited(vehicle.Id))
                {
                    seen = seen.With(new HiddenCell(vehicle.Id.Value));
                }
            }

            return seen;
        }

        private static SeenSet Seen(params int[] pieces) =>
            pieces.Aggregate(NothingSeen, (seen, piece) => seen.With(new HiddenCell((byte)piece)));

        private static CoverColours Fixed(int piece, ColourId colour) => new(new PieceId((byte)piece), ReadOnlyArray.Of(colour));

        private static CoverColours Open(int piece, params ColourId[] colours) =>
            new(new PieceId((byte)piece), ReadOnlyArray.CopyOf(colours.OrderBy(colour => colour.Value)));

        private static ReadOnlyArray<VehicleSpec> Covered(LotDefinition board, params int[] pieces) =>
            ReadOnlyArray.CopyOf(board.Vehicles.Select((spec, index) => spec with { StartsCovered = pieces.Contains(index) }));
    }
}

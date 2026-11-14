using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>The clue layer: uniqueness, the deduction grader and the covered generator (no-guess certificate).</summary>
    public sealed class LotCluesTests
    {
        private static readonly NodeBudget Budget = new(500_000);
        private static readonly PackId Pack = PackId.Parse("cover-test").Or(default);

        [Fact]
        public void The_briefs_example_is_unique_and_graded_d1_in_two_steps()
        {
            var clues = new LotClues(Load(SixBySix()));

            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));
            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));

            Assert.Equal(Option.Some(Blue), unique.Answer.ColourOf(new HiddenCell(3)));
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(7)));
            Assert.Equal(DeductionRung.D1, graded.Rung);
            Assert.Equal(new StepCount(2), graded.Steps);
            Assert.Equal(1, clues.LedgerCount(Red));
            Assert.Equal(0, clues.LedgerCount(Yellow));
        }

        [Fact]
        public void Without_its_tag_the_example_has_two_answers_and_needs_a_guess()
        {
            var clues = new LotClues(Load(SixBySix() with { Clues = ReadOnlyArray.Empty<LotClue>() }));

            Assert.IsType<Uniqueness.Multiple>(UniquenessCheck.Run(clues, Budget));
            Assert.IsType<DeductionGrade.NeedsGuess>(clues.Grade(Budget));
        }

        [Fact]
        public void Contradicting_tags_give_a_contradiction_and_no_grade()
        {
            LotDefinition board = SixBySix() with
            {
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), Red), new LotClue.Not(new PieceId(3), Blue)),
            };
            var clues = new LotClues(Load(board));

            Assert.IsType<Uniqueness.Contradiction>(UniquenessCheck.Run(clues, Budget));
            Assert.IsType<DeductionGrade.NeedsGuess>(clues.Grade(Budget));
        }

        [Fact]
        public void A_false_but_consistent_tag_gives_a_unique_answer_that_differs_from_the_truth()
        {
            // Why the certificate also compares the answer with the true colours.
            LotDefinition board = SixBySix() with { Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), Blue)) };

            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(new LotClues(Load(board)), Budget));

            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(3)));
        }

        [Fact]
        public void The_briefs_d2_example_needs_two_tags_about_one_cover_together()
        {
            // Covers 2 (Y), 3 (B) and 7 (R): the ledger holds R, B and Y once each. "2 not R" and "2 not B" leave 2
            // yellow only together (D2); then "3 not R" leaves 3 blue (D1) and the ledger leaves 7 red (D1).
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 2, 3, 7),
                Clues = ReadOnlyArray.Of<LotClue>(
                    new LotClue.Not(new PieceId(2), Red), new LotClue.Not(new PieceId(2), Blue), new LotClue.Not(new PieceId(3), Red)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D2, graded.Rung);
            Assert.Equal(new StepCount(3), graded.Steps);
            Assert.Equal(Option.Some(Yellow), unique.Answer.ColourOf(new HiddenCell(2)));
        }

        [Fact]
        public void One_tag_per_cover_that_leaves_two_colours_stalls_the_deduction()
        {
            // The D2 example without "2 not B": the ledger's count puts red on 7 (D3), but nothing tells 2 from 3
            // (yellow and blue either way round), so reasoning alone stalls.
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 2, 3, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(2), Red), new LotClue.Not(new PieceId(3), Red)),
            };
            var clues = new LotClues(Load(board));

            Assert.IsType<Uniqueness.Multiple>(UniquenessCheck.Run(clues, Budget));
            Assert.IsType<DeductionGrade.NeedsGuess>(clues.Grade(Budget));
        }

        [Fact]
        public void Three_tags_needed_together_about_one_cover_are_a_d2_step()
        {
            // Four covers of four colours, one each. The first cover's three Nots leave it its colour only all
            // together: tags about one cover alone combine the same way, two or more of them (D2). Then two Nots
            // fix the second cover (D2), one Not the third (D1) and the ledger the last (D1).
            LotLevel plain = PlainLevel(30);
            List<int> covers = FirstOfFourColours(plain);
            var tags = new List<LotClue>();
            for (int cover = 0; cover < covers.Count; cover++)
            {
                for (int later = cover + 1; later < covers.Count; later++)
                {
                    tags.Add(new LotClue.Not(new PieceId((byte)covers[cover]), plain.Vehicles[covers[later]].Colour));
                }
            }

            bool[] covered = Enumerable.Range(0, plain.VehicleCount).Select(covers.Contains).ToArray();
            var clues = new LotClues(Load(Definition(plain, covered, tags)));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));

            Assert.Equal(DeductionRung.D2, graded.Rung);
            Assert.Equal(new StepCount(4), graded.Steps);
        }

        [Fact]
        public void An_ahead_tag_against_an_open_vehicle_is_a_d1_step()
        {
            // ?< (7) faces west and the first vehicle on its path is the open R^ (6). "7 same as ahead" leaves 7 red
            // (D1), then the ledger leaves 3 blue (D1).
            LotDefinition board = SixBySix() with { Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Ahead(new PieceId(7), AheadRelation.Same)) };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));

            Assert.Equal(DeductionRung.D1, graded.Rung);
            Assert.Equal(new StepCount(2), graded.Steps);
        }

        [Fact]
        public void An_ahead_tag_against_a_cover_fixed_earlier_is_a_d3_step()
        {
            // The same tag with 6 covered too: covers 3 (B), 6 (R) and 7 (R), ledger R2 B1. "6 not B" leaves 6 red
            // (D1). "7 same as ahead" then works only through 6, a cover deduced earlier (D3). The ledger leaves 3 blue (D1).
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 3, 6, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(6), Blue), new LotClue.Ahead(new PieceId(7), AheadRelation.Same)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D3, graded.Rung);
            Assert.Equal(new StepCount(3), graded.Steps);
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(7)));
        }

        [Fact]
        public void An_ahead_link_combined_with_the_ledger_is_a_d3_deduction()
        {
            // Covers 0 (B), 3 (B), 6 (R), 7 (R): ledger R2 B2. "3 not R" makes 3 blue (D1). Then 7 "same as
            // ahead" links 7 to 6; neither the link nor the ledger alone fixes a cover, but together they force
            // 6 = 7 = red and 0 = blue (D3); the ledger then fixes 6 and 7 (D1).
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 0, 3, 6, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), Red), new LotClue.Ahead(new PieceId(7), AheadRelation.Same)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D3, graded.Rung);
            Assert.Equal(new StepCount(4), graded.Steps);
            Assert.Equal(Option.Some(Blue), unique.Answer.ColourOf(new HiddenCell(0)));
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(6)));
        }

        [Fact]
        public void A_touch_tag_whose_only_covered_neighbour_is_the_cover_is_a_d1_step()
        {
            // ?< (7) touches the open Y^ (5) and Y> (8). Cover 7 and 8 and tag 7 "touches 2 Y": with 5 counted
            // out, the tag is about 8 alone and leaves it yellow (D1); the ledger leaves 7 red (D1).
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 7, 8),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Touch(new PieceId(7), Yellow, 2)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));

            Assert.Equal(DeductionRung.D1, graded.Rung);
            Assert.Equal(new StepCount(2), graded.Steps);
        }

        [Fact]
        public void One_touch_count_across_two_covers_is_a_d3_deduction()
        {
            // ?< (7) touches Y^ (5) and Y> (8). Cover 5, 7 and 8 and tag 7 "touches 2 Y": a count over two
            // unknown covers makes both yellow (D3, one cover per step), then the ledger leaves red for 7 (D1).
            var clues = new LotClues(Load(TouchCountOverTwoCovers()));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D3, graded.Rung);
            Assert.Equal(new StepCount(3), graded.Steps);
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(7)));
        }

        [Fact]
        public void The_ledger_counting_over_several_unknown_covers_is_a_d3_step()
        {
            // The 6×6 board recoloured (4 Y, 5 R, 7 Y, 8 B; still three of each colour), covers 5 (R), 7 (Y) and
            // 8 (B): the ledger holds R, B and Y once each. "7 not R" and "8 not R" leave red only for 5, which the
            // ledger's count over three unknown covers fixes (D3). Then 7 "touches 1 B" works through 5, fixed
            // earlier, and leaves 8 blue (D3); the ledger leaves 7 yellow (D1).
            LotDefinition recoloured = SixBySix() with { Vehicles = Recoloured(SixBySix(), (4, Yellow), (5, Red), (7, Yellow), (8, Blue)) };
            LotDefinition board = recoloured with
            {
                Vehicles = Covered(recoloured, 5, 7, 8),
                Clues = ReadOnlyArray.Of<LotClue>(
                    new LotClue.Not(new PieceId(7), Red), new LotClue.Not(new PieceId(8), Red), new LotClue.Touch(new PieceId(7), Blue, 1)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D3, graded.Rung);
            Assert.Equal(new StepCount(3), graded.Steps);
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(5)));
            Assert.Equal(Option.Some(Blue), unique.Answer.ColourOf(new HiddenCell(8)));
        }

        [Fact]
        public void A_count_cut_off_by_the_budget_fixes_nothing_and_reports_the_budget()
        {
            // The two-cover Touch count needs seven search nodes; three cut it off part way.
            var clues = new LotClues(Load(TouchCountOverTwoCovers()));

            Assert.IsType<DeductionGrade.BudgetExceeded>(clues.Grade(new NodeBudget(3)));
        }

        [Fact]
        public void Graded_always_implies_unique_on_random_true_tag_sets()
        {
            // Soundness of the grader: a step only fixes a colour every answer agrees on, so whenever it fixes
            // every cover the brute force must find exactly that one answer, and it must be the truth.
            int graded = 0;
            for (int index = 0; index < 40; index++)
            {
                LotLevel plain = PlainLevel(index);
                Pcg32 random = Pcg32.Seeded((ulong)index, 99UL);
                bool[] covered = RandomCovers(plain, 2 + random.NextIndex(4), random);
                List<LotClue> tags = RandomTrueNots(plain, covered, random);
                LotLevel level = Load(Definition(plain, covered, tags));
                var clues = new LotClues(level);

                if (clues.Grade(Budget) is DeductionGrade.Graded grade)
                {
                    graded++;
                    var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));
                    Assert.True(AnswerIsTruth(level, unique.Answer));
                    Assert.Equal(new StepCount(clues.HiddenCells.Count), grade.Steps);
                }
            }

            Assert.True(graded >= 5, "Too few graded samples to say anything.");
        }

        [Fact]
        public void Graded_always_implies_unique_on_random_true_tags_of_every_family()
        {
            // The same soundness with Ahead and Touch tags too, so the D3 steps (counts, links, pairs) are covered.
            var rungs = new List<DeductionRung>();
            for (int index = 0; index < 40; index++)
            {
                LotLevel plain = PlainLevel(index);
                Pcg32 random = Pcg32.Seeded((ulong)index, 77UL);
                bool[] covered = RandomCovers(plain, 2 + random.NextIndex(4), random);
                List<LotClue> tags = TrueTags(plain, covered).Where(_ => random.NextIndex(3) == 0).ToList();
                LotLevel level = Load(Definition(plain, covered, tags));
                var clues = new LotClues(level);

                if (clues.Grade(Budget) is DeductionGrade.Graded grade)
                {
                    rungs.Add(grade.Rung);
                    var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));
                    Assert.True(AnswerIsTruth(level, unique.Answer));
                    Assert.Equal(new StepCount(clues.HiddenCells.Count), grade.Steps);
                }
            }

            Assert.True(rungs.Count >= 10, "Too few graded samples to say anything.");
            Assert.Contains(DeductionRung.D3, rungs);
        }

        [Fact]
        public void One_more_true_tag_never_makes_a_graded_deduction_harder()
        {
            // The coverer relies on this: it skips a tag that grades the level at another rung, since adding
            // more tags would never bring the rung back up.
            int checkedPairs = 0;
            for (int index = 0; index < 40; index++)
            {
                LotLevel plain = PlainLevel(index);
                Pcg32 random = Pcg32.Seeded((ulong)index, 55UL);
                bool[] covered = RandomCovers(plain, 2 + random.NextIndex(4), random);
                List<LotClue> all = TrueTags(plain, covered);
                List<LotClue> some = all.Where(_ => random.NextIndex(2) == 0).ToList();
                if (!(new LotClues(Load(Definition(plain, covered, some))).Grade(Budget) is DeductionGrade.Graded before))
                {
                    continue;
                }

                foreach (LotClue extra in all.Except(some))
                {
                    var more = new List<LotClue>(some) { extra };
                    var after = Assert.IsType<DeductionGrade.Graded>(new LotClues(Load(Definition(plain, covered, more))).Grade(Budget));
                    Assert.True(after.Rung <= before.Rung, "Adding " + extra + " raised " + before.Rung + " to " + after.Rung + ".");
                    checkedPairs++;
                }
            }

            Assert.True(checkedPairs >= 50, "Too few graded samples to say anything.");
        }

        [Fact]
        public void The_plans_covered_slots_pass_the_certificate_and_every_tag_is_needed()
        {
            // Every covered slot of the content plan (levels 20-99, D1 to D3): Unique against the true colours,
            // Graded at the plan's rung, one step per cover, the plain level's geometry, minimal tags.
            int planned = 0;
            int covered = 0;
            for (int index = LotContentPlan.FirstCoveredLevel; index < 100; index++)
            {
                var slot = new LevelIndex(index);
                if (!LotContentPlan.CoversFor(slot).TryGet(out CoverPlan? plan))
                {
                    continue;
                }

                planned++;
                if (!(LotGenerator.Generate(Pack, slot, LotContentPlan.For(slot)) is GenerateResult.Generated plain)
                    || !(LotCoverer.Cover(plain.Lot.Level, Pack, slot, plan) is CoverResult.Covered result))
                {
                    continue;
                }

                covered++;
                LotLevel level = result.Lot.Level;
                var clues = new LotClues(level);
                var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));
                Assert.True(AnswerIsTruth(level, unique.Answer));
                var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
                Assert.Equal(plan.Rung, graded.Rung);
                Assert.Equal(new StepCount(plan.Covers), graded.Steps);
                Assert.NotEmpty(level.Clues);
                Assert.Equal(plan.Covers, clues.HiddenCells.Count);
                Assert.Equal(VehiclesLine(plain.Lot.Level), VehiclesLine(level));

                // Minimal: dropping any one tag loses the deduction at the plan's rung.
                foreach (LotClue tag in level.Clues)
                {
                    var fewer = level.Clues.Where(other => !ReferenceEquals(other, tag)).ToList();
                    LotLevel weaker = Load(Definition(plain.Lot.Level, CoveredMask(level), fewer));
                    bool stillSameRung = new LotClues(weaker).Grade(Budget) is DeductionGrade.Graded weakerGrade && weakerGrade.Rung == plan.Rung;
                    Assert.False(stillSameRung, "A tag was not needed.");
                }
            }

            Assert.True(covered * 10 >= planned * 9, "Covered " + covered.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " of " + planned.ToString(System.Globalization.CultureInfo.InvariantCulture) + " planned slots.");
        }

        [Fact]
        public void Covering_is_deterministic()
        {
            LotLevel plain = PlainLevel(7);
            var plan = new CoverPlan(3, DeductionRung.D2);

            var first = Assert.IsType<CoverResult.Covered>(LotCoverer.Cover(plain, Pack, new LevelIndex(7), plan));
            var second = Assert.IsType<CoverResult.Covered>(LotCoverer.Cover(plain, Pack, new LevelIndex(7), plan));

            Assert.Equal(LotText.Write(first.Lot.Level), LotText.Write(second.Lot.Level));
        }

        /// <summary>The 6×6 board with 5, 7 and 8 covered and the tag 7 "touches 2 Y" over the two covered neighbours.</summary>
        private static LotDefinition TouchCountOverTwoCovers() => SixBySix() with
        {
            Vehicles = Covered(SixBySix(), 5, 7, 8),
            Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Touch(new PieceId(7), Yellow, 2)),
        };

        /// <summary>The pc-lot "vehicles=" line: the geometry and colours, which covering must not change.</summary>
        private static string VehiclesLine(LotLevel level) => LotText.Write(level).Split('\n')[5];

        private static ReadOnlyArray<VehicleSpec> Covered(LotDefinition board, params int[] pieces) =>
            ReadOnlyArray.CopyOf(board.Vehicles.Select((spec, index) => spec with { StartsCovered = pieces.Contains(index) }));

        private static ReadOnlyArray<VehicleSpec> Recoloured(LotDefinition board, params (int Piece, ColourId Colour)[] changes)
        {
            var vehicles = board.Vehicles.ToList();
            foreach ((int piece, ColourId colour) in changes)
            {
                vehicles[piece] = vehicles[piece] with { Colour = colour };
            }

            return ReadOnlyArray.CopyOf(vehicles);
        }

        private static LotLevel PlainLevel(int index)
        {
            var slot = new LevelIndex(index);
            return Assert.IsType<GenerateResult.Generated>(LotGenerator.Generate(Pack, slot, LotContentPlan.For(slot))).Lot.Level;
        }

        /// <summary>The lowest piece of each of the first four colours met in piece order.</summary>
        private static List<int> FirstOfFourColours(LotLevel plain)
        {
            var colours = new List<ColourId>();
            var pieces = new List<int>();
            for (int piece = 0; piece < plain.VehicleCount && pieces.Count < 4; piece++)
            {
                if (!colours.Contains(plain.Vehicles[piece].Colour))
                {
                    colours.Add(plain.Vehicles[piece].Colour);
                    pieces.Add(piece);
                }
            }

            Assert.Equal(4, pieces.Count);
            return pieces;
        }

        private static bool[] RandomCovers(LotLevel plain, int count, Pcg32 random)
        {
            var covered = new bool[plain.VehicleCount];
            int placed = 0;
            while (placed < count)
            {
                int pick = random.NextIndex(plain.VehicleCount);
                if (!covered[pick])
                {
                    covered[pick] = true;
                    placed++;
                }
            }

            return covered;
        }

        private static List<LotClue> RandomTrueNots(LotLevel plain, bool[] covered, Pcg32 random)
        {
            var tags = new List<LotClue>();
            for (int piece = 0; piece < plain.VehicleCount; piece++)
            {
                if (!covered[piece])
                {
                    continue;
                }

                for (byte colour = 1; colour <= 5; colour++)
                {
                    bool isTrue = plain.Vehicles[piece].Colour.Value != colour;
                    if (isTrue && random.NextIndex(2) == 0)
                    {
                        tags.Add(new LotClue.Not(new PieceId((byte)piece), new ColourId(colour)));
                    }
                }
            }

            return tags;
        }

        /// <summary>Every true tag on the covers, in a fixed order: Nots of colours 1-5, the Ahead tag where a blocker exists, Touch counts of colours 1-5.</summary>
        private static List<LotClue> TrueTags(LotLevel plain, bool[] covered)
        {
            var tags = new List<LotClue>();
            for (int index = 0; index < plain.VehicleCount; index++)
            {
                if (!covered[index])
                {
                    continue;
                }

                var piece = new PieceId((byte)index);
                ColourId colour = plain.VehicleOf(piece).Colour;
                for (byte other = 1; other <= 5; other++)
                {
                    if (other != colour.Value)
                    {
                        tags.Add(new LotClue.Not(piece, new ColourId(other)));
                    }
                }

                if (plain.FirstOnPathAtStart(piece).TryGet(out PieceId ahead))
                {
                    bool same = plain.VehicleOf(ahead).Colour == colour;
                    tags.Add(new LotClue.Ahead(piece, same ? AheadRelation.Same : AheadRelation.Differs));
                }

                IReadOnlyList<PieceId> neighbours = LotClues.Neighbours(plain, piece);
                for (byte touched = 1; touched <= 5; touched++)
                {
                    int count = neighbours.Count(neighbour => plain.VehicleOf(neighbour).Colour.Value == touched);
                    tags.Add(new LotClue.Touch(piece, new ColourId(touched), (byte)count));
                }
            }

            return tags;
        }

        private static bool[] CoveredMask(LotLevel level) => level.Vehicles.Select(vehicle => vehicle.StartsCovered).ToArray();

        private static LotDefinition Definition(LotLevel plain, bool[] covered, List<LotClue> tags)
        {
            var pillars = Enumerable.Range(0, 64).Where(cell => (plain.Pillars & (1UL << cell)) != 0).ToArray();
            var vehicles = plain.Vehicles.Select(vehicle => new VehicleSpec(vehicle.Colour, vehicle.Anchor.Index, vehicle.Length, vehicle.Facing, covered[vehicle.Id.Value]));
            return new LotDefinition(plain.Size.Width, plain.Size.Height, plain.Bays.Value, ReadOnlyArray.Of(pillars), ReadOnlyArray.CopyOf(vehicles), ReadOnlyArray.CopyOf(tags));
        }

        private static bool AnswerIsTruth(LotLevel level, HiddenAssignment answer)
        {
            for (int index = 0; index < answer.Cells.Count; index++)
            {
                if (level.VehicleOf(new PieceId(answer.Cells[index].Value)).Colour != answer.Colours[index])
                {
                    return false;
                }
            }

            return true;
        }
    }
}

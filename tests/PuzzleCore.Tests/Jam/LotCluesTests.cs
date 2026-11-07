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
        public void One_touch_count_across_two_covers_is_a_d2_deduction()
        {
            // ?< (7) touches Y^ (5) and Y> (8). Cover 5, 7 and 8 and tag 7 "touches 2 Y": both neighbours
            // are yellow at once (D2), then the ledger leaves red for 7 (D1).
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 5, 7, 8),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Touch(new PieceId(7), Yellow, 2)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D2, graded.Rung);
            Assert.Equal(new StepCount(2), graded.Steps);
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(7)));
        }

        [Fact]
        public void An_ahead_link_combined_with_the_ledger_is_a_d3_deduction()
        {
            // Covers 0 (B), 3 (B), 6 (R), 7 (R): ledger R2 B2. "3 not R" makes 3 blue (D1). Then 7 "same as
            // ahead" links 7 to 6; neither the link nor the ledger alone narrows anything, but together
            // they force 6 = 7 = red and 0 = blue (D3).
            LotDefinition board = SixBySix() with
            {
                Vehicles = Covered(SixBySix(), 0, 3, 6, 7),
                Clues = ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), Red), new LotClue.Ahead(new PieceId(7), AheadRelation.Same)),
            };
            var clues = new LotClues(Load(board));

            var graded = Assert.IsType<DeductionGrade.Graded>(clues.Grade(Budget));
            var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));

            Assert.Equal(DeductionRung.D3, graded.Rung);
            Assert.Equal(Option.Some(Blue), unique.Answer.ColourOf(new HiddenCell(0)));
            Assert.Equal(Option.Some(Red), unique.Answer.ColourOf(new HiddenCell(6)));
        }

        [Fact]
        public void Graded_always_implies_unique_on_random_true_tag_sets()
        {
            // Soundness of the grader: propagation only removes impossible colours, so whenever it resolves
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

                if (clues.Grade(Budget) is DeductionGrade.Graded)
                {
                    graded++;
                    var unique = Assert.IsType<Uniqueness.Unique>(UniquenessCheck.Run(clues, Budget));
                    Assert.True(AnswerIsTruth(level, unique.Answer));
                }
            }

            Assert.True(graded >= 5, "Too few graded samples to say anything.");
        }

        [Fact]
        public void The_plans_covered_slots_pass_the_certificate_and_every_tag_is_needed()
        {
            // Every covered slot of the content plan (levels 20-99, D1 to D3): Unique against the true colours,
            // Graded at the plan's rung with at least one deduction, the plain level's geometry, minimal tags.
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
                Assert.True(graded.Steps.Value >= 1);
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

        /// <summary>The pc-lot "vehicles=" line: the geometry and colours, which covering must not change.</summary>
        private static string VehiclesLine(LotLevel level) => LotText.Write(level).Split('\n')[5];

        private static ReadOnlyArray<VehicleSpec> Covered(LotDefinition board, params int[] pieces) =>
            ReadOnlyArray.CopyOf(board.Vehicles.Select((spec, index) => spec with { StartsCovered = pieces.Contains(index) }));

        private static LotLevel PlainLevel(int index)
        {
            var slot = new LevelIndex(index);
            return Assert.IsType<GenerateResult.Generated>(LotGenerator.Generate(Pack, slot, LotContentPlan.For(slot))).Lot.Level;
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

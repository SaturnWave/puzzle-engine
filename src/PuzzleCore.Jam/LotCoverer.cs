using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Randomness;
using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>How many vehicles to cover (2-5) and the deduction rung the level must need (its hardest step).</summary>
    public sealed record CoverPlan(int Covers, DeductionRung Rung);

    /// <summary>
    /// A covered level that passed the no-guess certificate: its tags fix every cover (Unique), reasoning
    /// alone gets there (Graded at exactly the plan's rung), and the geometry is the plain level's, so
    /// its PeakPar and par path still hold.
    /// </summary>
    public sealed record CoveredLot(LotLevel Level, DeductionGrade.Graded Grade, HiddenAssignment Answer, int Tries);

    public enum CoverFailure
    {
        /// <summary>No try reached a Unique, Graded tag set within the plan's rung.</summary>
        TriesExhausted,
    }

    public abstract record CoverResult
    {
        private CoverResult()
        {
        }

        public sealed record Covered(CoveredLot Lot) : CoverResult;

        public sealed record Rejected(CoverFailure Reason) : CoverResult;
    }

    /// <summary>
    /// Turns a plain level into a covered one (CoveredLot.md, generator step 6). Each try:
    /// 1. picks the vehicles to cover (Cover stream) and rejects a choice the ledger alone already solves
    ///    (for example every cover the same colour): such a level has nothing to deduce;
    /// 2. lists every TRUE tag (Not a colour it is not; Ahead where a blocker exists at the start; Touch
    ///    where a neighbour is covered) and shuffles them (Clues stream);
    /// 3. adds tags until the level is Graded at exactly the plan's rung, then drops each tag the
    ///    deduction does not need (keeping the rung), so the tag set is minimal;
    /// 4. checks Unique against the true colours.
    /// Covers never change geometry, so the plain level's solvability and PeakPar carry over.
    /// </summary>
    public static class LotCoverer
    {
        public const int TriesPerSlot = 20;
        private static readonly NodeBudget CheckBudget = new NodeBudget(200_000);

        public static CoverResult Cover(LotLevel plain, PackId pack, LevelIndex index, CoverPlan plan)
        {
            Seed seed = Seed.ForPack(pack);
            for (int attempt = 0; attempt < TriesPerSlot; attempt++)
            {
                Pcg32 coverStream = SeedDerivation.Stream(seed, StreamKey.ForGenerator(RandomPurpose.Cover, pack, index, attempt));
                Pcg32 clueStream = SeedDerivation.Stream(seed, StreamKey.ForGenerator(RandomPurpose.Clues, pack, index, attempt));
                if (TryOnce(plain, plan, coverStream, clueStream).TryGet(out CoveredLot? covered))
                {
                    return new CoverResult.Covered(covered with { Tries = attempt + 1 });
                }
            }

            return new CoverResult.Rejected(CoverFailure.TriesExhausted);
        }

        private static Option<CoveredLot> TryOnce(LotLevel plain, CoverPlan plan, Pcg32 coverStream, Pcg32 clueStream)
        {
            bool clustered = plan.Rung != DeductionRung.D1;
            bool[] covered = ChooseCovers(plain, plan.Covers, coverStream, clustered);
            bool solvedWithoutTags = Build(plain, covered, new List<LotClue>()).TryGet(out LotLevel? bare)
                && !(new LotClues(bare).Grade(CheckBudget) is DeductionGrade.NeedsGuess);
            if (solvedWithoutTags)
            {
                return Option.None<CoveredLot>();
            }

            // D2/D3 need tags that link several covers; try those first, single-cover tags after.
            (List<LotClue> linking, List<LotClue> single) = TrueTags(plain, covered);
            List<LotClue> candidates = clustered
                ? Shuffle(linking, clueStream).Concat(Shuffle(single, clueStream)).ToList()
                : Shuffle(linking.Concat(single).ToList(), clueStream);

            var chosen = new List<LotClue>();
            Option<DeductionGrade.Graded> grade = Option.None<DeductionGrade.Graded>();
            foreach (LotClue candidate in candidates)
            {
                chosen.Add(candidate);
                grade = GradeAt(plain, covered, chosen, plan.Rung);
                if (grade.HasValue)
                {
                    break;
                }

                // A tag that solves the level at another rung is skipped: more tags never make a solved
                // deduction harder, so keeping it would end the search at the wrong rung.
                if (IsGraded(plain, covered, chosen))
                {
                    chosen.RemoveAt(chosen.Count - 1);
                }
            }

            if (!grade.HasValue)
            {
                return Option.None<CoveredLot>();
            }

            // Minimise: every remaining tag is needed for the deduction.
            foreach (LotClue tag in chosen.ToList())
            {
                var without = chosen.Where(other => !ReferenceEquals(other, tag)).ToList();
                if (GradeAt(plain, covered, without, plan.Rung).HasValue)
                {
                    chosen = without;
                }
            }

            if (!Build(plain, covered, chosen).TryGet(out LotLevel? level))
            {
                return Option.None<CoveredLot>();
            }

            var clues = new LotClues(level);
            if (!(clues.Grade(CheckBudget) is DeductionGrade.Graded final)
                || final.Rung != plan.Rung
                || !(UniquenessCheck.Run(clues, CheckBudget) is Uniqueness.Unique unique)
                || !MatchesTruth(level, unique.Answer))
            {
                return Option.None<CoveredLot>();
            }

            return Option.Some(new CoveredLot(level, final, unique.Answer, 0));
        }

        /// <summary>The grade when reasoning alone resolves every cover and the hardest step is exactly <paramref name="rung"/>.</summary>
        private static Option<DeductionGrade.Graded> GradeAt(LotLevel plain, bool[] covered, List<LotClue> tags, DeductionRung rung)
        {
            if (!Build(plain, covered, tags).TryGet(out LotLevel? level))
            {
                return Option.None<DeductionGrade.Graded>();
            }

            return new LotClues(level).Grade(CheckBudget) is DeductionGrade.Graded graded && graded.Rung == rung
                ? Option.Some(graded)
                : Option.None<DeductionGrade.Graded>();
        }

        private static bool IsGraded(LotLevel plain, bool[] covered, List<LotClue> tags) =>
            Build(plain, covered, tags).TryGet(out LotLevel? level) && new LotClues(level).Grade(CheckBudget) is DeductionGrade.Graded;

        /// <summary>
        /// Covered vehicles drawn without replacement. Clustered draws (for D2/D3) prefer a vehicle that
        /// touches a cover or stands first on a cover's path, or has a cover first on its own path,
        /// so linking tags (Touch, Ahead between covers) exist.
        /// </summary>
        private static bool[] ChooseCovers(LotLevel plain, int covers, Pcg32 random, bool clustered)
        {
            var covered = new bool[plain.VehicleCount];
            for (int pick = 0; pick < covers; pick++)
            {
                var pool = new List<int>();
                var linked = new List<int>();
                for (int piece = 0; piece < plain.VehicleCount; piece++)
                {
                    if (covered[piece])
                    {
                        continue;
                    }

                    pool.Add(piece);
                    if (clustered && IsLinkedToCover(plain, new PieceId((byte)piece), covered))
                    {
                        linked.Add(piece);
                    }
                }

                List<int> from = linked.Count > 0 ? linked : pool;
                covered[from[random.NextIndex(from.Count)]] = true;
            }

            return covered;
        }

        private static bool IsLinkedToCover(LotLevel plain, PieceId piece, bool[] covered)
        {
            bool touchesCover = LotClues.Neighbours(plain, piece).Any(neighbour => covered[neighbour.Value]);
            bool coverAhead = plain.FirstOnPathAtStart(piece).TryGet(out PieceId ahead) && covered[ahead.Value];
            bool aheadOfCover = Enumerable.Range(0, plain.VehicleCount).Any(other =>
                covered[other] && plain.FirstOnPathAtStart(new PieceId((byte)other)).TryGet(out PieceId first) && first == piece);
            return touchesCover || coverAhead || aheadOfCover;
        }

        /// <summary>
        /// Every true tag that could inform a deduction, in a fixed order before shuffling, split into tags
        /// that link several covers (Touch over covered neighbours, Ahead against a covered blocker) and tags
        /// about one cover alone (Not, Ahead against an open blocker).
        /// </summary>
        private static (List<LotClue> Linking, List<LotClue> Single) TrueTags(LotLevel plain, bool[] covered)
        {
            var ledgerColours = new SortedSet<byte>();
            var levelColours = new SortedSet<byte>();
            for (int piece = 0; piece < plain.VehicleCount; piece++)
            {
                levelColours.Add(plain.Vehicles[piece].Colour.Value);
                if (covered[piece])
                {
                    ledgerColours.Add(plain.Vehicles[piece].Colour.Value);
                }
            }

            var linking = new List<LotClue>();
            var single = new List<LotClue>();
            for (int index = 0; index < plain.VehicleCount; index++)
            {
                if (!covered[index])
                {
                    continue;
                }

                var piece = new PieceId((byte)index);
                Vehicle vehicle = plain.VehicleOf(piece);
                foreach (byte colour in ledgerColours)
                {
                    if (colour != vehicle.Colour.Value)
                    {
                        single.Add(new LotClue.Not(piece, new ColourId(colour)));
                    }
                }

                if (plain.FirstOnPathAtStart(piece).TryGet(out PieceId ahead))
                {
                    bool same = plain.VehicleOf(ahead).Colour == vehicle.Colour;
                    (covered[ahead.Value] ? linking : single).Add(new LotClue.Ahead(piece, same ? AheadRelation.Same : AheadRelation.Differs));
                }

                IReadOnlyList<PieceId> neighbours = LotClues.Neighbours(plain, piece);
                bool hasCoveredNeighbour = neighbours.Any(neighbour => covered[neighbour.Value]);
                if (hasCoveredNeighbour)
                {
                    foreach (byte colour in ledgerColours)
                    {
                        int touching = neighbours.Count(neighbour => plain.VehicleOf(neighbour).Colour.Value == colour);
                        linking.Add(new LotClue.Touch(piece, new ColourId(colour), (byte)touching));
                    }
                }
            }

            return (linking, single);
        }

        private static List<LotClue> Shuffle(List<LotClue> tags, Pcg32 random)
        {
            // Fisher-Yates from the end; deterministic for a given stream.
            for (int last = tags.Count - 1; last > 0; last--)
            {
                int swap = random.NextIndex(last + 1);
                (tags[last], tags[swap]) = (tags[swap], tags[last]);
            }

            return tags;
        }

        private static Option<LotLevel> Build(LotLevel plain, bool[] covered, List<LotClue> tags)
        {
            var vehicles = new List<VehicleSpec>();
            foreach (Vehicle vehicle in plain.Vehicles)
            {
                vehicles.Add(new VehicleSpec(vehicle.Colour, vehicle.Anchor.Index, vehicle.Length, vehicle.Facing, covered[vehicle.Id.Value]));
            }

            var pillars = new List<int>();
            for (int cell = 0; cell <= LotCell.MaxIndex; cell++)
            {
                if ((plain.Pillars & (1UL << cell)) != 0)
                {
                    pillars.Add(cell);
                }
            }

            var definition = new LotDefinition(plain.Size.Width, plain.Size.Height, plain.Bays.Value,
                ReadOnlyArray.CopyOf(pillars), ReadOnlyArray.CopyOf(vehicles), ReadOnlyArray.CopyOf(tags));
            return LotLoader.Load(definition) is LoadResult.Loaded loaded ? Option.Some(loaded.Level) : Option.None<LotLevel>();
        }

        private static bool MatchesTruth(LotLevel level, HiddenAssignment answer)
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

using System;
using System.Collections.Generic;
using PuzzleCore.Play;
using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>A hidden cover and the colours a reading leaves it, ascending: one colour when the reading fixed it.</summary>
    public sealed record CoverColours(PieceId Cover, ReadOnlyArray<ColourId> Colours);

    /// <summary>
    /// What a bot reads about the covers still hidden in play (<see cref="LotClues.Read"/>; Q71, Q72): each hidden cover,
    /// ascending, with the colours it may have, and the ledger now (how many hidden covers have each colour).
    /// </summary>
    public sealed class CoverReading
    {
        private readonly int[] _ledger;

        internal CoverReading(ReadOnlyArray<CoverColours> covers, int[] ledger)
        {
            Covers = covers;
            _ledger = ledger;
        }

        public ReadOnlyArray<CoverColours> Covers { get; }

        /// <summary>How many of the hidden covers have this colour.</summary>
        public int LedgerCount(ColourId colour) => _ledger[colour.Value];
    }

    /// <summary>
    /// The clue model of a covered lot at the level start (CoveredLot.md, "Clue checker specifics").
    /// Hidden cells are the covered vehicles (HiddenCell = PieceId). Only public information enters:
    /// open vehicles' colours, the ledger (how many covered vehicles have each colour), the tags and the
    /// start geometry. Constraints: Not(c); Ahead(Same|Differs) against the first vehicle on the path
    /// at the start; Touch(c, n) over the vehicles sharing an edge with the footprint; the exact ledger.
    /// <para>
    /// Jam's steps on the one ladder (shared-design.md §2, "The deduction ladder"); each step fixes one cover.
    /// D1: one tag about that cover alone (Not, Ahead against an open vehicle, Touch whose only covered
    /// neighbour it is) leaves it one of the colours the ledger has left, or the ledger has one colour left
    /// (ledger completion, Q41). D2: only the tags about that cover alone, together, leave it one colour.
    /// D3: a constraint over several covers leaves it one colour, alone or with one more such constraint: a
    /// Touch count, an Ahead link (also one to a cover fixed earlier), or the ledger counting over several
    /// unknown covers (Q41). Anything more is NeedsGuess.
    /// </para>
    /// <para>
    /// A Touch that says the same of each of its covers (none of its covered neighbours has the colour, or every
    /// one has it) counts over its covers while two or more are unknown (D3). Once its other covers are fixed,
    /// it tells the last one the same whatever they turned out to be, so it is then a tag about that cover
    /// alone, read at D1 or D2.
    /// </para>
    /// <para>
    /// Sets of colours are bit masks over the colour ids 1-15 (bit n is colour n): the grader's inner loops
    /// intersect them on every step and every enumerated assignment.
    /// </para>
    /// <para>
    /// <see cref="Read"/> is the bots' reading in play (P5): the same steps up to a ceiling, with the covers already seen
    /// counted as open vehicles. <see cref="Grade"/> and the public model always read the level start.
    /// </para>
    /// </summary>
    public sealed class LotClues : IClueModel
    {
        // Every colour id, 1-15, as a set.
        private static readonly ushort AllColours =
            (ushort)(((1 << (ColourId.MaxValue + 1)) - 1) & ~((1 << ColourId.MinValue) - 1));

        private readonly int[] _ledger = new int[ColourId.MaxValue + 1];
        private readonly List<ColourId> _ledgerColours = new List<ColourId>();

        // Every tag and the ledger, for the consistency checks. A tag over no covered vehicle (a Touch with no
        // covered neighbour) is only in here: no step reads it, and only the contradiction check can break it.
        private readonly List<Rule> _rules = new List<Rule>();

        // Tags over a single cover, each with the colours it leaves that cover. With the Touch tags that have come
        // down to one cover (OwnTagsNow), the D1 and D2 steps read them.
        private readonly List<OwnTag> _ownTags = new List<OwnTag>();

        // Constraints over several covers (Touch counts, Ahead links), then the ledger's count: the D3 steps.
        private readonly List<Rule> _wideRules = new List<Rule>();

        // A tag that now speaks only of open vehicles (a Not on a cover seen, an Ahead between two open vehicles) and is
        // false of their colours. It puts no rule on the covers still hidden, so only Read's check can report it.
        private readonly bool _aSeenColourBreaksATag;

        public LotClues(LotLevel level)
            : this(level, default)
        {
        }

        /// <summary>
        /// The model with the covered vehicles in <paramref name="seen"/> counted as open: their colours were seen when
        /// their covers lifted (Q20, Q72). The ledger counts the covers still hidden. With nothing seen, this is the start.
        /// </summary>
        private LotClues(LotLevel level, SeenSet seen)
        {
            var hidden = new List<HiddenCell>();
            var positionOf = new int[level.VehicleCount];
            for (int piece = 0; piece < level.VehicleCount; piece++)
            {
                positionOf[piece] = -1;
                Vehicle vehicle = level.Vehicles[piece];
                if (vehicle.StartsCovered && !seen.Contains(new HiddenCell((byte)piece)))
                {
                    positionOf[piece] = hidden.Count;
                    hidden.Add(new HiddenCell((byte)piece));
                    _ledger[vehicle.Colour.Value]++;
                }
            }

            HiddenCells = ReadOnlyArray.CopyOf(hidden);
            for (byte colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                if (_ledger[colour] > 0)
                {
                    _ledgerColours.Add(new ColourId(colour));
                }
            }

            foreach (LotClue clue in level.Clues)
            {
                if (!RuleFor(level, clue, positionOf).TryGet(out Rule? rule))
                {
                    _aSeenColourBreaksATag |= !HoldsOnOpenVehicles(level, clue);
                    continue;
                }

                _rules.Add(rule);
                if (rule.Positions.Length == 1)
                {
                    _ownTags.Add(new OwnTag(rule.Positions[0], ColoursAllowedBy(rule)));
                }
                else if (rule.Positions.Length >= 2)
                {
                    _wideRules.Add(rule);
                }
            }

            var all = new int[hidden.Count];
            for (int position = 0; position < all.Length; position++)
            {
                all[position] = position;
            }

            var ledgerRule = new LedgerRule(all, _ledger);
            _rules.Add(ledgerRule);
            _wideRules.Add(ledgerRule);
        }

        public ReadOnlyArray<HiddenCell> HiddenCells { get; }

        /// <summary>Covered vehicles of each colour in the lot at the start (always shown on covered levels).</summary>
        public int LedgerCount(ColourId colour) => _ledger[colour.Value];

        public void AppendCandidates(int position, List<ColourId> into) => into.AddRange(_ledgerColours);

        public bool IsConsistent(PartialAssignment assignment)
        {
            foreach (Rule rule in _rules)
            {
                if (!rule.IsConsistent(assignment))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Grades the deduction the way a careful player reasons: one cover at a time, always with the easiest
        /// step available (D1 before D2 before D3), never with a what-if split. Between steps the player knows
        /// the covers fixed so far; the tags and the ledger are read afresh at every step. Each step fixes one
        /// cover, so Steps is the cover count on a graded level; Rung is the hardest step needed.
        /// </summary>
        public DeductionGrade Grade(NodeBudget budget)
        {
            var fixedColours = new Option<ColourId>[HiddenCells.Count];
            var counter = new NodeCounter(budget.Nodes);
            DeductionRung hardest = DeductionRung.D1;
            int steps = 0;
            while (true)
            {
                ushort coloursLeft = ColoursLeft(fixedColours);
                List<OwnTag> ownTags = OwnTagsNow(fixedColours);
                ushort[] domains = DomainsFor(fixedColours, coloursLeft, ownTags);
                if (IsContradicted(domains))
                {
                    return new DeductionGrade.NeedsGuess();
                }

                if (AllFixed(fixedColours))
                {
                    return new DeductionGrade.Graded(hardest, new StepCount(steps));
                }

                Option<Fix> step = EasiestStep(fixedColours, coloursLeft, ownTags, domains, counter);
                if (counter.IsOver)
                {
                    return new DeductionGrade.BudgetExceeded(new NodeCount(counter.Nodes));
                }

                if (!step.TryGet(out Fix fix))
                {
                    return new DeductionGrade.NeedsGuess();
                }

                fixedColours[fix.Position] = Option.Some(fix.Colour);
                hardest = DeductionRungs.Max(hardest, fix.Rung);
                steps++;
            }
        }

        /// <summary>
        /// What a bot reads about the covers still hidden in play (Q71, Q72, Q78). The covered vehicles in
        /// <paramref name="seen"/> count as open, because their colours were seen when they left; the ledger counts the
        /// covers still hidden; every tag is read, those of vehicles that have left included. The ladder's steps up to
        /// <paramref name="ceiling"/> (D1, or D1 and D2) are taken easiest first, as <see cref="Grade"/> takes them, until
        /// none applies. A cover a step fixed keeps its colour. Any other keeps the colours the ledger has left at D1, and
        /// those narrowed by every tag about that cover alone at D2; rules over several covers are never read. Throws
        /// InvalidOperationException when the tags contradict what was seen, which a certified level never does.
        /// </summary>
        public static CoverReading Read(LotLevel level, SeenSet seen, DeductionRung ceiling)
        {
            if (ceiling is not (DeductionRung.D1 or DeductionRung.D2))
            {
                throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "A bot reads covers at D1 or D2.");
            }

            var clues = new LotClues(level, seen);
            if (clues._aSeenColourBreaksATag)
            {
                throw new InvalidOperationException("A tag contradicts the colours seen.");
            }

            var fixedColours = new Option<ColourId>[clues.HiddenCells.Count];
            while (true)
            {
                ushort coloursLeft = clues.ColoursLeft(fixedColours);
                List<OwnTag> ownTags = clues.OwnTagsNow(fixedColours);
                ushort[] domains = DomainsFor(fixedColours, coloursLeft, ownTags);
                if (clues.IsContradicted(domains))
                {
                    throw new InvalidOperationException("The tags contradict the colours seen.");
                }

                if (AllFixed(fixedColours) || !StepUpTo(ceiling, fixedColours, coloursLeft, ownTags, domains).TryGet(out Fix fix))
                {
                    return clues.ReadingOf(fixedColours, ceiling == DeductionRung.D2 ? domains : null, coloursLeft);
                }

                fixedColours[fix.Position] = Option.Some(fix.Colour);
            }
        }

        /// <summary>The easiest step at or below the ceiling (D1 before D2), or none.</summary>
        private static Option<Fix> StepUpTo(DeductionRung ceiling, Option<ColourId>[] fixedColours, ushort coloursLeft, List<OwnTag> ownTags, ushort[] domains)
        {
            Option<Fix> byOneTag = D1Step(fixedColours, coloursLeft, ownTags);
            return byOneTag.HasValue || ceiling == DeductionRung.D1 ? byOneTag : D2Step(fixedColours, domains);
        }

        /// <summary>
        /// Each hidden cover's colours: a fixed cover its colour; any other its domain when the reader reads the tags about
        /// it alone (D2), or else the colours the ledger has left (D1).
        /// </summary>
        private CoverReading ReadingOf(Option<ColourId>[] fixedColours, ushort[]? ownTagDomains, ushort coloursLeft)
        {
            var covers = new List<CoverColours>();
            for (int position = 0; position < fixedColours.Length; position++)
            {
                ushort colours = fixedColours[position].TryGet(out ColourId colour) ? Bit(colour.Value)
                    : ownTagDomains is null ? coloursLeft
                    : ownTagDomains[position];
                covers.Add(new CoverColours(new PieceId(HiddenCells[position].Value), ColoursIn(colours)));
            }

            return new CoverReading(ReadOnlyArray.CopyOf(covers), (int[])_ledger.Clone());
        }

        /// <summary>The colours in a set, ascending.</summary>
        private static ReadOnlyArray<ColourId> ColoursIn(ushort colours)
        {
            var list = new List<ColourId>();
            for (byte colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                if ((colours & Bit(colour)) != 0)
                {
                    list.Add(new ColourId(colour));
                }
            }

            return ReadOnlyArray.CopyOf(list);
        }

        /// <summary>The ledger colours some cover still has to take: a colour the fixed covers used up is off (ledger completion).</summary>
        private ushort ColoursLeft(Option<ColourId>[] fixedColours)
        {
            var left = (int[])_ledger.Clone();
            foreach (Option<ColourId> fixedColour in fixedColours)
            {
                if (fixedColour.TryGet(out ColourId colour))
                {
                    left[colour.Value]--;
                }
            }

            ushort coloursLeft = 0;
            foreach (ColourId colour in _ledgerColours)
            {
                if (left[colour.Value] > 0)
                {
                    coloursLeft |= Bit(colour.Value);
                }
            }

            return coloursLeft;
        }

        /// <summary>
        /// The tags about one cover alone at this step: those over a single cover, plus each Touch that says the
        /// same of all its covers and has only one of them left unfixed.
        /// </summary>
        private List<OwnTag> OwnTagsNow(Option<ColourId>[] fixedColours)
        {
            var ownTags = new List<OwnTag>(_ownTags);
            foreach (Rule rule in _wideRules)
            {
                if (rule.EachCoverAllows.TryGet(out ushort allows) && LastUnfixed(rule, fixedColours).TryGet(out int position))
                {
                    ownTags.Add(new OwnTag(position, allows));
                }
            }

            return ownTags;
        }

        /// <summary>The rule's only cover not fixed yet; none when no cover or several are left.</summary>
        private static Option<int> LastUnfixed(Rule rule, Option<ColourId>[] fixedColours)
        {
            Option<int> last = Option.None<int>();
            foreach (int position in rule.Positions)
            {
                if (fixedColours[position].HasValue)
                {
                    continue;
                }

                if (last.HasValue)
                {
                    return Option.None<int>();
                }

                last = Option.Some(position);
            }

            return last;
        }

        /// <summary>
        /// Each cover's colours at this step: a fixed cover its colour; any other the colours left that every tag
        /// about it alone allows. A D3 step counts over these.
        /// </summary>
        private static ushort[] DomainsFor(Option<ColourId>[] fixedColours, ushort coloursLeft, List<OwnTag> ownTags)
        {
            var domains = new ushort[fixedColours.Length];
            for (int position = 0; position < domains.Length; position++)
            {
                domains[position] = fixedColours[position].TryGet(out ColourId colour) ? Bit(colour.Value) : coloursLeft;
            }

            foreach (OwnTag tag in ownTags)
            {
                if (!fixedColours[tag.Position].HasValue)
                {
                    domains[tag.Position] &= tag.Allows;
                }
            }

            return domains;
        }

        /// <summary>Whether a cover has no colour left, or the covers already known break a tag or the ledger.</summary>
        private bool IsContradicted(ushort[] domains)
        {
            var values = new byte[domains.Length];
            for (int position = 0; position < domains.Length; position++)
            {
                if (domains[position] == 0)
                {
                    return true;
                }

                if (IsSingle(domains[position]))
                {
                    values[position] = SingleColour(domains[position]);
                }
            }

            foreach (Rule rule in _rules)
            {
                if (rule.UnknownCount(domains) == 0 && !rule.Holds(values))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The easiest step available now, D1 before D2 before D3, or none when reasoning stalls.</summary>
        private Option<Fix> EasiestStep(Option<ColourId>[] fixedColours, ushort coloursLeft, List<OwnTag> ownTags, ushort[] domains, NodeCounter counter)
        {
            if (D1Step(fixedColours, coloursLeft, ownTags).TryGet(out Fix byOneTag))
            {
                return Option.Some(byOneTag);
            }

            if (D2Step(fixedColours, domains).TryGet(out Fix byTagsTogether))
            {
                return Option.Some(byTagsTogether);
            }

            return D3Step(domains, counter);
        }

        /// <summary>
        /// D1, on the lowest cover it fixes: the ledger has one colour left (ledger completion), or one tag about
        /// that cover alone leaves it one of the colours left.
        /// </summary>
        private static Option<Fix> D1Step(Option<ColourId>[] fixedColours, ushort coloursLeft, List<OwnTag> ownTags)
        {
            for (int position = 0; position < fixedColours.Length; position++)
            {
                if (fixedColours[position].HasValue)
                {
                    continue;
                }

                if (OnlyColour(coloursLeft).TryGet(out ColourId last))
                {
                    return Option.Some(new Fix(position, last, DeductionRung.D1));
                }

                foreach (OwnTag tag in ownTags)
                {
                    if (tag.Position == position && OnlyColour((ushort)(tag.Allows & coloursLeft)).TryGet(out ColourId colour))
                    {
                        return Option.Some(new Fix(position, colour, DeductionRung.D1));
                    }
                }
            }

            return Option.None<Fix>();
        }

        /// <summary>
        /// D2, on the lowest cover it fixes: the tags about that cover alone leave it one colour together. None
        /// of them did alone, or D1 would have fixed it.
        /// </summary>
        private static Option<Fix> D2Step(Option<ColourId>[] fixedColours, ushort[] domains)
        {
            for (int position = 0; position < domains.Length; position++)
            {
                if (!fixedColours[position].HasValue && OnlyColour(domains[position]).TryGet(out ColourId colour))
                {
                    return Option.Some(new Fix(position, colour, DeductionRung.D2));
                }
            }

            return Option.None<Fix>();
        }

        /// <summary>
        /// D3: one constraint over several covers, or two such constraints together, leave a cover one colour,
        /// counting over the colours each cover has at this step. Every unfixed cover has two colours or more
        /// here, or D1 or D2 would have fixed it, so the covers with one colour are exactly the fixed ones.
        /// </summary>
        private Option<Fix> D3Step(ushort[] domains, NodeCounter counter)
        {
            foreach (Rule rule in _wideRules)
            {
                if (FixedBy(new[] { rule }, domains, counter).TryGet(out Fix byOne))
                {
                    return Option.Some(byOne);
                }
            }

            for (int first = 0; first < _wideRules.Count; first++)
            {
                for (int second = first + 1; second < _wideRules.Count; second++)
                {
                    Rule one = _wideRules[first];
                    Rule other = _wideRules[second];
                    if (one.SharesUnknownWith(other, domains) && FixedBy(new[] { one, other }, domains, counter).TryGet(out Fix byPair))
                    {
                        return Option.Some(byPair);
                    }
                }
            }

            return Option.None<Fix>();
        }

        /// <summary>
        /// The lowest unknown cover the rules leave one colour, over every assignment of their unknown covers
        /// that satisfies them all. None once the node budget runs out, because a cut-off count proves nothing;
        /// the guard is defensive, since Grade reports the overrun before it applies any step.
        /// </summary>
        private static Option<Fix> FixedBy(Rule[] rules, ushort[] domains, NodeCounter counter)
        {
            List<int> unknown = UnknownPositions(rules, domains);
            var supported = new ushort[unknown.Count];
            Enumerate(rules, domains, unknown, 0, new byte[domains.Length], supported, counter);
            if (counter.IsOver)
            {
                return Option.None<Fix>();
            }

            for (int index = 0; index < unknown.Count; index++)
            {
                if (OnlyColour(supported[index]).TryGet(out ColourId colour))
                {
                    return Option.Some(new Fix(unknown[index], colour, DeductionRung.D3));
                }
            }

            return Option.None<Fix>();
        }

        /// <summary>Enumerates the unknown positions over their domains (resolved ones fixed); marks supported colours.</summary>
        private static bool Enumerate(Rule[] rules, ushort[] domains, List<int> unknown, int index, byte[] values, ushort[] supported, NodeCounter counter)
        {
            if (counter.IsOver)
            {
                return false;
            }

            counter.Count();
            if (index == unknown.Count)
            {
                FillResolved(rules, domains, values);
                foreach (Rule rule in rules)
                {
                    if (!rule.Holds(values))
                    {
                        return false;
                    }
                }

                for (int marked = 0; marked < unknown.Count; marked++)
                {
                    supported[marked] |= Bit(values[unknown[marked]]);
                }

                return true;
            }

            bool any = false;
            ushort domain = domains[unknown[index]];
            for (byte colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                if ((domain & (1 << colour)) != 0)
                {
                    values[unknown[index]] = colour;
                    any |= Enumerate(rules, domains, unknown, index + 1, values, supported, counter);
                }
            }

            return any;
        }

        private static void FillResolved(Rule[] rules, ushort[] domains, byte[] values)
        {
            foreach (Rule rule in rules)
            {
                foreach (int position in rule.Positions)
                {
                    if (IsSingle(domains[position]))
                    {
                        values[position] = SingleColour(domains[position]);
                    }
                }
            }
        }

        /// <summary>The rules' positions with more than one colour, ascending.</summary>
        private static List<int> UnknownPositions(Rule[] rules, ushort[] domains)
        {
            var inRules = new bool[domains.Length];
            foreach (Rule rule in rules)
            {
                foreach (int position in rule.Positions)
                {
                    inRules[position] = true;
                }
            }

            var unknown = new List<int>();
            for (int position = 0; position < domains.Length; position++)
            {
                if (inRules[position] && !IsSingle(domains[position]))
                {
                    unknown.Add(position);
                }
            }

            return unknown;
        }

        /// <summary>The colours a tag about one cover leaves it, tried one by one.</summary>
        private static ushort ColoursAllowedBy(Rule tag)
        {
            int position = tag.Positions[0];
            var values = new byte[position + 1];
            ushort allows = 0;
            for (byte colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                values[position] = colour;
                if (tag.Holds(values))
                {
                    allows |= Bit(colour);
                }
            }

            return allows;
        }

        private static bool AllFixed(Option<ColourId>[] fixedColours) => System.Array.TrueForAll(fixedColours, colour => colour.HasValue);

        private static ushort Bit(byte colour) => (ushort)(1 << colour);

        private static bool IsSingle(ushort domain) => domain != 0 && (domain & (domain - 1)) == 0;

        /// <summary>The one colour in the set, or none when it holds no colour or several.</summary>
        private static Option<ColourId> OnlyColour(ushort colours) =>
            IsSingle(colours) ? Option.Some(new ColourId(SingleColour(colours))) : Option.None<ColourId>();

        private static byte SingleColour(ushort domain)
        {
            byte colour = 0;
            while ((domain >> colour) != 1)
            {
                colour++;
            }

            return colour;
        }

        /// <summary>
        /// The rule a tag puts on the hidden covers. A tag sits on a covered vehicle; once that vehicle's colour is seen, a
        /// Not about it says nothing more, so it has none. A Touch always has one: it speaks of the neighbours.
        /// </summary>
        private static Option<Rule> RuleFor(LotLevel level, LotClue clue, int[] positionOf) => clue switch
        {
            LotClue.Not not => positionOf[not.On.Value] >= 0
                ? Option.Some<Rule>(new NotRule(positionOf[not.On.Value], not.Colour))
                : Option.None<Rule>(),
            LotClue.Ahead ahead => AheadRuleFor(level, ahead, positionOf),
            LotClue.Touch touch => Option.Some(TouchRuleFor(level, touch, positionOf)),
            _ => throw new ArgumentOutOfRangeException(nameof(clue), "Unknown clue family."),
        };

        /// <summary>
        /// Same or differs between the tag's vehicle and its start reference, each either hidden or open (open: never
        /// covered, or seen). Both open says nothing more. When only the reference is hidden (its tag's vehicle seen through
        /// Insight), the relation, being symmetric, becomes a tag about the reference.
        /// </summary>
        private static Option<Rule> AheadRuleFor(LotLevel level, LotClue.Ahead ahead, int[] positionOf)
        {
            // The loader guarantees a blocker at the start for every Ahead tag.
            level.FirstOnPathAtStart(ahead.On).TryGet(out PieceId reference);
            int holder = positionOf[ahead.On.Value];
            int referenceAt = positionOf[reference.Value];
            bool same = ahead.Relation == AheadRelation.Same;
            if (holder >= 0)
            {
                return Option.Some<Rule>(referenceAt >= 0
                    ? new AheadRule(holder, referenceAt, 0, same)
                    : new AheadRule(holder, -1, level.VehicleOf(reference).Colour.Value, same));
            }

            return referenceAt >= 0
                ? Option.Some<Rule>(new AheadRule(referenceAt, -1, level.VehicleOf(ahead.On).Colour.Value, same))
                : Option.None<Rule>();
        }

        /// <summary>
        /// Whether a tag that puts no rule on the hidden covers is true of the open vehicles it speaks of: a Not about a cover
        /// seen, or an Ahead whose vehicle and reference are both open. Only those two lose their rule (<see cref="RuleFor"/>).
        /// </summary>
        private static bool HoldsOnOpenVehicles(LotLevel level, LotClue clue)
        {
            switch (clue)
            {
                case LotClue.Not not:
                    return level.VehicleOf(not.On).Colour != not.Colour;
                case LotClue.Ahead ahead:
                    // The loader guarantees a blocker at the start for every Ahead tag.
                    level.FirstOnPathAtStart(ahead.On).TryGet(out PieceId reference);
                    bool isSame = level.VehicleOf(ahead.On).Colour == level.VehicleOf(reference).Colour;
                    return isSame == (ahead.Relation == AheadRelation.Same);
                default:
                    throw new ArgumentOutOfRangeException(nameof(clue), "Only Not and Ahead tags can lose their rule.");
            }
        }

        /// <summary>The count over the hidden neighbours; open neighbours (never covered, or seen) of the colour are counted out.</summary>
        private static Rule TouchRuleFor(LotLevel level, LotClue.Touch touch, int[] positionOf)
        {
            int knownMatches = 0;
            var hiddenNeighbours = new List<int>();
            foreach (PieceId neighbour in Neighbours(level, touch.On))
            {
                if (positionOf[neighbour.Value] >= 0)
                {
                    hiddenNeighbours.Add(positionOf[neighbour.Value]);
                }
                else if (level.VehicleOf(neighbour).Colour == touch.Colour)
                {
                    knownMatches++;
                }
            }

            return new TouchRule(hiddenNeighbours.ToArray(), touch.Colour.Value, touch.Count - knownMatches);
        }

        /// <summary>The vehicles sharing an edge with the footprint at the start, ascending PieceId.</summary>
        public static IReadOnlyList<PieceId> Neighbours(LotLevel level, PieceId piece)
        {
            ulong footprint = level.FootprintOf(piece);
            var seen = new bool[level.VehicleCount];
            for (int cell = 0; cell <= LotCell.MaxIndex; cell++)
            {
                if ((footprint & (1UL << cell)) == 0)
                {
                    continue;
                }

                foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                {
                    bool isOther = level.Size.Step(new LotCell((byte)cell), facing).TryGet(out LotCell next)
                        && level.PieceAt(next).TryGet(out PieceId other) && other != piece;
                    if (isOther)
                    {
                        level.PieceAt(next).TryGet(out PieceId neighbour);
                        seen[neighbour.Value] = true;
                    }
                }
            }

            var neighbours = new List<PieceId>();
            for (int index = 0; index < seen.Length; index++)
            {
                if (seen[index])
                {
                    neighbours.Add(new PieceId((byte)index));
                }
            }

            return neighbours;
        }

        /// <summary>Counts search nodes for the grader's budget.</summary>
        private sealed class NodeCounter
        {
            private readonly int _budget;

            public NodeCounter(int budget) => _budget = budget;

            public int Nodes { get; private set; }

            public bool IsOver => Nodes > _budget;

            public void Count() => Nodes++;
        }

        /// <summary>One step of the deduction: the cover it fixes, that cover's colour and the step's rung.</summary>
        private readonly struct Fix
        {
            public Fix(int position, ColourId colour, DeductionRung rung)
            {
                Position = position;
                Colour = colour;
                Rung = rung;
            }

            public int Position { get; }

            public ColourId Colour { get; }

            public DeductionRung Rung { get; }
        }

        /// <summary>A tag about one cover alone, and the colours it leaves that cover.</summary>
        private readonly struct OwnTag
        {
            public OwnTag(int position, ushort allows)
            {
                Position = position;
                Allows = allows;
            }

            public int Position { get; }

            public ushort Allows { get; }
        }

        /// <summary>One constraint over some hidden positions. Values are indexed by position (colour 1-15).</summary>
        private abstract class Rule
        {
            protected Rule(int[] positions) => Positions = positions;

            public int[] Positions { get; }

            /// <summary>
            /// The colours the rule leaves each of its covers when it tells every one the same, whatever the others
            /// are; none when what it says of one cover depends on the others.
            /// </summary>
            public virtual Option<ushort> EachCoverAllows => Option.None<ushort>();

            /// <summary>Whether the rule holds when every one of its positions has a value.</summary>
            public abstract bool Holds(byte[] values);

            /// <summary>Checks the rule once all its positions are assigned; a partial assignment passes.</summary>
            public virtual bool IsConsistent(PartialAssignment assignment)
            {
                var values = new byte[assignment.Count];
                foreach (int position in Positions)
                {
                    if (!assignment.At(position).TryGet(out ColourId colour))
                    {
                        return true;
                    }

                    values[position] = colour.Value;
                }

                return Holds(values);
            }

            public int UnknownCount(ushort[] domains)
            {
                int unknown = 0;
                foreach (int position in Positions)
                {
                    unknown += IsSingle(domains[position]) ? 0 : 1;
                }

                return unknown;
            }

            public bool SharesUnknownWith(Rule other, ushort[] domains)
            {
                foreach (int position in Positions)
                {
                    if (!IsSingle(domains[position]) && System.Array.IndexOf(other.Positions, position) >= 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private sealed class NotRule : Rule
        {
            private readonly byte _colour;

            public NotRule(int position, ColourId colour)
                : base(new[] { position }) => _colour = colour.Value;

            public override bool Holds(byte[] values) => values[Positions[0]] != _colour;
        }

        /// <summary>Same/differs against the reference: another cover's position, or a known open colour.</summary>
        private sealed class AheadRule : Rule
        {
            private readonly int _referencePosition;
            private readonly byte _knownReference;
            private readonly bool _same;

            public AheadRule(int position, int referencePosition, byte knownReference, bool same)
                : base(referencePosition >= 0 ? new[] { position, referencePosition } : new[] { position })
            {
                _referencePosition = referencePosition;
                _knownReference = knownReference;
                _same = same;
            }

            public override bool Holds(byte[] values)
            {
                byte reference = _referencePosition >= 0 ? values[_referencePosition] : _knownReference;
                return (values[Positions[0]] == reference) == _same;
            }
        }

        /// <summary>Exactly Needed of the covered neighbours have Colour (open neighbours already counted out).</summary>
        private sealed class TouchRule : Rule
        {
            private readonly byte _colour;
            private readonly int _needed;

            public TouchRule(int[] positions, byte colour, int needed)
                : base(positions)
            {
                _colour = colour;
                _needed = needed;
            }

            /// <summary>None of the covered neighbours has the colour, or every one has it.</summary>
            public override Option<ushort> EachCoverAllows =>
                _needed == 0 ? Option.Some((ushort)(AllColours & ~Bit(_colour)))
                : _needed == Positions.Length ? Option.Some(Bit(_colour))
                : Option.None<ushort>();

            public override bool Holds(byte[] values)
            {
                int matches = 0;
                foreach (int position in Positions)
                {
                    matches += values[position] == _colour ? 1 : 0;
                }

                return matches == _needed;
            }
        }

        /// <summary>The covered vehicles hold exactly the ledger's count of each colour.</summary>
        private sealed class LedgerRule : Rule
        {
            private readonly int[] _ledger;

            public LedgerRule(int[] positions, int[] ledger)
                : base(positions) => _ledger = ledger;

            public override bool Holds(byte[] values)
            {
                var counts = new int[ColourId.MaxValue + 1];
                foreach (int position in Positions)
                {
                    counts[values[position]]++;
                }

                for (int colour = 0; colour < counts.Length; colour++)
                {
                    if (counts[colour] != _ledger[colour])
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>Partial check: no colour is used more often than the ledger allows.</summary>
            public override bool IsConsistent(PartialAssignment assignment)
            {
                var counts = new int[ColourId.MaxValue + 1];
                foreach (int position in Positions)
                {
                    if (assignment.At(position).TryGet(out ColourId colour))
                    {
                        counts[colour.Value]++;
                        if (counts[colour.Value] > _ledger[colour.Value])
                        {
                            return false;
                        }
                    }
                }

                return base.IsConsistent(assignment);
            }
        }
    }
}

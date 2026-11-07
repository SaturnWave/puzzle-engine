using System.Collections.Generic;
using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// The clue model of a covered lot at the level start (CoveredLot.md, "Clue checker specifics").
    /// Hidden cells are the covered vehicles (HiddenCell = PieceId). Only public information enters:
    /// open vehicles' colours, the ledger (how many covered vehicles have each colour), the tags and the
    /// start geometry. Constraints: Not(c); Ahead(Same|Differs) against the first vehicle on the path
    /// at the start; Touch(c, n) over the vehicles sharing an edge with the footprint; the exact ledger.
    /// <para>
    /// Jam's mapping onto the shared ladder (the brief lets each game map its techniques):
    /// D1 = one constraint with one unknown cover, or a colour the ledger has used up;
    /// D2 = one constraint across several unknown covers (a Touch count, the ledger count);
    /// D3 = two constraints combined (for example an Ahead link together with the ledger).
    /// Anything that needs more is NeedsGuess.
    /// </para>
    /// </summary>
    public sealed class LotClues : IClueModel
    {
        private readonly int[] _ledger = new int[ColourId.MaxValue + 1];
        private readonly List<ColourId> _ledgerColours = new List<ColourId>();
        private readonly List<Rule> _rules = new List<Rule>();

        public LotClues(LotLevel level)
        {
            var hidden = new List<HiddenCell>();
            var positionOf = new int[level.VehicleCount];
            for (int piece = 0; piece < level.VehicleCount; piece++)
            {
                positionOf[piece] = -1;
                Vehicle vehicle = level.Vehicles[piece];
                if (vehicle.StartsCovered)
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
                _rules.Add(RuleFor(level, clue, positionOf));
            }

            var all = new int[hidden.Count];
            for (int position = 0; position < all.Length; position++)
            {
                all[position] = position;
            }

            _rules.Add(new LedgerRule(all, _ledger));
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
        /// Grades the deduction by propagation only, never by a what-if split, the way a careful player
        /// would: one deduction at a time, always the easiest available (D1 before D2 before D3). A
        /// deduction keeps only the colours that some assignment satisfying the constraint(s) supports.
        /// Steps counts the deductions; Rung is the hardest one needed.
        /// </summary>
        public DeductionGrade Grade(NodeBudget budget)
        {
            var domains = new ushort[HiddenCells.Count];
            ushort initial = 0;
            foreach (ColourId colour in _ledgerColours)
            {
                initial |= (ushort)(1 << colour.Value);
            }

            for (int position = 0; position < domains.Length; position++)
            {
                domains[position] = initial;
            }

            var counter = new NodeCounter(budget.Nodes);
            DeductionRung hardest = DeductionRung.D1;
            int steps = 0;
            while (true)
            {
                if (IsContradicted(domains, counter))
                {
                    return new DeductionGrade.NeedsGuess();
                }

                if (AllResolved(domains))
                {
                    return new DeductionGrade.Graded(hardest, new StepCount(steps));
                }

                Option<DeductionRung> step = EasiestDeduction(domains, counter);
                if (counter.IsOver)
                {
                    return new DeductionGrade.BudgetExceeded(new NodeCount(counter.Nodes));
                }

                if (!step.TryGet(out DeductionRung rung))
                {
                    return new DeductionGrade.NeedsGuess();
                }

                hardest = DeductionRungs.Max(hardest, rung);
                steps++;
            }
        }

        /// <summary>Applies the first deduction found, easiest rung first, and returns its rung.</summary>
        private Option<DeductionRung> EasiestDeduction(ushort[] domains, NodeCounter counter)
        {
            if (LedgerUsedUp(domains))
            {
                return Option.Some(DeductionRung.D1);
            }

            foreach (Rule rule in _rules)
            {
                if (rule.UnknownCount(domains) == 1 && Narrow(new[] { rule }, domains, counter))
                {
                    return Option.Some(DeductionRung.D1);
                }
            }

            foreach (Rule rule in _rules)
            {
                if (rule.UnknownCount(domains) >= 2 && Narrow(new[] { rule }, domains, counter))
                {
                    return Option.Some(DeductionRung.D2);
                }
            }

            for (int first = 0; first < _rules.Count; first++)
            {
                for (int second = first + 1; second < _rules.Count; second++)
                {
                    bool isLinked = _rules[first].SharesUnknownWith(_rules[second], domains);
                    if (isLinked && Narrow(new[] { _rules[first], _rules[second] }, domains, counter))
                    {
                        return Option.Some(DeductionRung.D3);
                    }
                }
            }

            return Option.None<DeductionRung>();
        }

        /// <summary>One colour whose ledger count is already reached by resolved covers comes off every other cover (D1).</summary>
        private bool LedgerUsedUp(ushort[] domains)
        {
            foreach (ColourId colour in _ledgerColours)
            {
                ushort bit = (ushort)(1 << colour.Value);
                int resolved = 0;
                bool appearsElsewhere = false;
                foreach (ushort domain in domains)
                {
                    resolved += domain == bit ? 1 : 0;
                    appearsElsewhere |= domain != bit && (domain & bit) != 0;
                }

                if (resolved < _ledger[colour.Value] || !appearsElsewhere)
                {
                    continue;
                }

                for (int position = 0; position < domains.Length; position++)
                {
                    if (domains[position] != bit)
                    {
                        domains[position] = (ushort)(domains[position] & ~bit);
                    }
                }

                return true;
            }

            return false;
        }

        /// <summary>Whether the resolved covers already break some constraint, or a cover has no colour left.</summary>
        private bool IsContradicted(ushort[] domains, NodeCounter counter)
        {
            foreach (ushort domain in domains)
            {
                if (domain == 0)
                {
                    return true;
                }
            }

            foreach (Rule rule in _rules)
            {
                if (rule.UnknownCount(domains) == 0 && !HasSupport(new[] { rule }, domains, counter))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Keeps, on each unknown position of the rules, only colours that some joint assignment
        /// satisfying all of them supports. Returns whether any domain shrank.
        /// </summary>
        private static bool Narrow(Rule[] rules, ushort[] domains, NodeCounter counter)
        {
            List<int> unknown = UnknownPositions(rules, domains);
            var supported = new ushort[unknown.Count];
            Enumerate(rules, domains, unknown, 0, new byte[domains.Length], supported, counter);
            bool progressed = false;
            for (int index = 0; index < unknown.Count; index++)
            {
                int position = unknown[index];
                ushort narrowed = (ushort)(domains[position] & supported[index]);
                if (narrowed != domains[position])
                {
                    domains[position] = narrowed;
                    progressed = true;
                }
            }

            return progressed;
        }

        private static bool HasSupport(Rule[] rules, ushort[] domains, NodeCounter counter)
        {
            List<int> unknown = UnknownPositions(rules, domains);
            var supported = new ushort[unknown.Count];
            return Enumerate(rules, domains, unknown, 0, new byte[domains.Length], supported, counter);
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
                    supported[marked] |= (ushort)(1 << values[unknown[marked]]);
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

        private static List<int> UnknownPositions(Rule[] rules, ushort[] domains)
        {
            var unknown = new List<int>();
            foreach (Rule rule in rules)
            {
                foreach (int position in rule.Positions)
                {
                    if (!IsSingle(domains[position]) && !unknown.Contains(position))
                    {
                        unknown.Add(position);
                    }
                }
            }

            return unknown;
        }

        private static bool AllResolved(ushort[] domains)
        {
            foreach (ushort domain in domains)
            {
                if (!IsSingle(domain))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSingle(ushort domain) => domain != 0 && (domain & (domain - 1)) == 0;

        private static byte SingleColour(ushort domain)
        {
            byte colour = 0;
            while ((domain >> colour) != 1)
            {
                colour++;
            }

            return colour;
        }

        private static Rule RuleFor(LotLevel level, LotClue clue, int[] positionOf) => clue switch
        {
            LotClue.Not not => new NotRule(positionOf[not.On.Value], not.Colour),
            LotClue.Ahead ahead => AheadRuleFor(level, ahead, positionOf),
            LotClue.Touch touch => TouchRuleFor(level, touch, positionOf),
            _ => throw new System.ArgumentOutOfRangeException(nameof(clue), "Unknown clue family."),
        };

        private static Rule AheadRuleFor(LotLevel level, LotClue.Ahead ahead, int[] positionOf)
        {
            // The loader guarantees a blocker at the start for every Ahead tag.
            level.FirstOnPathAtStart(ahead.On).TryGet(out PieceId reference);
            Vehicle referenceVehicle = level.VehicleOf(reference);
            bool same = ahead.Relation == AheadRelation.Same;
            return referenceVehicle.StartsCovered
                ? new AheadRule(positionOf[ahead.On.Value], positionOf[reference.Value], 0, same)
                : new AheadRule(positionOf[ahead.On.Value], -1, referenceVehicle.Colour.Value, same);
        }

        private static Rule TouchRuleFor(LotLevel level, LotClue.Touch touch, int[] positionOf)
        {
            int knownMatches = 0;
            var coveredNeighbours = new List<int>();
            foreach (PieceId neighbour in Neighbours(level, touch.On))
            {
                Vehicle vehicle = level.VehicleOf(neighbour);
                if (vehicle.StartsCovered)
                {
                    coveredNeighbours.Add(positionOf[neighbour.Value]);
                }
                else if (vehicle.Colour == touch.Colour)
                {
                    knownMatches++;
                }
            }

            return new TouchRule(coveredNeighbours.ToArray(), touch.Colour.Value, touch.Count - knownMatches);
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

        /// <summary>One constraint over some hidden positions. Values are indexed by position (colour 1-15).</summary>
        private abstract class Rule
        {
            protected Rule(int[] positions) => Positions = positions;

            public int[] Positions { get; }

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

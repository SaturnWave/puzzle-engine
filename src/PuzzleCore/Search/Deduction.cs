using System;
using System.Collections.Generic;

namespace PuzzleCore.Search
{
    /// <summary>
    /// A colour for each hidden cell, in ascending cell order. Used for the answer of a clue level
    /// and for uniqueness counter-examples.
    /// </summary>
    public sealed record HiddenAssignment(ReadOnlyArray<HiddenCell> Cells, ReadOnlyArray<ColourId> Colours)
    {
        public Option<ColourId> ColourOf(HiddenCell cell)
        {
            for (int index = 0; index < Cells.Count; index++)
            {
                if (Cells[index] == cell)
                {
                    return Option.Some(Colours[index]);
                }
            }

            return Option.None<ColourId>();
        }
    }

    /// <summary>
    /// Colours chosen so far for the hidden cells, by position in the model's ascending cell list;
    /// a position may still be open. Mutable scratch space for the checkers; never shared.
    /// </summary>
    public sealed class PartialAssignment
    {
        private readonly byte[] _colours;

        public PartialAssignment(int cellCount) => _colours = new byte[cellCount];

        public int Count => _colours.Length;

        public Option<ColourId> At(int position)
        {
            byte value = _colours[position];
            return value == 0 ? Option.None<ColourId>() : Option.Some(new ColourId(value));
        }

        public void Set(int position, ColourId colour) => _colours[position] = colour.Value;

        public void Clear(int position) => _colours[position] = 0;
    }

    /// <summary>
    /// The clue model of one clue level (Sort: tags + shelf ledger; Jam: tags + cover ledger), read by
    /// the uniqueness checker. Only public information enters it: never the hidden answer itself.
    /// </summary>
    public interface IClueModel
    {
        /// <summary>The hidden cells, ascending.</summary>
        ReadOnlyArray<HiddenCell> HiddenCells { get; }

        /// <summary>The colours a cell may take before any clue is applied (ascending ColourId).</summary>
        void AppendCandidates(int position, List<ColourId> into);

        /// <summary>False only when the assigned positions already break a clue or the ledger. Prunes early.</summary>
        bool IsConsistent(PartialAssignment assignment);
    }

    /// <summary>Whether the clues fix exactly one assignment. Closed set.</summary>
    public abstract record Uniqueness
    {
        private Uniqueness()
        {
        }

        public sealed record Unique(HiddenAssignment Answer) : Uniqueness;

        public sealed record Multiple(HiddenAssignment First, HiddenAssignment Second) : Uniqueness;

        /// <summary>No assignment satisfies every clue.</summary>
        public sealed record Contradiction : Uniqueness;

        public sealed record BudgetExceeded(NodeCount NodesUsed) : Uniqueness;
    }

    /// <summary>
    /// A rung of the one deduction ladder. The rungs are defined only in shared-design.md §2, "The deduction
    /// ladder"; each game's grader documents how its steps map onto them (Jam: LotClues).
    /// </summary>
    public enum DeductionRung
    {
        D1,
        D2,
        D3,
    }

    /// <summary>How hard the deduction is, or why it cannot be done by reasoning alone. Closed set.</summary>
    public abstract record DeductionGrade
    {
        private DeductionGrade()
        {
        }

        /// <summary>Every hidden cell is forced by reasoning; Rung is the hardest step needed, Steps the passes that made progress.</summary>
        public sealed record Graded(DeductionRung Rung, StepCount Steps) : DeductionGrade;

        /// <summary>Reasoning stalls: a what-if case split would be needed. Rejected in v1.</summary>
        public sealed record NeedsGuess : DeductionGrade;

        public sealed record BudgetExceeded(NodeCount NodesUsed) : DeductionGrade;
    }

    /// <summary>Brute force over the few hidden cells, stopping at the second solution.</summary>
    public static class UniquenessCheck
    {
        public static Uniqueness Run(IClueModel model, NodeBudget budget)
        {
            int count = model.HiddenCells.Count;
            var domains = new List<ColourId>[count];
            for (int position = 0; position < count; position++)
            {
                domains[position] = new List<ColourId>();
                model.AppendCandidates(position, domains[position]);
            }

            var search = new Search(model, domains, budget.Nodes);
            search.Explore(0);
            if (search.Nodes > budget.Nodes)
            {
                return new Uniqueness.BudgetExceeded(new NodeCount(search.Nodes));
            }

            return search.Found.Count switch
            {
                0 => new Uniqueness.Contradiction(),
                1 => new Uniqueness.Unique(search.Found[0]),
                _ => new Uniqueness.Multiple(search.Found[0], search.Found[1]),
            };
        }

        private sealed class Search
        {
            private readonly IClueModel _model;
            private readonly List<ColourId>[] _domains;
            private readonly int _budget;
            private readonly PartialAssignment _assignment;

            public Search(IClueModel model, List<ColourId>[] domains, int budget)
            {
                _model = model;
                _domains = domains;
                _budget = budget;
                _assignment = new PartialAssignment(domains.Length);
            }

            public List<HiddenAssignment> Found { get; } = new List<HiddenAssignment>();

            public int Nodes { get; private set; }

            public void Explore(int position)
            {
                if (Found.Count >= 2 || Nodes > _budget)
                {
                    return;
                }

                Nodes++;
                if (!_model.IsConsistent(_assignment))
                {
                    return;
                }

                if (position == _domains.Length)
                {
                    Found.Add(Snapshot());
                    return;
                }

                foreach (ColourId colour in _domains[position])
                {
                    _assignment.Set(position, colour);
                    Explore(position + 1);
                    _assignment.Clear(position);
                }
            }

            private HiddenAssignment Snapshot()
            {
                var colours = new ColourId[_domains.Length];
                for (int position = 0; position < colours.Length; position++)
                {
                    colours[position] = _assignment.At(position).Or(default);
                }

                return new HiddenAssignment(_model.HiddenCells, ReadOnlyArray.Of(colours));
            }
        }
    }

    /// <summary>Combines rungs: a level's rung is its hardest step.</summary>
    public static class DeductionRungs
    {
        public static DeductionRung Max(DeductionRung left, DeductionRung right) => (int)left >= (int)right ? left : right;
    }
}

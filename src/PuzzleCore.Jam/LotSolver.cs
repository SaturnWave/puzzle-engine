using System.Collections.Generic;
using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>PeakPar(peak, winning path) or why no peak was found within the bays tried.</summary>
    public abstract record PeakParResult
    {
        private PeakParResult()
        {
        }

        /// <summary>The fewest bays the level can be won with, and a winning order that never uses more.</summary>
        public sealed record Found(CapacityCount Peak, ReadOnlyArray<Exit> Path) : PeakParResult;

        /// <summary>No order wins even with the largest capacity tried.</summary>
        public sealed record Unsolvable : PeakParResult;

        public sealed record BudgetExceeded(NodeCount NodesUsed) : PeakParResult;
    }

    /// <summary>
    /// Feasibility search for Covered Lot (full information). The rules depend only on which
    /// vehicles have left (the bays hold, per colour, the exited count mod 3), so the search runs
    /// over exited masks. With k bays a park that clears a set needs room for it (occupied + 1 ≤ k);
    /// a park that does not clear must leave a bay free (occupied + 1 &lt; k), or the row is full.
    /// Moves are tried in ascending PieceId. Dead masks go in a fixed-size table where a
    /// collision replaces the old entry (bounded memory).
    /// </summary>
    public static class LotSolver
    {
        public const int SetSize = 3;
        private const int DeadTableSize = 1 << 16;

        public static SolveResult<Exit> Solve(LotLevel level, CapacityCount bays, NodeBudget budget)
        {
            var search = new Search(level, bays.Value, budget);
            return search.Run();
        }

        /// <summary>
        /// The smallest bay count that wins, searching 3 up to <paramref name="maxBays"/>. Since every
        /// order with peak k wins with k bays, this is also the lowest peak over all winning orders.
        /// </summary>
        public static PeakParResult PeakPar(LotLevel level, CapacityCount maxBays, NodeBudget budget)
        {
            // Each bay count gets the whole budget; BudgetExceeded reports the nodes of the search that ran out.
            for (int bays = SetSize; bays <= maxBays.Value; bays++)
            {
                switch (new Search(level, bays, budget).Run())
                {
                    case SolveResult<Exit>.Solved solved:
                        return new PeakParResult.Found(new CapacityCount(bays), solved.Path);
                    case SolveResult<Exit>.BudgetExceeded exceeded:
                        return new PeakParResult.BudgetExceeded(exceeded.NodesUsed);
                    case SolveResult<Exit>.Unsolvable:
                        break;
                }
            }

            return new PeakParResult.Unsolvable();
        }

        /// <summary>The highest bay use along an order (counted when each vehicle parks, before its set leaves).</summary>
        public static int PeakOf(LotLevel level, IReadOnlyList<Exit> path)
        {
            var counts = new int[ColourId.MaxValue + 1];
            int occupied = 0;
            int peak = 0;
            foreach (Exit exit in path)
            {
                int colour = level.VehicleOf(exit.Piece).Colour.Value;
                occupied++;
                peak = System.Math.Max(peak, occupied);
                counts[colour]++;
                if (counts[colour] % SetSize == 0)
                {
                    occupied -= SetSize;
                }
            }

            return peak;
        }

        private sealed class Search
        {
            private readonly LotLevel _level;
            private readonly int _bays;
            private readonly int _budget;
            private readonly int[] _colourCounts = new int[ColourId.MaxValue + 1];
            private readonly uint[] _dead = new uint[DeadTableSize];
            private readonly List<Exit> _path = new List<Exit>();
            private int _nodes;

            public Search(LotLevel level, int bays, NodeBudget budget)
            {
                _level = level;
                _bays = bays;
                _budget = budget.Nodes;
            }

            public SolveResult<Exit> Run()
            {
                Outcome outcome = Explore(0U, 0);
                return outcome switch
                {
                    Outcome.Win => new SolveResult<Exit>.Solved(ReadOnlyArray.CopyOf(_path)),
                    Outcome.Budget => new SolveResult<Exit>.BudgetExceeded(new NodeCount(_nodes)),
                    _ => new SolveResult<Exit>.Unsolvable(),
                };
            }

            private Outcome Explore(uint exited, int occupied)
            {
                if (exited == _level.AllExitedMask)
                {
                    return Outcome.Win;
                }

                if (IsKnownDead(exited))
                {
                    return Outcome.Dead;
                }

                _nodes++;
                if (_nodes > _budget)
                {
                    return Outcome.Budget;
                }

                for (int index = 0; index < _level.VehicleCount; index++)
                {
                    if ((exited & (1U << index)) != 0)
                    {
                        continue;
                    }

                    var piece = new PieceId((byte)index);
                    if (_level.FirstBlocker(piece, exited).HasValue)
                    {
                        continue;
                    }

                    int colour = _level.VehicleOf(piece).Colour.Value;
                    bool clears = (_colourCounts[colour] + 1) % SetSize == 0;
                    bool fits = clears ? occupied + 1 <= _bays : occupied + 1 < _bays;
                    if (!fits)
                    {
                        continue;
                    }

                    _colourCounts[colour]++;
                    _path.Add(new Exit(piece));
                    Outcome result = Explore(exited | (1U << index), clears ? occupied + 1 - SetSize : occupied + 1);
                    if (result != Outcome.Dead)
                    {
                        return result;
                    }

                    _path.RemoveAt(_path.Count - 1);
                    _colourCounts[colour]--;
                }

                MarkDead(exited);
                return Outcome.Dead;
            }

            // The table stores mask + 1 so that an empty slot (0) never matches the start mask.
            private bool IsKnownDead(uint exited) => _dead[Slot(exited)] == exited + 1;

            private void MarkDead(uint exited) => _dead[Slot(exited)] = exited + 1;

            private static int Slot(uint exited)
            {
                // Fibonacci hashing of the mask into the table; only spreads entries, never decides results.
                unchecked
                {
                    return (int)((exited * 2654435769U) >> 16);
                }
            }
        }

        private enum Outcome
        {
            Win,
            Dead,
            Budget,
        }
    }
}

using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Search;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>A position solver that records every state it is asked to solve, then solves it for real.</summary>
    internal sealed class RecordingSolver : IPositionSolver<LotState, Exit>
    {
        private readonly LotPositionSolver _inner;

        public RecordingSolver(LotLevel level) => _inner = new LotPositionSolver(level);

        public List<LotState> Asked { get; } = new List<LotState>();

        public SolveResult<Exit> SolveFrom(in LotState state, NodeBudget budget)
        {
            Asked.Add(state);
            return _inner.SolveFrom(state, budget);
        }
    }
}

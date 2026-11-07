namespace PuzzleCore.Search
{
    /// <summary>A solver's answer. Closed set. Budgets count nodes, never time.</summary>
    public abstract record SolveResult<TMove>
        where TMove : struct
    {
        private SolveResult()
        {
        }

        /// <summary>A winning path; its length is Par.</summary>
        public sealed record Solved(ReadOnlyArray<TMove> Path) : SolveResult<TMove>;

        public sealed record Unsolvable : SolveResult<TMove>;

        public sealed record BudgetExceeded(NodeCount NodesUsed) : SolveResult<TMove>;
    }
}

namespace PuzzleCore.Search
{
    /// <summary>
    /// A full-information solver for one level: a winning continuation from a position in play,
    /// within a node budget. Each game supplies one; the shared fail analysis (near miss and rewind
    /// reach) runs on it, so both games share one definition of what can still win.
    /// </summary>
    public interface IPositionSolver<TState, TMove>
        where TState : struct
        where TMove : struct
    {
        /// <summary>Solved(the remaining moves), Unsolvable, or BudgetExceeded when the budget runs out first.</summary>
        SolveResult<TMove> SolveFrom(in TState state, NodeBudget budget);
    }
}

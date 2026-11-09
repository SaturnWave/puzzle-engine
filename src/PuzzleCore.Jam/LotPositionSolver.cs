using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>Covered Lot's position solver for the shared fail analysis: <see cref="LotSolver.SolveFrom"/> on one level.</summary>
    public sealed class LotPositionSolver : IPositionSolver<LotState, Exit>
    {
        private readonly LotLevel _level;

        public LotPositionSolver(LotLevel level) => _level = level;

        public SolveResult<Exit> SolveFrom(in LotState state, NodeBudget budget) => LotSolver.SolveFrom(_level, state, budget);
    }
}

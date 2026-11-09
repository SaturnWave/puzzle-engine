using PuzzleCore.Rules;
using PuzzleCore.Search;

namespace PuzzleCore.Play
{
    /// <summary>
    /// What can still win after a fail, decided by full-information solves under a node budget: the
    /// near miss behind the +1 spare and the rewind reach behind the rewind. Both games share these
    /// definitions and supply their own position solver. A budget overrun gives Unknown, which the
    /// fail offer never sells.
    /// </summary>
    public static class FailAnalysis
    {
        /// <summary>
        /// The near miss of a failed attempt. SpareUnavailable when the attempt's one spare is used or
        /// no spare fits; otherwise one solve from the failed state plus one spare decides Yes (with the
        /// moves left to the win), No, or Unknown. Call it on Failed only: any other outcome throws
        /// InvalidOperationException.
        /// </summary>
        public static NearMiss NearMissOf<TState, TMove, TRejection>(
            Attempt<TState, TMove, TRejection> attempt, IPositionSolver<TState, TMove> solver, NodeBudget budget)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            RequireFailed(attempt);
            if (!attempt.PeekSpare().TryGet(out TState spared))
            {
                return new NearMiss.SpareUnavailable();
            }

            return solver.SolveFrom(spared, budget) switch
            {
                SolveResult<TMove>.Solved solved => new NearMiss.Yes(new MoveCount(solved.Path.Count)),
                SolveResult<TMove>.Unsolvable => new NearMiss.No(),
                SolveResult<TMove>.BudgetExceeded exceeded => new NearMiss.Unknown(exceeded.NodesUsed),
                _ => throw new System.InvalidOperationException("Unknown solve result."),
            };
        }

        /// <summary>
        /// The rewind reach of a failed attempt, from one solve of the longest rewind it allows
        /// (<see cref="Attempt{TState, TMove, TRejection}.RewindLimit"/>). Winning rewinds are upward-closed
        /// within the limit: a longer rewind can replay the moves a shorter one keeps, and the spare a
        /// rewind adds back only adds capacity, which never hurts. So if any allowed rewind wins, the
        /// longest one does. NotWinnable at limit 0, without a solve; Unknown when the budget runs out.
        /// Call it on Failed only: any other outcome throws InvalidOperationException.
        /// </summary>
        public static RewindReach RewindReachOf<TState, TMove, TRejection>(
            Attempt<TState, TMove, TRejection> attempt, IPositionSolver<TState, TMove> solver, NodeBudget budget)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            RequireFailed(attempt);
            if (!attempt.PeekRewind(attempt.RewindLimit).TryGet(out TState rewound))
            {
                return RewindReach.NotWinnable;
            }

            return solver.SolveFrom(rewound, budget) switch
            {
                SolveResult<TMove>.Solved => RewindReach.Winnable,
                SolveResult<TMove>.Unsolvable => RewindReach.NotWinnable,
                SolveResult<TMove>.BudgetExceeded => RewindReach.Unknown,
                _ => throw new System.InvalidOperationException("Unknown solve result."),
            };
        }

        /// <summary>Both analyses are defined on a fail only; asking on any other outcome is a caller's bug.</summary>
        private static void RequireFailed<TState, TMove, TRejection>(Attempt<TState, TMove, TRejection> attempt)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            if (!(attempt.Outcome is Outcome.Failed))
            {
                throw new System.InvalidOperationException("The near miss and the rewind reach are defined on a failed attempt only.");
            }
        }
    }
}

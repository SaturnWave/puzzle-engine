using PuzzleCore.Rules;
using PuzzleCore.Search;

namespace PuzzleCore.Play
{
    /// <summary>
    /// What can still win after a fail, and why it was lost, decided by full-information solves under a
    /// node budget: the near miss behind the +1 spare, the rewind reach behind the rewind, and the fail
    /// cause for analytics. Both games share these definitions and supply their own position solver. A
    /// budget overrun gives Unknown, which the fail offer never sells.
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

        /// <summary>
        /// Why a failed attempt was lost (shared-design.md §2, "Fail cause for analytics"). The losing move
        /// is the one that left the last winnable entry of the current line. Winnable entries come first on
        /// the line: from an earlier entry the line's own moves reach any later winnable one, so a lost entry
        /// is never followed by a winnable one, and a binary search finds the last winnable entry with one
        /// solve per entry it checks. It starts at the spare's entry once the spare is taken, otherwise at the
        /// start, and takes that entry as winnable without a solve: the spare is sold only on a near miss of
        /// Yes, and a shipped level's start is certified winnable. After a rewind behind the spare, the
        /// spare's entry is the rewind's target, which gets the spare back; from it the line's own moves led
        /// to the entry where the spare was sold, so it is winnable too. The losing move is judged with the
        /// SeenSet from just before it, because it reveals its own cell: Deduction when it touched a cell
        /// hidden then (the game's check), Capacity otherwise.
        /// The first solve that runs out of <paramref name="budgetPerEntry"/> gives Unknown. Call it on
        /// Failed only: any other outcome throws InvalidOperationException.
        /// </summary>
        public static FailCause CauseOf<TState, TMove, TRejection>(
            Attempt<TState, TMove, TRejection> attempt,
            IPositionSolver<TState, TMove> solver,
            IHiddenMoveCheck<TState, TMove> hiddenCheck,
            NodeBudget budgetPerEntry)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            RequireFailed(attempt);
            return LastWinnableEntry(attempt, solver, budgetPerEntry) switch
            {
                EntryScan.Found found => LabelTheMoveAfter(attempt, hiddenCheck, found.Entry),
                EntryScan.Overrun overrun => new FailCause.Unknown(overrun.NodesUsed),
                _ => throw new System.InvalidOperationException("Unknown entry scan result."),
            };
        }

        /// <summary>The analyses are defined on a fail only; asking on any other outcome is a caller's bug.</summary>
        private static void RequireFailed<TState, TMove, TRejection>(Attempt<TState, TMove, TRejection> attempt)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            if (!(attempt.Outcome is Outcome.Failed))
            {
                throw new System.InvalidOperationException("The fail analysis is defined on a failed attempt only.");
            }
        }

        /// <summary>
        /// Binary search between the first scannable entry, taken as winnable, and the failed entry: each
        /// step solves the middle entry and keeps the half where the last winnable entry must lie.
        /// </summary>
        private static EntryScan LastWinnableEntry<TState, TMove, TRejection>(
            Attempt<TState, TMove, TRejection> attempt, IPositionSolver<TState, TMove> solver, NodeBudget budget)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            int winnable = attempt.FirstScannableEntry.Value;
            int lost = attempt.Moves.Value;
            while (lost - winnable > 1)
            {
                int middle = winnable + ((lost - winnable) / 2);
                switch (solver.SolveFrom(attempt.EntryAfter(new MoveCount(middle)), budget))
                {
                    case SolveResult<TMove>.Solved:
                        winnable = middle;
                        break;
                    case SolveResult<TMove>.Unsolvable:
                        lost = middle;
                        break;
                    case SolveResult<TMove>.BudgetExceeded exceeded:
                        return new EntryScan.Overrun(exceeded.NodesUsed);
                    default:
                        throw new System.InvalidOperationException("Unknown solve result.");
                }
            }

            return new EntryScan.Found(new MoveCount(winnable));
        }

        /// <summary>The cause of the move played from the last winnable entry, judged by what was seen before it.</summary>
        private static FailCause LabelTheMoveAfter<TState, TMove, TRejection>(
            Attempt<TState, TMove, TRejection> attempt, IHiddenMoveCheck<TState, TMove> hiddenCheck, MoveCount lastWinnable)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            var losingMoveBack = new MoveCount(attempt.Moves.Value - lastWinnable.Value);
            Attempt<TState, TMove, TRejection>.LineStep losingMove = attempt.StepInto(new MoveCount(lastWinnable.Value + 1));
            if (hiddenCheck.TouchesHiddenCell(attempt.EntryAfter(lastWinnable), losingMove.Move, losingMove.SeenBefore))
            {
                return new FailCause.Deduction(losingMoveBack);
            }

            return new FailCause.Capacity(losingMoveBack);
        }

        /// <summary>What the entry scan found: the last winnable entry, or the first solve that ran out of budget.</summary>
        private abstract record EntryScan
        {
            private EntryScan()
            {
            }

            /// <summary>The last winnable entry, counted in moves from the start.</summary>
            public sealed record Found(MoveCount Entry) : EntryScan;

            public sealed record Overrun(NodeCount NodesUsed) : EntryScan;
        }
    }
}

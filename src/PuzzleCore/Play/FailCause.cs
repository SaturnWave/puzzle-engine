namespace PuzzleCore.Play
{
    /// <summary>
    /// Why a failed attempt was lost, for analytics and debug lines only: it is never shown to players
    /// or sold, because the exact losing move points at the answer (shared-design.md §2). The losing
    /// move is the one that left the last winnable position; LosingMoveBack counts how far back from the
    /// fail it was, and 1 is the last move. Closed set.
    /// </summary>
    public abstract record FailCause
    {
        private FailCause()
        {
        }

        /// <summary>The losing move touched no cell that was hidden when it was played.</summary>
        public sealed record Capacity(MoveCount LosingMoveBack) : FailCause;

        /// <summary>The losing move touched a cell that was hidden when it was played (Jam: it sent out a covered vehicle not yet seen).</summary>
        public sealed record Deduction(MoveCount LosingMoveBack) : FailCause;

        /// <summary>A solve ran out of its budget, so the losing move is not known.</summary>
        public sealed record Unknown(NodeCount NodesUsed) : FailCause;
    }
}

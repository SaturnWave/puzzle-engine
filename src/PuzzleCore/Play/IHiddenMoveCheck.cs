namespace PuzzleCore.Play
{
    /// <summary>
    /// A game's check for the fail cause: whether a move touched a cell that was hidden when it was
    /// played. Each game supplies one, as it supplies its position solver (Jam: LotHiddenMoveCheck).
    /// </summary>
    public interface IHiddenMoveCheck<TState, TMove>
        where TState : struct
        where TMove : struct
    {
        /// <summary>
        /// Whether <paramref name="move"/>, played from <paramref name="before"/>, touched a cell that
        /// started hidden and is not in <paramref name="seenBefore"/>, the attempt's SeenSet just before
        /// the move. That set leaves out what the move itself revealed.
        /// </summary>
        bool TouchesHiddenCell(in TState before, TMove move, SeenSet seenBefore);
    }
}

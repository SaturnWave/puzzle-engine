using PuzzleCore.Play;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// Covered Lot's check for the fail cause (Q2, Q20, Q25): an exit touched a hidden cell when its own
    /// vehicle started covered and was not in the SeenSet just before the exit. Only that vehicle counts,
    /// never the vehicles its exit unblocked.
    /// </summary>
    public sealed class LotHiddenMoveCheck : IHiddenMoveCheck<LotState, Exit>
    {
        private readonly LotLevel _level;

        public LotHiddenMoveCheck(LotLevel level) => _level = level;

        public bool TouchesHiddenCell(in LotState before, Exit move, SeenSet seenBefore) =>
            _level.VehicleOf(move.Piece).StartsCovered && !seenBefore.Contains(new HiddenCell(move.Piece.Value));
    }
}

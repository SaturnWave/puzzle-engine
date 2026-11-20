using PuzzleCore.Jam;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// Covered Lot's rules as the bots model them (TC21): a vehicle can leave once every vehicle standing on its path at the
    /// start has left, and the level is won when every vehicle has left. The colours come from each bot, through
    /// <see cref="LotPosition.After"/>: the model reads the level's geometry, never a colour. On the true colours it agrees
    /// with JamRules move for move.
    /// </summary>
    public sealed class LotModel
    {
        // Per vehicle, the vehicles standing on its path at the start.
        private readonly uint[] _standingOnPath;
        private readonly uint _allExited;

        public LotModel(LotLevel level)
        {
            _standingOnPath = new uint[level.VehicleCount];
            for (int index = 0; index < level.VehicleCount; index++)
            {
                // The loader refuses a pillar on any path (LoadProblem.PillarBlocksPath), so only vehicles can block one.
                foreach (LotCell cell in level.PathOf(new PieceId((byte)index)))
                {
                    if (level.PieceAt(cell).TryGet(out PieceId standing))
                    {
                        _standingOnPath[index] |= 1U << standing.Value;
                    }
                }
            }

            _allExited = level.AllExitedMask;
        }

        public int VehicleCount => _standingOnPath.Length;

        /// <summary>Whether the vehicle is still in the lot with nothing left on its path.</summary>
        public bool CanLeave(LotPosition position, PieceId piece) =>
            (position.Exited & (1U << piece.Value)) == 0 && (_standingOnPath[piece.Value] & ~position.Exited) == 0;

        /// <summary>Every vehicle has left: the level is won, whatever the bays held before the last set cleared.</summary>
        public bool IsWon(LotPosition position) => position.Exited == _allExited;
    }
}

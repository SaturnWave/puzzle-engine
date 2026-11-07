using System.Collections.Generic;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// A loaded, valid lot: geometry, colours, covers and clues, with the cell masks the rules
    /// need precomputed. Only <see cref="LotLoader"/> builds one, so every LotLevel passed the
    /// load checks (acyclic blocking, no pillar on a path, colour totals in threes...).
    /// </summary>
    public sealed class LotLevel
    {
        private const byte NoPiece = byte.MaxValue;

        private readonly ulong[] _footprints;
        private readonly LotCell[][] _paths;
        private readonly byte[] _pieceAtCell;
        private readonly Option<PieceId>[] _firstOnPathAtStart;

        internal LotLevel(
            LotSize size,
            BayCount bays,
            ulong pillars,
            ReadOnlyArray<Vehicle> vehicles,
            ReadOnlyArray<LotClue> clues,
            ulong[] footprints,
            LotCell[][] paths)
        {
            Size = size;
            Bays = bays;
            Pillars = pillars;
            Vehicles = vehicles;
            Clues = clues;
            _footprints = footprints;
            _paths = paths;

            _pieceAtCell = new byte[LotCell.MaxIndex + 1];
            for (int cell = 0; cell < _pieceAtCell.Length; cell++)
            {
                _pieceAtCell[cell] = NoPiece;
            }

            for (int piece = 0; piece < footprints.Length; piece++)
            {
                for (int cell = 0; cell < _pieceAtCell.Length; cell++)
                {
                    if ((footprints[piece] & (1UL << cell)) != 0)
                    {
                        _pieceAtCell[cell] = (byte)piece;
                    }
                }
            }

            _firstOnPathAtStart = new Option<PieceId>[vehicles.Count];
            for (int piece = 0; piece < vehicles.Count; piece++)
            {
                _firstOnPathAtStart[piece] = FirstPieceOnPath(new PieceId((byte)piece), exitedMask: 0U);
            }

            uint allExited = 0;
            uint covered = 0;
            for (int piece = 0; piece < vehicles.Count; piece++)
            {
                allExited |= 1U << piece;
                if (vehicles[piece].StartsCovered)
                {
                    covered |= 1U << piece;
                }
            }

            AllExitedMask = allExited;
            CoveredAtStartMask = covered;
        }

        public LotSize Size { get; }

        /// <summary>The bays the level starts with (4-7).</summary>
        public BayCount Bays { get; }

        /// <summary>Pillar cells as a 64-bit cell mask. Fixed level data, never part of the state.</summary>
        public ulong Pillars { get; }

        /// <summary>Vehicles by PieceId (ascending anchor order).</summary>
        public ReadOnlyArray<Vehicle> Vehicles { get; }

        public ReadOnlyArray<LotClue> Clues { get; }

        public int VehicleCount => Vehicles.Count;

        /// <summary>The exited mask once every vehicle has left.</summary>
        public uint AllExitedMask { get; }

        /// <summary>Vehicles that start under a cover.</summary>
        public uint CoveredAtStartMask { get; }

        public bool IsCoveredLevel => CoveredAtStartMask != 0;

        public Vehicle VehicleOf(PieceId piece) => Vehicles[piece.Value];

        /// <summary>The cells the vehicle occupies, as a cell mask.</summary>
        public ulong FootprintOf(PieceId piece) => _footprints[piece.Value];

        /// <summary>The cells from the one in front of the vehicle's front up to the lot edge, nearest first.</summary>
        public IReadOnlyList<LotCell> PathOf(PieceId piece) => _paths[piece.Value];

        /// <summary>The vehicle standing on <paramref name="cell"/> at the level start, if any.</summary>
        public Option<PieceId> PieceAt(LotCell cell)
        {
            byte piece = _pieceAtCell[cell.Index];
            return piece == NoPiece ? Option.None<PieceId>() : Option.Some(new PieceId(piece));
        }

        /// <summary>The first vehicle on the path at the level start: the fixed reference of an Ahead tag.</summary>
        public Option<PieceId> FirstOnPathAtStart(PieceId piece) => _firstOnPathAtStart[piece.Value];

        /// <summary>The nearest blocker on the vehicle's path, given which vehicles have exited.</summary>
        public Option<Blocker> FirstBlocker(PieceId piece, uint exitedMask)
        {
            foreach (LotCell cell in _paths[piece.Value])
            {
                if ((Pillars & cell.Bit) != 0)
                {
                    return Option.Some<Blocker>(new Blocker.Pillar(cell));
                }

                byte standing = _pieceAtCell[cell.Index];
                bool isOccupied = standing != NoPiece && (exitedMask & (1U << standing)) == 0;
                if (isOccupied)
                {
                    return Option.Some<Blocker>(new Blocker.Piece(new PieceId(standing)));
                }
            }

            return Option.None<Blocker>();
        }

        private Option<PieceId> FirstPieceOnPath(PieceId piece, uint exitedMask)
        {
            foreach (LotCell cell in _paths[piece.Value])
            {
                byte standing = _pieceAtCell[cell.Index];
                if (standing != NoPiece && (exitedMask & (1U << standing)) == 0)
                {
                    return Option.Some(new PieceId(standing));
                }
            }

            return Option.None<PieceId>();
        }
    }
}

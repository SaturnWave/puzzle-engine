using System;

namespace PuzzleCore.Jam
{
    /// <summary>A vehicle as level data. Its PieceId is its index in ascending anchor order.</summary>
    public readonly struct Vehicle : IEquatable<Vehicle>
    {
        public Vehicle(PieceId id, LotCell anchor, VehicleLength length, Facing facing, ColourId colour, bool startsCovered)
        {
            Id = id;
            Anchor = anchor;
            Length = length;
            Facing = facing;
            Colour = colour;
            StartsCovered = startsCovered;
        }

        public PieceId Id { get; }

        /// <summary>The lowest cell index of the footprint.</summary>
        public LotCell Anchor { get; }

        public VehicleLength Length { get; }

        public Facing Facing { get; }

        public ColourId Colour { get; }

        public bool StartsCovered { get; }

        public bool IsHorizontal => Facing == Facing.East || Facing == Facing.West;

        public bool Equals(Vehicle other) =>
            Id == other.Id && Anchor == other.Anchor && Length == other.Length && Facing == other.Facing
            && Colour == other.Colour && StartsCovered == other.StartsCovered;

        public override bool Equals(object? obj) => obj is Vehicle other && Equals(other);

        public override int GetHashCode() => (Id.Value * 64) + Anchor.Index;

        public static bool operator ==(Vehicle left, Vehicle right) => left.Equals(right);

        public static bool operator !=(Vehicle left, Vehicle right) => !left.Equals(right);
    }

    /// <summary>How an Ahead tag relates a covered vehicle to the first vehicle on its path at the level start.</summary>
    public enum AheadRelation
    {
        Same,
        Differs,
    }

    /// <summary>A clue tag on a covered vehicle. Closed set (greybox families).</summary>
    public abstract record LotClue
    {
        private LotClue()
        {
        }

        public abstract PieceId On { get; }

        /// <summary>"Not red": the covered vehicle is not this colour.</summary>
        public sealed record Not(PieceId On, ColourId Colour) : LotClue
        {
            public override PieceId On { get; } = On;
        }

        /// <summary>Same colour as, or a different colour from, the first vehicle on this vehicle's path at the level start.</summary>
        public sealed record Ahead(PieceId On, AheadRelation Relation) : LotClue
        {
            public override PieceId On { get; } = On;
        }

        /// <summary>Exactly Count vehicles of Colour share an edge with this footprint at the level start.</summary>
        public sealed record Touch(PieceId On, ColourId Colour, byte Count) : LotClue
        {
            public override PieceId On { get; } = On;
        }
    }

    /// <summary>What stops a vehicle: another vehicle, or a pillar. Closed set.</summary>
    public abstract record Blocker
    {
        private Blocker()
        {
        }

        public sealed record Piece(PieceId Id) : Blocker;

        public sealed record Pillar(LotCell Cell) : Blocker;
    }

    /// <summary>Why a tap is not a move. It costs nothing and is never recorded. Closed set.</summary>
    public abstract record ExitRejection
    {
        private ExitRejection()
        {
        }

        /// <summary>The vehicle has already left, or no such vehicle exists in this level.</summary>
        public sealed record NotInLot : ExitRejection;

        /// <summary>The level is already won or failed.</summary>
        public sealed record NotInPlay : ExitRejection;

        /// <summary>Something stands on the path; the client bumps and highlights it.</summary>
        public sealed record Blocked(Blocker By) : ExitRejection;
    }

    /// <summary>The one Jam move: tap a vehicle so it drives out.</summary>
    public readonly struct Exit : IEquatable<Exit>
    {
        public Exit(PieceId piece) => Piece = piece;

        public PieceId Piece { get; }

        public bool Equals(Exit other) => Piece == other.Piece;

        public override bool Equals(object? obj) => obj is Exit other && Equals(other);

        public override int GetHashCode() => Piece.Value;

        public override string ToString() => "Exit(" + Piece.ToString() + ")";

        public static bool operator ==(Exit left, Exit right) => left.Equals(right);

        public static bool operator !=(Exit left, Exit right) => !left.Equals(right);
    }
}

using System;
using System.Globalization;

namespace PuzzleCore.Jam
{
    /// <summary>The way a vehicle points along its long axis; its front is the end it faces.</summary>
    public enum Facing : byte
    {
        North,
        East,
        South,
        West,
    }

    /// <summary>A car (2 cells) or a van (3 cells).</summary>
    public enum VehicleLength : byte
    {
        Two = 2,
        Three = 3,
    }

    /// <summary>A row of the lot, 0 at the top.</summary>
    public readonly struct Row : IEquatable<Row>
    {
        public Row(byte value) => Value = value;

        public byte Value { get; }

        public bool Equals(Row other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is Row other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(Row left, Row right) => left.Equals(right);

        public static bool operator !=(Row left, Row right) => !left.Equals(right);
    }

    /// <summary>A column of the lot, 0 at the left.</summary>
    public readonly struct Column : IEquatable<Column>
    {
        public Column(byte value) => Value = value;

        public byte Value { get; }

        public bool Equals(Column other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is Column other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(Column left, Column right) => left.Equals(right);

        public static bool operator !=(Column left, Column right) => !left.Equals(right);
    }

    /// <summary>
    /// A cell of the lot, indexed row-major (row × width + column), 0-63. Named LotCell, not
    /// Cell, so it never clashes with Block.Core.Geometry in a client that uses both.
    /// </summary>
    public readonly struct LotCell : IEquatable<LotCell>
    {
        public const byte MaxIndex = 63;

        public LotCell(byte index)
        {
            if (index > MaxIndex)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "A lot cell index is 0-63.");
            }

            Index = index;
        }

        public byte Index { get; }

        /// <summary>The bit of this cell in a 64-bit cell mask.</summary>
        public ulong Bit => 1UL << Index;

        public bool Equals(LotCell other) => Index == other.Index;

        public override bool Equals(object? obj) => obj is LotCell other && Equals(other);

        public override int GetHashCode() => Index;

        public override string ToString() => Index.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(LotCell left, LotCell right) => left.Equals(right);

        public static bool operator !=(LotCell left, LotCell right) => !left.Equals(right);
    }

    /// <summary>The lot's width and height, each 5-8.</summary>
    public readonly struct LotSize : IEquatable<LotSize>
    {
        public const byte MinSide = 5;
        public const byte MaxSide = 8;

        private LotSize(byte width, byte height)
        {
            Width = width;
            Height = height;
        }

        public byte Width { get; }

        public byte Height { get; }

        public int CellCount => Width * Height;

        /// <summary>None unless both sides are 5-8.</summary>
        public static Option<LotSize> Of(int width, int height)
        {
            bool isInRange = width >= MinSide && width <= MaxSide && height >= MinSide && height <= MaxSide;
            return isInRange ? Option.Some(new LotSize((byte)width, (byte)height)) : Option.None<LotSize>();
        }

        public bool Contains(int index) => index >= 0 && index < CellCount;

        public LotCell CellAt(Row row, Column column)
        {
            if (row.Value >= Height || column.Value >= Width)
            {
                throw new ArgumentOutOfRangeException(nameof(row), "The position lies outside the lot.");
            }

            return new LotCell((byte)((row.Value * Width) + column.Value));
        }

        public Row RowOf(LotCell cell) => new Row((byte)(cell.Index / Width));

        public Column ColumnOf(LotCell cell) => new Column((byte)(cell.Index % Width));

        /// <summary>The neighbour one step towards <paramref name="facing"/>, or None at the lot edge.</summary>
        public Option<LotCell> Step(LotCell cell, Facing facing)
        {
            int row = RowOf(cell).Value;
            int column = ColumnOf(cell).Value;
            (int nextRow, int nextColumn) = facing switch
            {
                Facing.North => (row - 1, column),
                Facing.East => (row, column + 1),
                Facing.South => (row + 1, column),
                Facing.West => (row, column - 1),
                _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "Unknown facing."),
            };

            bool isInside = nextRow >= 0 && nextRow < Height && nextColumn >= 0 && nextColumn < Width;
            return isInside ? Option.Some(new LotCell((byte)((nextRow * Width) + nextColumn))) : Option.None<LotCell>();
        }

        public bool Equals(LotSize other) => Width == other.Width && Height == other.Height;

        public override bool Equals(object? obj) => obj is LotSize other && Equals(other);

        public override int GetHashCode() => (Width * 16) + Height;

        public override string ToString() => Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(LotSize left, LotSize right) => left.Equals(right);

        public static bool operator !=(LotSize left, LotSize right) => !left.Equals(right);
    }

    /// <summary>The number of bays at the kerb: 4-7 when a level loads, 8 only through one spare.</summary>
    public readonly struct BayCount : IEquatable<BayCount>
    {
        public const byte MinAtLoad = 4;
        public const byte MaxAtLoad = 7;
        public const byte PackingLimit = 8;

        private BayCount(byte value) => Value = value;

        public byte Value { get; }

        /// <summary>None unless 4-7: the range a level may start with.</summary>
        public static Option<BayCount> AtLoad(int value) =>
            value >= MinAtLoad && value <= MaxAtLoad ? Option.Some(new BayCount((byte)value)) : Option.None<BayCount>();

        /// <summary>One more bay, or None at the 8-slot packing limit.</summary>
        public Option<BayCount> PlusOneSpare() =>
            Value < PackingLimit ? Option.Some(new BayCount((byte)(Value + 1))) : Option.None<BayCount>();

        public CapacityCount AsCapacity => new CapacityCount(Value);

        public bool Equals(BayCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is BayCount other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(BayCount left, BayCount right) => left.Equals(right);

        public static bool operator !=(BayCount left, BayCount right) => !left.Equals(right);
    }
}

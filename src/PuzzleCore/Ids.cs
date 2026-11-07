using System;
using System.Globalization;

namespace PuzzleCore
{
    /// <summary>
    /// A colour, 1-15. Zero is reserved for "empty" in packed state words, so default(ColourId)
    /// never names a real colour.
    /// </summary>
    public readonly struct ColourId : IEquatable<ColourId>
    {
        public const byte MinValue = 1;
        public const byte MaxValue = 15;

        public ColourId(byte value)
        {
            if (value < MinValue || value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A colour id is 1-15.");
            }

            Value = value;
        }

        public byte Value { get; }

        public bool Equals(ColourId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ColourId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ColourId left, ColourId right) => left.Equals(right);

        public static bool operator !=(ColourId left, ColourId right) => !left.Equals(right);
    }

    /// <summary>A vial on the Sort shelf, 0-13 (13 at the start plus one spare).</summary>
    public readonly struct VialId : IEquatable<VialId>
    {
        public const byte MaxValue = 13;

        public VialId(byte value)
        {
            if (value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A vial id is 0-13.");
            }

            Value = value;
        }

        public byte Value { get; }

        public bool Equals(VialId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is VialId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(VialId left, VialId right) => left.Equals(right);

        public static bool operator !=(VialId left, VialId right) => !left.Equals(right);
    }

    /// <summary>A layer inside a Sort vial, 0-3; 0 is the bottom.</summary>
    public readonly struct LayerIndex : IEquatable<LayerIndex>
    {
        public const byte MaxValue = 3;

        public LayerIndex(byte value)
        {
            if (value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A layer index is 0-3.");
            }

            Value = value;
        }

        public byte Value { get; }

        public bool Equals(LayerIndex other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is LayerIndex other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(LayerIndex left, LayerIndex right) => left.Equals(right);

        public static bool operator !=(LayerIndex left, LayerIndex right) => !left.Equals(right);
    }

    /// <summary>A Jam vehicle: its index in ascending anchor order, 0-23 (24 is the exited-mask width).</summary>
    public readonly struct PieceId : IEquatable<PieceId>
    {
        public const byte MaxValue = 23;

        public PieceId(byte value)
        {
            if (value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A piece id is 0-23.");
            }

            Value = value;
        }

        public byte Value { get; }

        public bool Equals(PieceId other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is PieceId other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(PieceId left, PieceId right) => left.Equals(right);

        public static bool operator !=(PieceId left, PieceId right) => !left.Equals(right);
    }

    /// <summary>A bay in the Jam holding row, 0-7 (7 at the start plus one spare); 0 is the leftmost.</summary>
    public readonly struct SlotIndex : IEquatable<SlotIndex>
    {
        public const byte MaxValue = 7;

        public SlotIndex(byte value)
        {
            if (value > MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A slot index is 0-7.");
            }

            Value = value;
        }

        public byte Value { get; }

        public bool Equals(SlotIndex other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is SlotIndex other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(SlotIndex left, SlotIndex right) => left.Equals(right);

        public static bool operator !=(SlotIndex left, SlotIndex right) => !left.Equals(right);
    }

    /// <summary>
    /// A hidden cell of a clue level, game-neutral: Sort uses vial × 4 + layer, Jam uses the
    /// covered vehicle's PieceId.
    /// </summary>
    public readonly struct HiddenCell : IEquatable<HiddenCell>
    {
        public HiddenCell(byte value) => Value = value;

        public byte Value { get; }

        public bool Equals(HiddenCell other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is HiddenCell other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(HiddenCell left, HiddenCell right) => left.Equals(right);

        public static bool operator !=(HiddenCell left, HiddenCell right) => !left.Equals(right);
    }

    /// <summary>A level's position inside its pack, from 0.</summary>
    public readonly struct LevelIndex : IEquatable<LevelIndex>
    {
        public LevelIndex(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A level index is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(LevelIndex other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is LevelIndex other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(LevelIndex left, LevelIndex right) => left.Equals(right);

        public static bool operator !=(LevelIndex left, LevelIndex right) => !left.Equals(right);
    }

    /// <summary>An index into an ActionRecord's action list, from 0.</summary>
    public readonly struct ActionIndex : IEquatable<ActionIndex>
    {
        public ActionIndex(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "An action index is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(ActionIndex other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ActionIndex other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ActionIndex left, ActionIndex right) => left.Equals(right);

        public static bool operator !=(ActionIndex left, ActionIndex right) => !left.Equals(right);
    }
}

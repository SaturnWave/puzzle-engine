using System;
using System.Globalization;

namespace PuzzleCore
{
    /// <summary>A number of moves (pours or exits). Never negative.</summary>
    public readonly struct MoveCount : IEquatable<MoveCount>, IComparable<MoveCount>
    {
        public MoveCount(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A move count is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(MoveCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is MoveCount other && Equals(other);

        public override int GetHashCode() => Value;

        public int CompareTo(MoveCount other) => Value.CompareTo(other.Value);

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(MoveCount left, MoveCount right) => left.Equals(right);

        public static bool operator !=(MoveCount left, MoveCount right) => !left.Equals(right);

        public static bool operator <(MoveCount left, MoveCount right) => left.Value < right.Value;

        public static bool operator >(MoveCount left, MoveCount right) => left.Value > right.Value;

        public static bool operator <=(MoveCount left, MoveCount right) => left.Value <= right.Value;

        public static bool operator >=(MoveCount left, MoveCount right) => left.Value >= right.Value;
    }

    /// <summary>A number of deduction steps. Never negative.</summary>
    public readonly struct StepCount : IEquatable<StepCount>
    {
        public StepCount(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A step count is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(StepCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is StepCount other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(StepCount left, StepCount right) => left.Equals(right);

        public static bool operator !=(StepCount left, StepCount right) => !left.Equals(right);
    }

    /// <summary>A ratio in thousandths, 0-1000. Ratios are integers so results never depend on floating point.</summary>
    public readonly struct PerMille : IEquatable<PerMille>
    {
        public const int Whole = 1000;

        public PerMille(int value)
        {
            if (value < 0 || value > Whole)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A per-mille value is 0-1000.");
            }

            Value = value;
        }

        public int Value { get; }

        /// <summary>part / whole in thousandths, rounded down. whole must be positive and part in 0..whole.</summary>
        public static PerMille Of(int part, int whole)
        {
            if (whole <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(whole), whole, "The whole must be positive.");
            }

            if (part < 0 || part > whole)
            {
                throw new ArgumentOutOfRangeException(nameof(part), part, "The part must lie in 0..whole.");
            }

            return new PerMille((int)((long)part * Whole / whole));
        }

        public bool Equals(PerMille other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is PerMille other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(PerMille left, PerMille right) => left.Equals(right);

        public static bool operator !=(PerMille left, PerMille right) => !left.Equals(right);
    }

    /// <summary>Capacity units: vials (Sort) or holding slots (Jam). Never negative.</summary>
    public readonly struct CapacityCount : IEquatable<CapacityCount>, IComparable<CapacityCount>
    {
        public CapacityCount(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A capacity count is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(CapacityCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is CapacityCount other && Equals(other);

        public override int GetHashCode() => Value;

        public int CompareTo(CapacityCount other) => Value.CompareTo(other.Value);

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(CapacityCount left, CapacityCount right) => left.Equals(right);

        public static bool operator !=(CapacityCount left, CapacityCount right) => !left.Equals(right);

        public static bool operator <(CapacityCount left, CapacityCount right) => left.Value < right.Value;

        public static bool operator >(CapacityCount left, CapacityCount right) => left.Value > right.Value;

        public static bool operator <=(CapacityCount left, CapacityCount right) => left.Value <= right.Value;

        public static bool operator >=(CapacityCount left, CapacityCount right) => left.Value >= right.Value;
    }

    /// <summary>Fail-offer screens shown. Never negative.</summary>
    public readonly struct OfferCount : IEquatable<OfferCount>
    {
        public OfferCount(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "An offer count is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(OfferCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is OfferCount other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(OfferCount left, OfferCount right) => left.Equals(right);

        public static bool operator !=(OfferCount left, OfferCount right) => !left.Equals(right);
    }

    /// <summary>Rewarded ads watched. Never negative.</summary>
    public readonly struct AdCount : IEquatable<AdCount>
    {
        public AdCount(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "An ad count is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(AdCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is AdCount other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(AdCount left, AdCount right) => left.Equals(right);

        public static bool operator !=(AdCount left, AdCount right) => !left.Equals(right);
    }

    /// <summary>
    /// How many search nodes a solver, grader or checker may visit. Budgets count nodes, never
    /// wall-clock time, so the same seed gives the same result on every machine.
    /// </summary>
    public readonly struct NodeBudget : IEquatable<NodeBudget>
    {
        public NodeBudget(int nodes)
        {
            if (nodes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nodes), nodes, "A node budget is at least 1.");
            }

            Nodes = nodes;
        }

        public int Nodes { get; }

        public bool Equals(NodeBudget other) => Nodes == other.Nodes;

        public override bool Equals(object? obj) => obj is NodeBudget other && Equals(other);

        public override int GetHashCode() => Nodes;

        public override string ToString() => Nodes.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(NodeBudget left, NodeBudget right) => left.Equals(right);

        public static bool operator !=(NodeBudget left, NodeBudget right) => !left.Equals(right);
    }

    /// <summary>Search nodes actually visited. Never negative.</summary>
    public readonly struct NodeCount : IEquatable<NodeCount>
    {
        public NodeCount(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A node count is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        public bool Equals(NodeCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is NodeCount other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(NodeCount left, NodeCount right) => left.Equals(right);

        public static bool operator !=(NodeCount left, NodeCount right) => !left.Equals(right);
    }
}

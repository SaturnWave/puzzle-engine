using System;
using System.Globalization;

namespace PuzzleCore.Rules
{
    /// <summary>
    /// The exact identity of a state: 128-bit FNV-1a over all packed words in id order. Used by
    /// replays, rewind and golden tests. Two states with the same ExactKey are the same state.
    /// </summary>
    public readonly struct ExactKey : IEquatable<ExactKey>
    {
        public ExactKey(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }

        public ulong Low { get; }

        public bool Equals(ExactKey other) => High == other.High && Low == other.Low;

        public override bool Equals(object? obj) => obj is ExactKey other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Low ^ (Low >> 32) ^ High));

        /// <summary>32 lowercase hex digits, high word first.</summary>
        public override string ToString() =>
            High.ToString("x16", CultureInfo.InvariantCulture) + Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(ExactKey left, ExactKey right) => left.Equals(right);

        public static bool operator !=(ExactKey left, ExactKey right) => !left.Equals(right);
    }

    /// <summary>
    /// A reduced identity that merges states the rules cannot tell apart (Sort: permuted
    /// identical vials; Jam: the same exited set with another bay order). Only the solver's
    /// transposition table uses it.
    /// </summary>
    public readonly struct CanonicalKey : IEquatable<CanonicalKey>
    {
        public CanonicalKey(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        public ulong High { get; }

        public ulong Low { get; }

        public bool Equals(CanonicalKey other) => High == other.High && Low == other.Low;

        public override bool Equals(object? obj) => obj is CanonicalKey other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Low ^ (Low >> 32) ^ High));

        public override string ToString() =>
            High.ToString("x16", CultureInfo.InvariantCulture) + Low.ToString("x16", CultureInfo.InvariantCulture);

        public static bool operator ==(CanonicalKey left, CanonicalKey right) => left.Equals(right);

        public static bool operator !=(CanonicalKey left, CanonicalKey right) => !left.Equals(right);
    }
}

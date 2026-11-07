using System;

namespace PuzzleCore.Randomness
{
    /// <summary>
    /// The full state of a <see cref="Pcg32"/> generator. Game states store this value, so a
    /// generator can be resumed exactly. Ported unchanged from Block.Core.Randomness.
    /// </summary>
    public readonly struct Pcg32State : IEquatable<Pcg32State>
    {
        public Pcg32State(ulong state, ulong increment)
        {
            if ((increment & 1UL) == 0)
            {
                throw new ArgumentException("A PCG32 increment must be odd.", nameof(increment));
            }

            State = state;
            Increment = increment;
        }

        public ulong State { get; }

        /// <summary>Selects the stream; always odd.</summary>
        public ulong Increment { get; }

        public bool Equals(Pcg32State other) => State == other.State && Increment == other.Increment;

        public override bool Equals(object? obj) => obj is Pcg32State other && Equals(other);

        public override int GetHashCode() => unchecked((int)(State ^ (State >> 32) ^ Increment ^ (Increment >> 32)));

        public static bool operator ==(Pcg32State left, Pcg32State right) => left.Equals(right);

        public static bool operator !=(Pcg32State left, Pcg32State right) => !left.Equals(right);
    }

    /// <summary>
    /// PCG32 (XSH-RR variant, 64-bit state, 32-bit output) by Melissa O'Neill. Integer-only,
    /// so it produces the same numbers on every platform and runtime. Each instance is one
    /// stream; different increments give independent streams.
    /// </summary>
    public sealed class Pcg32
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private readonly ulong _increment;

        private Pcg32(ulong state, ulong increment)
        {
            _state = state;
            _increment = increment;
        }

        /// <summary>Seeds like the reference pcg32_srandom_r(initState, initSequence).</summary>
        public static Pcg32 Seeded(ulong initialState, ulong sequence)
        {
            ulong increment = unchecked((sequence << 1) | 1UL);
            var generator = new Pcg32(0UL, increment);
            generator.NextUInt32();
            generator._state = unchecked(generator._state + initialState);
            generator.NextUInt32();
            return generator;
        }

        public static Pcg32 Resume(Pcg32State state) => new Pcg32(state.State, state.Increment);

        public Pcg32State Snapshot() => new Pcg32State(_state, _increment);

        public uint NextUInt32()
        {
            // PCG arithmetic is defined modulo 2^64 and 2^32, so wrapping is intended here.
            unchecked
            {
                ulong previous = _state;
                _state = (previous * Multiplier) + _increment;
                uint xorShifted = (uint)(((previous >> 18) ^ previous) >> 27);
                int rotation = (int)(previous >> 59);
                return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
            }
        }

        /// <summary>
        /// A uniformly distributed number in [0, bound), without modulo bias (Lemire's
        /// multiply-and-reject method). The number of values drawn depends only on the
        /// generator's state, so it is the same on every platform.
        /// </summary>
        public uint NextBelow(uint bound)
        {
            if (bound == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bound), "The bound must be at least 1.");
            }

            // Wrapping multiplication and negation modulo 2^32 are part of the method.
            unchecked
            {
                ulong product = (ulong)NextUInt32() * bound;
                uint low = (uint)product;
                if (low < bound)
                {
                    uint threshold = (0U - bound) % bound;
                    while (low < threshold)
                    {
                        product = (ulong)NextUInt32() * bound;
                        low = (uint)product;
                    }
                }

                return (uint)(product >> 32);
            }
        }

        /// <summary>An index in [0, count) for picking one of <paramref name="count"/> items.</summary>
        public int NextIndex(int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "There must be at least one item to pick from.");
            }

            return (int)NextBelow((uint)count);
        }
    }
}

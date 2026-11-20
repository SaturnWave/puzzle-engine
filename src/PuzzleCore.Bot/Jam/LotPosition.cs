using System;
using PuzzleCore.Jam;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// A lot position as Covered Lot's bots model it: the vehicles gone, how many of each colour wait in the bays (0-2,
    /// since three of a colour leave together), the bays in use and the bays there are. The bays' order is dropped, because
    /// the rules clear by count.
    /// </summary>
    public readonly struct LotPosition : IEquatable<LotPosition>
    {
        private const int BitsPerColour = 4;
        private const ulong CountMask = 0xFUL;

        // Bit 0 of every colour's count: a count of 1 or 2 sets it in (counts | counts >> 1).
        private const ulong LowBitOfEachCount = 0x1111_1111_1111_1111UL;

        // Colour c's count sits in bits 4c to 4c+3.
        private readonly ulong _waiting;

        private LotPosition(uint exited, ulong waiting, int baysInUse, int bays)
        {
            Exited = exited;
            _waiting = waiting;
            BaysInUse = baysInUse;
            Bays = bays;
        }

        /// <summary>Bit p is set once vehicle p has left the lot.</summary>
        public uint Exited { get; }

        public int BaysInUse { get; }

        /// <summary>The bays there are: the level's own (bots never take the spare).</summary>
        public int Bays { get; }

        /// <summary>Every bay holds a vehicle: the rules' SlotsFull, unless the level is won.</summary>
        public bool BaysFull => BaysInUse >= Bays;

        /// <summary>How many colours have a vehicle waiting.</summary>
        public int ColoursWaiting
        {
            get
            {
                int colours = 0;
                for (ulong left = (_waiting | (_waiting >> 1)) & LowBitOfEachCount; left != 0; left &= left - 1)
                {
                    colours++;
                }

                return colours;
            }
        }

        /// <summary>The position the rules' state shows. The bays hold only vehicles that have left, so every colour in them was seen.</summary>
        public static LotPosition Of(LotState state)
        {
            ulong waiting = 0;
            int baysInUse = state.OccupiedSlots;
            for (int slot = 0; slot < baysInUse; slot++)
            {
                if (state.ColourIn(new SlotIndex((byte)slot)).TryGet(out ColourId colour))
                {
                    waiting += 1UL << (colour.Value * BitsPerColour);
                }
            }

            return new LotPosition(state.Exited, waiting, baysInUse, state.Capacity.Value);
        }

        /// <summary>How many vehicles of the colour wait in the bays.</summary>
        public int Waiting(ColourId colour) => (int)((_waiting >> (colour.Value * BitsPerColour)) & CountMask);

        /// <summary>
        /// The position after the vehicle leaves with the colour the bot believes it has: it parks in a bay, and when that
        /// makes three of its colour, the three leave together.
        /// </summary>
        public LotPosition After(PieceId piece, ColourId colour)
        {
            uint bit = 1U << piece.Value;
            if ((Exited & bit) != 0)
            {
                throw new InvalidOperationException("A vehicle leaves the lot only once.");
            }

            int shift = colour.Value * BitsPerColour;
            bool makesASet = ((_waiting >> shift) & CountMask) == JamRules.SetSize - 1;
            return makesASet
                ? new LotPosition(Exited | bit, _waiting & ~(CountMask << shift), BaysInUse - (JamRules.SetSize - 1), Bays)
                : new LotPosition(Exited | bit, _waiting + (1UL << shift), BaysInUse + 1, Bays);
        }

        public bool Equals(LotPosition other) =>
            Exited == other.Exited && _waiting == other._waiting && BaysInUse == other.BaysInUse && Bays == other.Bays;

        public override bool Equals(object? obj) => obj is LotPosition other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Exited ^ (uint)_waiting ^ (uint)(_waiting >> 32) ^ ((uint)Bays << 28)));

        public static bool operator ==(LotPosition left, LotPosition right) => left.Equals(right);

        public static bool operator !=(LotPosition left, LotPosition right) => !left.Equals(right);
    }
}

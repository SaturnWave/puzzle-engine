using System;
using PuzzleCore.Hashing;
using PuzzleCore.Rules;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// The Jam state: which vehicles have exited, the colours waiting in the bays (left-packed,
    /// one 4-bit ColourId per slot, 0 = empty) and the slot count. Geometry, colours and covers
    /// are level data; a vehicle is covered while it starts covered and has not exited.
    /// </summary>
    public readonly struct LotState : IPuzzleState<LotState>
    {
        private const int BitsPerSlot = 4;
        private const uint SlotMask = 0xFU;

        internal LotState(uint exited, uint slots, BayCount capacity)
        {
            Exited = exited;
            Slots = slots;
            Capacity = capacity;
        }

        /// <summary>Bit p is set once vehicle p has left the lot (≤ 24 bits).</summary>
        public uint Exited { get; }

        /// <summary>Slot i holds the colour in bits 4i..4i+3; occupied slots are always the leftmost ones.</summary>
        public uint Slots { get; }

        /// <summary>Bays available now: the level's own, plus one if a spare was taken.</summary>
        public BayCount Capacity { get; }

        public int OccupiedSlots
        {
            get
            {
                int count = 0;
                while (count < BayCount.PackingLimit && ColourNibble(count) != 0)
                {
                    count++;
                }

                return count;
            }
        }

        public int FreeSlots => Capacity.Value - OccupiedSlots;

        public static LotState Start(LotLevel level) => new LotState(0U, 0U, level.Bays);

        public bool HasExited(PieceId piece) => (Exited & (1U << piece.Value)) != 0;

        /// <summary>The colour waiting in a bay, or None when the bay is empty.</summary>
        public Option<ColourId> ColourIn(SlotIndex slot)
        {
            uint nibble = ColourNibble(slot.Value);
            return nibble == 0 ? Option.None<ColourId>() : Option.Some(new ColourId((byte)nibble));
        }

        public ExactKey Exact
        {
            get
            {
                Fnv1a128 hash = Fnv1a128.Start();
                hash.AddWord(Exited);
                hash.AddWord(Slots);
                hash.AddWord(Capacity.Value);
                return new ExactKey(hash.High, hash.Low);
            }
        }

        /// <summary>
        /// The bays hold (exited of colour c) mod 3 for each colour, so the exited mask and the slot
        /// count fix everything the rules can see; only the bay order is dropped.
        /// </summary>
        public CanonicalKey Canonical => new CanonicalKey(Capacity.Value, Exited);

        public bool Equals(LotState other) => Exited == other.Exited && Slots == other.Slots && Capacity == other.Capacity;

        public override bool Equals(object? obj) => obj is LotState other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Exited ^ (Slots * 31U) ^ ((uint)Capacity.Value << 28)));

        public static bool operator ==(LotState left, LotState right) => left.Equals(right);

        public static bool operator !=(LotState left, LotState right) => !left.Equals(right);

        internal LotState WithCapacity(BayCount capacity) => new LotState(Exited, Slots, capacity);

        internal static uint PackSlots(ReadOnlySpan<byte> colours)
        {
            uint slots = 0;
            for (int index = 0; index < colours.Length; index++)
            {
                slots |= (uint)colours[index] << (index * BitsPerSlot);
            }

            return slots;
        }

        private uint ColourNibble(int slot) => (Slots >> (slot * BitsPerSlot)) & SlotMask;
    }
}

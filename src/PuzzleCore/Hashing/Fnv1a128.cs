namespace PuzzleCore.Hashing
{
    /// <summary>
    /// FNV-1a, 128-bit, fed byte by byte (64-bit words little-endian). netstandard2.1 has no
    /// UInt128, so the state is two ulongs and the multiply by the FNV prime 2^88 + 0x13B is
    /// written out. Integer-only: the same bytes give the same key on every platform.
    /// </summary>
    public struct Fnv1a128
    {
        private const ulong OffsetBasisHigh = 0x6C62272E07BB0142UL;
        private const ulong OffsetBasisLow = 0x62B821756295C58DUL;
        private const ulong PrimeLowPart = 0x13BUL;

        private ulong _high;
        private ulong _low;

        public static Fnv1a128 Start() => new Fnv1a128 { _high = OffsetBasisHigh, _low = OffsetBasisLow };

        public ulong High => _high;

        public ulong Low => _low;

        public void AddByte(byte value)
        {
            _low ^= value;
            MultiplyByPrime();
        }

        public void AddWord(ulong word)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                AddByte((byte)((word >> shift) & 0xFF));
            }
        }

        private void MultiplyByPrime()
        {
            // Arithmetic modulo 2^128 by definition of FNV: (high, low) * (2^88 + 0x13B).
            unchecked
            {
                ulong lowTimesPrimeLow = (_low & 0xFFFFFFFFUL) * PrimeLowPart;
                ulong highHalfTimesPrimeLow = (_low >> 32) * PrimeLowPart;
                ulong middle = (lowTimesPrimeLow >> 32) + (highHalfTimesPrimeLow & 0xFFFFFFFFUL);
                ulong carry = (highHalfTimesPrimeLow >> 32) + (middle >> 32);
                ulong newLow = lowTimesPrimeLow + (highHalfTimesPrimeLow << 32);

                // The 2^88 term shifts (high, low) left by 88: only low's bottom 40 bits land in the high word.
                ulong newHigh = (_high * PrimeLowPart) + carry + (_low << 24);

                _high = newHigh;
                _low = newLow;
            }
        }
    }
}

using System;
using System.Globalization;
using System.Text;

namespace PuzzleCore.Randomness
{
    /// <summary>
    /// The purposes that each get their own random stream, so drawing for one purpose never
    /// shifts another. The NAMES are fixed forever (they are hashed into stream keys); add new
    /// purposes at the end only. Streams use the name, never the ordinal.
    /// </summary>
    public enum RandomPurpose
    {
        Layout,
        Palette,
        Scramble,
        Cover,
        Clues,
        Bot,
        Playout,
    }

    /// <summary>A 64-bit seed, written as an unsigned decimal string because JSON numbers cannot hold every value.</summary>
    public readonly struct Seed : IEquatable<Seed>
    {
        public Seed(ulong value) => Value = value;

        public ulong Value { get; }

        /// <summary>Parses the plain decimal form (no sign, no leading zero); None when the text is not a 64-bit unsigned integer.</summary>
        public static Option<Seed> Parse(string text)
        {
            bool isPlainDigits = text.Length > 0 && text.Length <= 20 && IsAllDigits(text) && (text.Length == 1 || text[0] != '0');
            return isPlainDigits && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong value)
                ? Option.Some(new Seed(value))
                : Option.None<Seed>();
        }

        /// <summary>The seed of a pack, derived from its id so a pack id alone reproduces the pack.</summary>
        public static Seed ForPack(PackId pack)
        {
            ulong mixer = SeedDerivation.Fnv1a64("pack|" + pack.Value);
            return new Seed(SeedDerivation.SplitMix64(ref mixer));
        }

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public bool Equals(Seed other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is Seed other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Value ^ (Value >> 32)));

        public static bool operator ==(Seed left, Seed right) => left.Equals(right);

        public static bool operator !=(Seed left, Seed right) => !left.Equals(right);

        private static bool IsAllDigits(string text)
        {
            foreach (char character in text)
            {
                if (character < '0' || character > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>A pack id: lowercase ASCII letters, digits and '-', 1-64 characters (for example "daily-jam-2026-11-08").</summary>
    public readonly struct PackId : IEquatable<PackId>
    {
        private PackId(string value) => Value = value;

        public string Value { get; }

        public static Option<PackId> Parse(string text) =>
            IsWellFormed(text) ? Option.Some(new PackId(text)) : Option.None<PackId>();

        public bool Equals(PackId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PackId other && Equals(other);

        public override int GetHashCode() => unchecked((int)SeedDerivation.Fnv1a64(Value ?? string.Empty));

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(PackId left, PackId right) => left.Equals(right);

        public static bool operator !=(PackId left, PackId right) => !left.Equals(right);

        private static bool IsWellFormed(string text)
        {
            if (text.Length == 0 || text.Length > 64)
            {
                return false;
            }

            foreach (char character in text)
            {
                bool isAllowed = (character >= 'a' && character <= 'z') || (character >= '0' && character <= '9') || character == '-';
                if (!isAllowed)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The text that names one random stream. Built with string.Concat and invariant number
    /// formatting, never interpolation, so it is the same on every device:
    /// generator "{purpose}|{pack}:{level}:{try}", bots "Bot|{pack}:{level}:{profile}:{run}",
    /// playouts "Playout|{pack}:{level}:{playout}".
    /// </summary>
    public readonly struct StreamKey : IEquatable<StreamKey>
    {
        private StreamKey(string text) => Text = text;

        public string Text { get; }

        /// <summary>A generator stream. Bot and Playout have their own builders.</summary>
        public static StreamKey ForGenerator(RandomPurpose purpose, PackId pack, LevelIndex level, int tryIndex)
        {
            if (purpose == RandomPurpose.Bot || purpose == RandomPurpose.Playout)
            {
                throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Bot and Playout streams have their own key builders.");
            }

            RequireNotNegative(tryIndex, nameof(tryIndex));
            return new StreamKey(string.Concat(PurposeName(purpose), "|", pack.Value, ":", Number(level.Value), ":", Number(tryIndex)));
        }

        public static StreamKey ForBot(PackId pack, LevelIndex level, string profileName, int run)
        {
            RequireNotNegative(run, nameof(run));
            return new StreamKey(string.Concat(
                PurposeName(RandomPurpose.Bot), "|", pack.Value, ":", Number(level.Value), ":", profileName + ":" + Number(run)));
        }

        public static StreamKey ForPlayout(PackId pack, LevelIndex level, int playout)
        {
            RequireNotNegative(playout, nameof(playout));
            return new StreamKey(string.Concat(PurposeName(RandomPurpose.Playout), "|", pack.Value, ":", Number(level.Value), ":", Number(playout)));
        }

        /// <summary>The fixed name of each purpose. Part of the pack contract: never change one.</summary>
        public static string PurposeName(RandomPurpose purpose) => purpose switch
        {
            RandomPurpose.Layout => "Layout",
            RandomPurpose.Palette => "Palette",
            RandomPurpose.Scramble => "Scramble",
            RandomPurpose.Cover => "Cover",
            RandomPurpose.Clues => "Clues",
            RandomPurpose.Bot => "Bot",
            RandomPurpose.Playout => "Playout",
            _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown random purpose."),
        };

        public bool Equals(StreamKey other) => string.Equals(Text, other.Text, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is StreamKey other && Equals(other);

        public override int GetHashCode() => unchecked((int)SeedDerivation.Fnv1a64(Text ?? string.Empty));

        public override string ToString() => Text ?? string.Empty;

        public static bool operator ==(StreamKey left, StreamKey right) => left.Equals(right);

        public static bool operator !=(StreamKey left, StreamKey right) => !left.Equals(right);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static void RequireNotNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name, value, "Must not be negative.");
            }
        }
    }

    /// <summary>
    /// Derives an independent PCG32 stream from a seed and a stream key:
    /// SplitMix64(seed XOR FNV-1a-64(UTF-8 key)) gives the initial state, then the sequence.
    /// Ported from Block.Core.Randomness, keyed by StreamKey text instead of a purpose enum.
    /// </summary>
    public static class SeedDerivation
    {
        public static Pcg32 Stream(Seed seed, StreamKey key)
        {
            ulong mixer = seed.Value ^ Fnv1a64(key.Text);
            ulong initialState = SplitMix64(ref mixer);
            ulong sequence = SplitMix64(ref mixer);
            return Pcg32.Seeded(initialState, sequence);
        }

        /// <summary>FNV-1a, 64-bit, over the UTF-8 bytes of the text.</summary>
        public static ulong Fnv1a64(string text)
        {
            const ulong OffsetBasis = 14695981039346656037UL;
            const ulong Prime = 1099511628211UL;
            ulong hash = OffsetBasis;
            foreach (byte value in Encoding.UTF8.GetBytes(text))
            {
                // FNV is defined modulo 2^64.
                hash = unchecked((hash ^ value) * Prime);
            }

            return hash;
        }

        /// <summary>One step of SplitMix64 (Steele, Lea, Flood); advances <paramref name="state"/>.</summary>
        public static ulong SplitMix64(ref ulong state)
        {
            // SplitMix64 is defined modulo 2^64.
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong mixed = state;
                mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
                mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
                return mixed ^ (mixed >> 31);
            }
        }
    }
}

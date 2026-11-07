using System;
using PuzzleCore.Randomness;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    public sealed class RandomnessTests
    {
        private static readonly PackId GoldenPack = PackId.Parse("golden-jam").Or(default);

        [Fact]
        public void Pcg32_reproduces_the_reference_demo_sequence()
        {
            // pcg32-demo from the reference implementation: srandom(42, 54).
            Pcg32 generator = Pcg32.Seeded(42UL, 54UL);

            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (uint value in expected)
            {
                Assert.Equal(value, generator.NextUInt32());
            }
        }

        [Fact]
        public void Stream_keys_are_built_from_fixed_names_and_invariant_numbers()
        {
            Assert.Equal("Layout|golden-jam:3:7", StreamKey.ForGenerator(RandomPurpose.Layout, GoldenPack, new LevelIndex(3), 7).Text);
            Assert.Equal("Bot|golden-jam:0:Greedy:12", StreamKey.ForBot(GoldenPack, new LevelIndex(0), "Greedy", 12).Text);
            Assert.Equal("Playout|golden-jam:41:999", StreamKey.ForPlayout(GoldenPack, new LevelIndex(41), 999).Text);
        }

        [Fact]
        public void Generator_keys_refuse_the_bot_and_playout_purposes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StreamKey.ForGenerator(RandomPurpose.Bot, GoldenPack, new LevelIndex(0), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => StreamKey.ForGenerator(RandomPurpose.Playout, GoldenPack, new LevelIndex(0), 0));
        }

        [Fact]
        public void Two_stream_keys_give_pinned_golden_values()
        {
            // Golden values: a change here changes every generated pack, so it needs a new generatorVersion.
            Seed seed = Seed.ForPack(GoldenPack);
            Pcg32 layout = SeedDerivation.Stream(seed, StreamKey.ForGenerator(RandomPurpose.Layout, GoldenPack, new LevelIndex(0), 0));
            Pcg32 palette = SeedDerivation.Stream(seed, StreamKey.ForGenerator(RandomPurpose.Palette, GoldenPack, new LevelIndex(0), 0));

            Assert.Equal(GoldenValues.PackSeed, seed.Value);
            Assert.Equal(GoldenValues.LayoutFirst, layout.NextUInt32());
            Assert.Equal(GoldenValues.PaletteFirst, palette.NextUInt32());
        }

        [Fact]
        public void Pack_ids_are_lowercase_ascii_letters_digits_and_dashes()
        {
            Assert.True(PackId.Parse("daily-jam-2026-11-08").HasValue);
            Assert.False(PackId.Parse("Daily").HasValue);
            Assert.False(PackId.Parse("İ").HasValue);
            Assert.False(PackId.Parse(string.Empty).HasValue);
        }

        [Fact]
        public void Seeds_parse_only_plain_decimal_text()
        {
            Assert.Equal(Option.Some(new Seed(18446744073709551615UL)), Seed.Parse("18446744073709551615"));
            Assert.False(Seed.Parse("-1").HasValue);
            Assert.False(Seed.Parse("007").HasValue);
            Assert.False(Seed.Parse("18446744073709551616").HasValue);
        }

        /// <summary>Computed by an independent Python implementation of FNV-1a-64, SplitMix64 and PCG32 (2026-11-07).</summary>
        private static class GoldenValues
        {
            public const ulong PackSeed = 2600279022768060080UL;
            public const uint LayoutFirst = 1515456353U;
            public const uint PaletteFirst = 3062451731U;
        }
    }
}

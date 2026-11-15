using System;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    /// <summary>Level ids (TC14, Q66): a pack's slot has the id "{packId}-{slot:0000}"; the record reader is unchanged.</summary>
    public sealed class LevelIdTests
    {
        [Theory]
        [InlineData("campaign-jam", 24, "campaign-jam-0024")]
        [InlineData("campaign-jam", 0, "campaign-jam-0000")]
        [InlineData("golden-gen", 9999, "golden-gen-9999")]
        [InlineData("golden-gen", 12345, "golden-gen-12345")]
        public void A_slot_id_is_the_pack_id_then_the_slot_in_at_least_four_digits(string pack, int slot, string expected)
        {
            Assert.Equal(LevelId.Parse(expected), LevelId.For(Pack(pack), new LevelIndex(slot)));
        }

        [Fact]
        public void A_slot_id_has_at_most_64_characters_or_there_is_none()
        {
            string longestPack = new string('a', 59);

            Assert.True(LevelId.For(Pack(longestPack), new LevelIndex(9999)).TryGet(out LevelId longest));
            Assert.Equal(64, longest.Value.Length);
            Assert.False(LevelId.For(Pack(longestPack + "a"), new LevelIndex(0)).HasValue);
            Assert.False(LevelId.For(Pack(longestPack), new LevelIndex(10000)).HasValue);
        }

        [Fact]
        public void The_empty_default_pack_id_gives_no_slot_id()
        {
            Assert.False(LevelId.For(default, new LevelIndex(3)).HasValue);
        }

        [Fact]
        public void The_record_reader_still_accepts_a_hand_made_level_id()
        {
            // The golden 6x6 records keep "six-by-six" (Q66): reading does not require the slot form.
            Assert.True(LevelId.Parse("six-by-six").HasValue);
            Assert.False(LevelId.Parse("Six-by-six").HasValue);
            Assert.False(LevelId.Parse(new string('a', 65)).HasValue);
        }

        private static PackId Pack(string text) =>
            PackId.Parse(text).TryGet(out PackId pack) ? pack : throw new ArgumentException("Not a pack id: " + text, nameof(text));
    }
}

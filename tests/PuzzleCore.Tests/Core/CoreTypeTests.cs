using System;
using System.Text;
using PuzzleCore.Hashing;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    public sealed class CoreTypeTests
    {
        [Fact]
        public void Identity_values_are_equal_exactly_when_their_values_are()
        {
            Assert.Equal(new ColourId(3), new ColourId(3));
            Assert.NotEqual(new ColourId(3), new ColourId(4));
            Assert.True(new PieceId(7) == new PieceId(7));
            Assert.True(new SlotIndex(1) != new SlotIndex(2));
            Assert.Equal(new MoveCount(5), new MoveCount(5));
            Assert.True(new CapacityCount(4) < new CapacityCount(5));
            Assert.Equal(new LevelIndex(12).GetHashCode(), new LevelIndex(12).GetHashCode());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(16)]
        public void A_colour_id_outside_1_to_15_is_refused(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ColourId((byte)value));
        }

        [Fact]
        public void Range_checked_ids_refuse_values_past_their_packing_limit()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PieceId(24));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlotIndex(8));
            Assert.Throws<ArgumentOutOfRangeException>(() => new VialId(14));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LayerIndex(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerMille(1001));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveCount(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new NodeBudget(0));
        }

        [Fact]
        public void Per_mille_of_a_part_rounds_down()
        {
            Assert.Equal(new PerMille(333), PerMille.Of(1, 3));
            Assert.Equal(new PerMille(1000), PerMille.Of(7, 7));
            Assert.Equal(new PerMille(0), PerMille.Of(0, 9));
        }

        [Fact]
        public void An_option_is_none_by_default_and_some_holds_its_value()
        {
            Option<int> none = default;
            Option<int> some = Option.Some(42);

            Assert.False(none.TryGet(out _));
            Assert.True(some.TryGet(out int value));
            Assert.Equal(42, value);
            Assert.Equal(Option.None<int>(), none);
            Assert.NotEqual(none, some);
            Assert.Equal(7, none.Or(7));
        }

        [Fact]
        public void A_read_only_array_copies_its_source_and_compares_element_wise()
        {
            int[] source = { 1, 2, 3 };
            ReadOnlyArray<int> array = ReadOnlyArray.Of(source);
            source[0] = 99;

            Assert.Equal(1, array[0]);
            Assert.Equal(ReadOnlyArray.Of(1, 2, 3), array);
            Assert.NotEqual(ReadOnlyArray.Of(1, 3, 2), array);
            Assert.True(default(ReadOnlyArray<int>).IsEmpty);
            Assert.Equal(ReadOnlyArray.Empty<int>(), default);
        }

        [Fact]
        public void An_outcome_is_a_closed_set_with_value_equality()
        {
            Assert.Equal(new Outcome.Failed(FailKind.SlotsFull), new Outcome.Failed(FailKind.SlotsFull));
            Assert.NotEqual<Outcome>(new Outcome.Failed(FailKind.SlotsFull), new Outcome.Failed(FailKind.NoLegalMove));
            Assert.NotEqual<Outcome>(new Outcome.Won(), new Outcome.InPlay());
        }

        [Theory]
        [InlineData("", "6c62272e07bb014262b821756295c58d")]
        [InlineData("a", "d228cb696f1a8caf78912b704e4a8964")]
        [InlineData("foobar", "343e1662793c64bf6f0d3597ba446f18")]
        public void Fnv1a128_matches_the_reference_vectors(string text, string expected)
        {
            Fnv1a128 hash = Fnv1a128.Start();
            foreach (byte value in Encoding.UTF8.GetBytes(text))
            {
                hash.AddByte(value);
            }

            Assert.Equal(expected, new ExactKey(hash.High, hash.Low).ToString());
        }

        [Fact]
        public void Fnv1a128_feeds_a_word_little_endian()
        {
            Fnv1a128 hash = Fnv1a128.Start();
            hash.AddWord(0x0123456789ABCDEFUL);

            Assert.Equal("0619098f38659878f047fc4523abfdfd", new ExactKey(hash.High, hash.Low).ToString());
        }
    }
}

using System;
using System.Linq;
using PuzzleCore.Bot;
using PuzzleCore.Randomness;
using Xunit;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>The bot names are fixed forever: they seed the runs' Bot streams and name the sim CSV's rows (Q75, Q84).</summary>
    public sealed class BotNamesTests
    {
        [Fact]
        public void Every_profile_and_end_reason_has_its_fixed_name_in_its_fixed_place()
        {
            Assert.Equal(new[] { "Greedy", "Planner1", "Planner3", "Expert" }, Enum.GetValues<BotProfileId>().Select(BotNames.Of));
            Assert.Equal(new[] { "Won", "NoLegalMove", "SlotsFull", "Cycle", "StepCap" }, Enum.GetValues<BotEndReason>().Select(BotNames.Of));
        }

        [Fact]
        public void A_value_outside_either_enum_has_no_name()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BotNames.Of((BotProfileId)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => BotNames.Of((BotProfileId)4));
            Assert.Throws<ArgumentOutOfRangeException>(() => BotNames.Of((BotEndReason)(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => BotNames.Of((BotEndReason)5));
        }

        [Fact]
        public void A_run_stream_key_names_its_pack_slot_profile_and_run()
        {
            PackId pack = PackId.Parse("campaign-jam").Or(default);

            StreamKey key = StreamKey.ForBot(pack, new LevelIndex(24), BotNames.Of(BotProfileId.Planner3), 17);

            Assert.Equal("Bot|campaign-jam:24:Planner3:17", key.Text);
        }
    }
}

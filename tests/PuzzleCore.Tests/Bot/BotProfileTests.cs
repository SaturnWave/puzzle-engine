using System;
using PuzzleCore.Bot;
using PuzzleCore.Search;
using Xunit;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>A bot profile and its search: the shared shapes of shared-design.md §2 (Q74-Q76).</summary>
    public sealed class BotProfileTests
    {
        [Fact]
        public void A_profile_carries_its_fixed_name_search_and_mistake_rate()
        {
            var search = new BotSearch.Limited(new MoveCount(3), DeductionRung.D2);

            var profile = new BotProfile(BotProfileId.Planner3, search, new PerMille(50));

            Assert.Equal((BotProfileId.Planner3, "Planner3", (BotSearch)search, new PerMille(50)), (profile.Id, profile.Name, profile.Search, profile.Mistakes));
        }

        [Fact]
        public void A_profile_outside_the_enum_is_refused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BotProfile((BotProfileId)4, new BotSearch.Habit(), new PerMille(0)));
        }

        [Theory]
        [InlineData(0, DeductionRung.D1)]
        [InlineData(1, (DeductionRung)(-1))]
        [InlineData(1, (DeductionRung)3)]
        public void A_limited_search_looks_at_least_one_move_ahead_up_to_a_rung_of_the_ladder(int plies, DeductionRung ceiling)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new BotSearch.Limited(new MoveCount(plies), ceiling));
        }

        [Fact]
        public void Two_searches_are_equal_when_they_have_the_same_shape()
        {
            Assert.Equal(new BotSearch.Limited(new MoveCount(1), DeductionRung.D1), new BotSearch.Limited(new MoveCount(1), DeductionRung.D1));
            Assert.NotEqual<BotSearch>(new BotSearch.Limited(new MoveCount(1), DeductionRung.D1), new BotSearch.Limited(new MoveCount(3), DeductionRung.D1));
            Assert.NotEqual<BotSearch>(new BotSearch.Habit(), new BotSearch.FullSolver());
        }
    }
}

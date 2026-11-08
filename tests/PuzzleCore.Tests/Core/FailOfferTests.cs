using PuzzleCore.Play;
using PuzzleCore.Records;
using Xunit;

namespace PuzzleCore.Tests.Core
{
    /// <summary>Offer eligibility (shared engine §4): when the fail screen opens and what it may sell.</summary>
    public sealed class FailOfferTests
    {
        private static readonly NearMiss NearMissYes = new NearMiss.Yes(new MoveCount(4));

        private static readonly RewardedAdCapState NoAdsYet = Ads(attempt: 0, session: 0, utcDay: 0);

        private static readonly PlayMode[] AllModes =
        {
            PlayMode.Campaign, PlayMode.DailyProof, PlayMode.Competitive, PlayMode.Casebook, PlayMode.WeeklyTwist, PlayMode.Siege,
        };

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Each_of_the_first_four_campaign_fails_in_a_session_opens_a_screen(int shown)
        {
            Assert.IsType<OfferSet.Screen>(FailOffer.Eligible(Context(shown: shown)));
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(40)]
        public void From_the_fifth_fail_in_a_session_only_the_free_retry_is_offered(int shown)
        {
            Assert.IsType<OfferSet.RetryOnly>(FailOffer.Eligible(Context(shown: shown)));
        }

        [Theory]
        [InlineData(PlayMode.DailyProof)]
        [InlineData(PlayMode.Competitive)]
        [InlineData(PlayMode.Casebook)]
        [InlineData(PlayMode.WeeklyTwist)]
        [InlineData(PlayMode.Siege)]
        public void No_mode_but_campaign_opens_a_screen_even_when_everything_could_win(PlayMode mode)
        {
            Assert.IsType<OfferSet.RetryOnly>(FailOffer.Eligible(Context(mode: mode)));
        }

        [Fact]
        public void A_fail_that_already_showed_its_screen_never_shows_a_second()
        {
            Assert.IsType<OfferSet.RetryOnly>(FailOffer.Eligible(Context(thisFail: FailScreen.AlreadyShown)));
        }

        [Fact]
        public void A_screen_opens_and_counts_even_when_nothing_can_be_sold()
        {
            OfferSet offer = FailOffer.Eligible(Context(nearMiss: new NearMiss.No(), reach: RewindReach.NotWinnable));

            Assert.Equal(Screen(), offer);
        }

        [Fact]
        public void A_near_miss_and_a_winnable_rewind_sell_both_and_the_ad_grants_the_spare()
        {
            Assert.Equal(
                Screen(PaidOption.SpareForGold, PaidOption.RewindForGold, PaidOption.AdForSpare),
                FailOffer.Eligible(Context()));
        }

        [Fact]
        public void Without_a_near_miss_the_ad_grants_the_rewind()
        {
            Assert.Equal(
                Screen(PaidOption.RewindForGold, PaidOption.AdForRewind),
                FailOffer.Eligible(Context(nearMiss: new NearMiss.No())));
        }

        [Fact]
        public void A_near_miss_alone_sells_the_spare_for_gold_or_for_an_ad()
        {
            Assert.Equal(
                Screen(PaidOption.SpareForGold, PaidOption.AdForSpare),
                FailOffer.Eligible(Context(reach: RewindReach.NotWinnable)));
        }

        [Fact]
        public void Only_a_near_miss_sells_the_spare()
        {
            foreach (NearMiss notYes in new NearMiss[] { new NearMiss.No(), new NearMiss.SpareUnavailable(), new NearMiss.Unknown(new NodeCount(500_000)) })
            {
                Assert.Equal(Screen(), FailOffer.Eligible(Context(nearMiss: notYes, reach: RewindReach.NotWinnable)));
            }
        }

        [Fact]
        public void A_used_spare_is_never_sold_again_even_on_a_near_miss()
        {
            Assert.Equal(Screen(), FailOffer.Eligible(Context(reach: RewindReach.NotWinnable, spare: SpareUse.Used)));
            Assert.Equal(
                Screen(PaidOption.RewindForGold, PaidOption.AdForRewind),
                FailOffer.Eligible(Context(spare: SpareUse.Used)));
        }

        [Theory]
        [InlineData(RewindReach.NotWinnable)]
        [InlineData(RewindReach.Unknown)]
        public void Only_a_winnable_rewind_is_sold_for_gold_or_for_an_ad(RewindReach reach)
        {
            Assert.Equal(Screen(), FailOffer.Eligible(Context(nearMiss: new NearMiss.No(), reach: reach)));
        }

        [Fact]
        public void An_unknown_result_blocks_only_the_option_it_decides()
        {
            var unknownNearMiss = new NearMiss.Unknown(new NodeCount(500_000));

            Assert.Equal(
                Screen(PaidOption.RewindForGold, PaidOption.AdForRewind),
                FailOffer.Eligible(Context(nearMiss: unknownNearMiss)));
            Assert.Equal(
                Screen(PaidOption.SpareForGold, PaidOption.AdForSpare),
                FailOffer.Eligible(Context(reach: RewindReach.Unknown)));
            Assert.Equal(Screen(), FailOffer.Eligible(Context(nearMiss: unknownNearMiss, reach: RewindReach.Unknown)));
        }

        [Fact]
        public void An_enum_value_outside_its_set_offers_nothing()
        {
            // A C# enum is not closed at run time, so a client can pass a cast or a corrupt saved value.
            Assert.IsType<OfferSet.RetryOnly>(FailOffer.Eligible(Context(mode: (PlayMode)99)));
            Assert.IsType<OfferSet.RetryOnly>(FailOffer.Eligible(Context(thisFail: (FailScreen)99)));
            Assert.Equal(
                Screen(PaidOption.RewindForGold, PaidOption.AdForRewind),
                FailOffer.Eligible(Context(spare: (SpareUse)99)));
            Assert.Equal(
                Screen(PaidOption.SpareForGold, PaidOption.AdForSpare),
                FailOffer.Eligible(Context(reach: (RewindReach)99)));
            Assert.Equal(
                Screen(PaidOption.SpareForGold, PaidOption.RewindForGold),
                FailOffer.Eligible(Context(ads: NoAdsYet with { FirstGame = (FirstGame)99 })));
        }

        [Theory]
        [InlineData(1, 0, 0)]
        [InlineData(0, 2, 0)]
        [InlineData(0, 0, 5)]
        [InlineData(3, 7, 9)]
        public void No_rewarded_ad_once_any_cap_is_reached(int attempt, int session, int utcDay)
        {
            OfferSet offer = FailOffer.Eligible(Context(ads: Ads(attempt, session, utcDay)));

            Assert.Equal(Screen(PaidOption.SpareForGold, PaidOption.RewindForGold), offer);
        }

        [Fact]
        public void A_rewarded_ad_just_inside_every_cap_is_offered()
        {
            OfferSet offer = FailOffer.Eligible(Context(ads: Ads(attempt: 0, session: 1, utcDay: 4)));

            Assert.Equal(Screen(PaidOption.SpareForGold, PaidOption.RewindForGold, PaidOption.AdForSpare), offer);
        }

        [Fact]
        public void No_rewarded_ad_in_the_players_first_game_but_gold_stays_on_sale()
        {
            RewardedAdCapState firstGame = NoAdsYet with { FirstGame = FirstGame.InProgress };

            OfferSet offer = FailOffer.Eligible(Context(ads: firstGame));

            Assert.Equal(Screen(PaidOption.SpareForGold, PaidOption.RewindForGold), offer);
        }

        [Fact]
        public void Every_context_follows_the_offer_rules()
        {
            // The rules of shared engine §2 and §4, checked as separate properties over every combination.
            var nearMisses = new NearMiss[] { NearMissYes, new NearMiss.No(), new NearMiss.SpareUnavailable(), new NearMiss.Unknown(new NodeCount(9)) };
            var reaches = new[] { RewindReach.Winnable, RewindReach.NotWinnable, RewindReach.Unknown };
            var adStates = new[] { NoAdsYet, Ads(1, 0, 0), Ads(0, 2, 0), Ads(0, 0, 5), NoAdsYet with { FirstGame = FirstGame.InProgress } };
            int checkedScreens = 0;
            foreach (PlayMode mode in AllModes)
            {
                foreach (NearMiss nearMiss in nearMisses)
                {
                    foreach (RewindReach reach in reaches)
                    {
                        foreach (int shown in new[] { 0, 3, 4 })
                        {
                            foreach (FailScreen thisFail in new[] { FailScreen.NotYetShown, FailScreen.AlreadyShown })
                            {
                                foreach (SpareUse spare in new[] { SpareUse.Unused, SpareUse.Used })
                                {
                                    foreach (RewardedAdCapState ads in adStates)
                                    {
                                        var context = new OfferContext(
                                            Mode: mode,
                                            NearMiss: nearMiss,
                                            RewindReach: reach,
                                            OffersShownThisSession: new OfferCount(shown),
                                            ThisFail: thisFail,
                                            SpareThisAttempt: spare,
                                            AdCap: ads);
                                        checkedScreens += CheckRules(context, FailOffer.Eligible(context)) ? 1 : 0;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Campaign, not yet offered, 2 of 3 session counts: 4 × 3 × 2 × 2 × 5 contexts open a screen.
            Assert.Equal(240, checkedScreens);
        }

        /// <summary>Asserts every eligibility rule for one context; returns whether a screen opened.</summary>
        private static bool CheckRules(OfferContext context, OfferSet offer)
        {
            bool mayOpen = context.Mode == PlayMode.Campaign && context.ThisFail == FailScreen.NotYetShown && context.OffersShownThisSession.Value < 4;
            if (!mayOpen)
            {
                Assert.IsType<OfferSet.RetryOnly>(offer);
                return false;
            }

            ReadOnlyArray<PaidOption> paid = Assert.IsType<OfferSet.Screen>(offer).Paid;
            bool spareSellable = context.NearMiss is NearMiss.Yes && context.SpareThisAttempt == SpareUse.Unused;
            bool rewindSellable = context.RewindReach == RewindReach.Winnable;

            Assert.Equal(spareSellable, Contains(paid, PaidOption.SpareForGold));
            Assert.Equal(rewindSellable, Contains(paid, PaidOption.RewindForGold));
            Assert.False(!spareSellable && Contains(paid, PaidOption.AdForSpare));
            Assert.False(!rewindSellable && Contains(paid, PaidOption.AdForRewind));

            int ads = Count(paid, PaidOption.AdForSpare) + Count(paid, PaidOption.AdForRewind);
            bool adGrantsSomething = spareSellable || rewindSellable;
            bool adExpected = adGrantsSomething && CapsAllowAnAd(context.AdCap);
            Assert.Equal(adExpected ? 1 : 0, ads);
            Assert.False(spareSellable && Contains(paid, PaidOption.AdForRewind));

            for (int index = 1; index < paid.Count; index++)
            {
                Assert.True(paid[index - 1] < paid[index], "Paid options are listed once each, in PaidOption order.");
            }

            return true;
        }

        /// <summary>The caps written out apart from RewardedAdCapState.AllowsOneMore: K8 (1 per attempt), K24 (not in the first game), the brief's 2 per session and 5 per UTC day.</summary>
        private static bool CapsAllowAnAd(RewardedAdCapState ads) =>
            ads.FirstGame == FirstGame.Completed && ads.ThisAttempt.Value < 1 && ads.ThisSession.Value < 2 && ads.ThisUtcDay.Value < 5;

        private static OfferContext Context(
            PlayMode mode = PlayMode.Campaign,
            NearMiss? nearMiss = null,
            RewindReach reach = RewindReach.Winnable,
            int shown = 0,
            FailScreen thisFail = FailScreen.NotYetShown,
            SpareUse spare = SpareUse.Unused,
            RewardedAdCapState? ads = null) => new(
                Mode: mode,
                NearMiss: nearMiss ?? NearMissYes,
                RewindReach: reach,
                OffersShownThisSession: new OfferCount(shown),
                ThisFail: thisFail,
                SpareThisAttempt: spare,
                AdCap: ads ?? NoAdsYet);

        private static RewardedAdCapState Ads(int attempt, int session, int utcDay) => new(
            FirstGame: FirstGame.Completed,
            ThisAttempt: new AdCount(attempt),
            ThisSession: new AdCount(session),
            ThisUtcDay: new AdCount(utcDay));

        private static OfferSet Screen(params PaidOption[] paid) => new OfferSet.Screen(ReadOnlyArray.Of(paid));

        private static bool Contains(ReadOnlyArray<PaidOption> paid, PaidOption option) => Count(paid, option) > 0;

        private static int Count(ReadOnlyArray<PaidOption> paid, PaidOption option)
        {
            int count = 0;
            foreach (PaidOption item in paid)
            {
                count += item == option ? 1 : 0;
            }

            return count;
        }
    }
}

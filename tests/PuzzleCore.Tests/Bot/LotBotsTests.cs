using System;
using System.Linq;
using PuzzleCore.Bot;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>Covered Lot's four bots (Q73-Q76): profiles, move cap and policies; and Expert's engine check (Q74).</summary>
    public sealed class LotBotsTests
    {
        [Fact]
        public void Covered_lot_has_four_bots_in_a_fixed_order_with_the_settings_of_round_7()
        {
            Assert.Equal(new[] { "Greedy", "Planner1", "Planner3", "Expert" }, LotBots.Profiles.Select(profile => profile.Name));
            Assert.IsType<BotSearch.Habit>(LotBots.Greedy.Search);
            var planner1 = Assert.IsType<BotSearch.Limited>(LotBots.Planner1.Search);
            var planner3 = Assert.IsType<BotSearch.Limited>(LotBots.Planner3.Search);
            Assert.IsType<BotSearch.FullSolver>(LotBots.Expert.Search);
            Assert.Equal((new MoveCount(1), DeductionRung.D1), (planner1.LookaheadPlies, planner1.Ceiling));
            Assert.Equal((new MoveCount(3), DeductionRung.D2), (planner3.LookaheadPlies, planner3.Ceiling));
            Assert.Equal(new[] { 0, 150, 50, 0 }, LotBots.Profiles.Select(profile => profile.Mistakes.Value));
        }

        [Fact]
        public void A_run_may_make_three_moves_per_vehicle()
        {
            LotLevel level = Load(SixBySix());

            Assert.Equal(new MoveCount(27), LotBots.MoveCapFor(level));
        }

        [Fact]
        public void Each_profile_plays_the_policy_of_its_search_with_its_lookahead_and_ceiling()
        {
            BuiltLevel built = CampaignLevels.All[0].Built;

            Assert.IsType<GreedyPolicy>(LotBots.PolicyFor(LotBots.Greedy, built));
            Assert.IsType<ExpertPolicy>(LotBots.PolicyFor(LotBots.Expert, built));
            var planner1 = Assert.IsType<PlannerPolicy>(LotBots.PolicyFor(LotBots.Planner1, built));
            var planner3 = Assert.IsType<PlannerPolicy>(LotBots.PolicyFor(LotBots.Planner3, built));
            Assert.Equal((new MoveCount(1), DeductionRung.D1), (planner1.LookaheadPlies, planner1.Ceiling));
            Assert.Equal((new MoveCount(3), DeductionRung.D2), (planner3.LookaheadPlies, planner3.Ceiling));
        }

        [Fact]
        public void The_calibration_pack_builds_98_levels_16_of_them_covered()
        {
            Assert.Equal(98, CampaignLevels.All.Count);
            Assert.DoesNotContain(CampaignLevels.All, level => level.Slot is 62 or 92);
            Assert.Equal(16, CampaignLevels.Covered.Count());
        }

        [Fact]
        public void Expert_wins_every_calibration_level_on_its_par_path_without_drawing()
        {
            foreach (CampaignLevel campaign in CampaignLevels.All)
            {
                Pcg32 random = BotStreams.For(CampaignLevels.Pack, campaign.Index, LotBots.Expert, 0);
                Pcg32State before = random.Snapshot();

                BotRun<LotState, Exit, ExitRejection> run = Play(LotBots.Expert, campaign.Built, random);

                Assert.Equal(BotEndReason.Won, run.EndReason);
                Assert.Equal(campaign.Built.ParPath, run.Steps.Select(step => step.Move));
                Assert.Equal(before, random.Snapshot());
            }
        }

        [Fact]
        public void A_par_path_that_ends_with_the_level_still_in_play_is_an_engine_fault()
        {
            BuiltLevel built = CampaignLevels.All[0].Built;
            BuiltLevel cut = built with { ParPath = ReadOnlyArray.CopyOf(built.ParPath.Take(built.ParPath.Count - 1)) };

            Assert.Throws<InvalidOperationException>(() => Play(LotBots.Expert, cut, Pcg32.Seeded(1, 1)));
        }

        [Fact]
        public void Greedy_and_expert_read_every_cover_so_they_never_guess()
        {
            BuiltLevel built = CampaignLevels.Covered.First().Built;

            Assert.False(LotBots.PolicyFor(LotBots.Greedy, built).Guesses.HasValue);
            Assert.False(LotBots.PolicyFor(LotBots.Expert, built).Guesses.HasValue);
        }

        internal static BotRun<LotState, Exit, ExitRejection> Play(BotProfile profile, BuiltLevel built, Pcg32 random) =>
            Play(profile, built, LotBots.PolicyFor(profile, built), random);

        internal static BotRun<LotState, Exit, ExitRejection> Play(BotProfile profile, BuiltLevel built, LotBotPolicy policy, Pcg32 random) =>
            BotRunner.Run(new JamRules(built.Level), LotState.Start(built.Level), profile, policy, random, LotBots.MoveCapFor(built.Level));
    }
}

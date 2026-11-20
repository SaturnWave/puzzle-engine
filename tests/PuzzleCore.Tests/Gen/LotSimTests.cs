using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Bot;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Gen;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Tests.Bot;
using Xunit;

namespace PuzzleCore.Tests.Gen
{
    /// <summary>
    /// sim's driver and reports (Q81, Q84, TC21, TC24) on four calibration slots: a plain level (0), and covered levels
    /// read at D1 (24), D2 (44) and D3 (64), so the Planners both read every cover and guess.
    /// </summary>
    public sealed class LotSimTests
    {
        private static readonly LevelIndex[] Sample = { new(0), new(24), new(44), new(64) };

        private static readonly Lazy<SimPack> Simulated = new(() => LotSim.Run(CampaignLevels.Pack, Sample, 4, withFailCauses: true));

        [Fact]
        public void Every_profile_runs_200_times_and_every_run_is_won_or_fills_the_bays()
        {
            SimPack pack = Simulated.Value;

            Assert.Equal(Sample, pack.Slots.Select(slot => slot.Slot));
            foreach (SimSlot.Simulated level in pack.Simulated)
            {
                Assert.Equal(LotBots.Profiles, level.Profiles.Select(profile => profile.Profile));
                foreach (SimProfileRuns profile in level.Profiles)
                {
                    Assert.Equal(Enumerable.Range(0, LotSim.RunsPerProfile), profile.Runs.Select(run => run.Run));
                    Assert.All(profile.Runs, run => Assert.True(run.EndReason is BotEndReason.Won or BotEndReason.SlotsFull));
                    Assert.All(profile.Runs, run => Assert.Equal(run.Failed, run.NearMiss.HasValue));
                    Assert.All(profile.Runs, run => Assert.True(run.EndReason == BotEndReason.Won ? run.Cleared.Value == 1000 : run.Cleared.Value < 1000));
                    Assert.All(profile.Runs, run => Assert.Equal(profile.Profile.Search is BotSearch.Limited, run.Guesses.HasValue));
                    Assert.All(profile.Runs, run => Assert.Equal(profile.Profile.Id == BotProfileId.Planner3 && run.Failed, run.Cause.HasValue));
                }
            }
        }

        [Fact]
        public void Expert_wins_every_run_on_its_par_path_and_greedy_and_expert_never_slip()
        {
            foreach (SimSlot.Simulated level in Simulated.Value.Simulated)
            {
                SimProfileRuns expert = Profile(level, BotProfileId.Expert);

                Assert.All(expert.Runs, run => Assert.Equal(BotEndReason.Won, run.EndReason));
                Assert.All(expert.Runs, run => Assert.Equal(level.Built.ParPath, run.Moves));
                Assert.All(Profile(level, BotProfileId.Greedy).Runs.Concat(expert.Runs), run => Assert.Equal(0, run.Mistakes));
            }
        }

        [Fact]
        public void A_run_is_the_shared_runner_on_its_own_bot_stream()
        {
            SimSlot.Simulated level = Simulated.Value.Simulated.Last();
            SimRun run = Profile(level, BotProfileId.Planner3).Runs[17];

            var replayed = BotRunner.Run(
                new JamRules(level.Built.Level), LotState.Start(level.Built.Level), LotBots.Planner3, LotBots.PolicyFor(LotBots.Planner3, level.Built),
                BotStreams.For(CampaignLevels.Pack, level.Slot, LotBots.Planner3, 17), LotBots.MoveCapFor(level.Built.Level));

            Assert.Equal(replayed.Steps.Select(step => step.Move), run.Moves);
            Assert.Equal(replayed.Steps.Count(step => step.WasMistake), run.Mistakes);
            Assert.Equal(replayed.EndReason, run.EndReason);
        }

        [Fact]
        public void One_worker_and_four_write_byte_identical_reports()
        {
            SimPack alone = LotSim.Run(CampaignLevels.Pack, Sample, 1, withFailCauses: true);

            Assert.Equal(LotSimCsv.Write(alone), LotSimCsv.Write(Simulated.Value));
            Assert.Equal(LotSimCsv.WriteFails(alone), LotSimCsv.WriteFails(Simulated.Value));
        }

        [Fact]
        public void The_report_lists_every_end_reason_of_every_profile_with_near_misses_only_on_fails()
        {
            string[] lines = LotSimCsv.Write(Simulated.Value).Split('\n');
            List<string[]> rows = lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')).ToList();

            Assert.Equal(LotSimCsv.Header, lines[0]);
            Assert.Equal(string.Empty, lines[^1]);
            Assert.Equal(4 * 4 * 5, rows.Count);
            Assert.All(rows, row => Assert.Equal(17, row.Length));
            Assert.Equal(
                new[] { "Won", "NoLegalMove", "SlotsFull", "Cycle", "StepCap" },
                rows.Take(5).Select(row => row[5]));
            Assert.All(rows.Where(row => row[5] is "Won" or "Cycle" or "StepCap"), row => Assert.True(row[7..13].All(field => field.Length == 0)));
            Assert.All(rows.Where(row => row[5] is "NoLegalMove" or "SlotsFull"), row => Assert.True(row[7..11].All(field => field.Length > 0)));
            Assert.All(rows.Where(row => row[6] == "0"), row => Assert.Equal(string.Empty, row[11]));
            Assert.All(rows.Where(row => row[4] is "Greedy" or "Expert"), row => Assert.Equal(new[] { string.Empty, string.Empty }, row[15..17]));
            Assert.All(rows.Where(row => row[4] is "Planner1" or "Planner3"), row => Assert.True(row[15].Length > 0 && row[16].Length > 0));
        }

        [Fact]
        public void The_report_counts_match_the_runs()
        {
            SimPack pack = Simulated.Value;
            List<string[]> rows = LotSimCsv.Write(pack).Split('\n').Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')).ToList();
            SimSlot.Simulated level = pack.Simulated.Last();
            SimProfileRuns planner3 = Profile(level, BotProfileId.Planner3);
            string[] slotsFull = rows.Single(row => row[1] == level.Slot.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) && row[4] == "Planner3" && row[5] == "SlotsFull");
            List<SimRun> fails = planner3.Runs.Where(run => run.Failed).ToList();

            Assert.Equal(level.Id.Value, slotsFull[0]);
            Assert.Equal(("Think", "D3"), (slotsFull[2], slotsFull[3]));
            Assert.Equal(Text(fails.Count), slotsFull[6]);
            Assert.Equal(Text(fails.Count(run => run.IsNearMiss)), slotsFull[7]);
            Assert.Equal(Text(PerMille.Of(fails.Count(run => run.IsNearMiss), fails.Count).Value), slotsFull[11]);
            Assert.Equal(Text(fails.Count(run => run.Mistakes > 0)), slotsFull[13]);
            Assert.Equal(Text(fails.Sum(run => run.Guesses.Or(new GuessTally(0, 0)).Wrong)), slotsFull[16]);
        }

        [Fact]
        public void The_fails_report_counts_each_profiles_fails_by_near_miss_tenth_and_planner3s_cause()
        {
            SimPack pack = Simulated.Value;
            string[] lines = LotSimCsv.WriteFails(pack).Split('\n');
            List<string[]> rows = lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')).ToList();

            Assert.Equal(LotSimCsv.FailsHeader, lines[0]);
            Assert.All(rows, row => Assert.Equal(8, row.Length));
            Assert.All(rows, row => Assert.InRange(int.Parse(row[4], System.Globalization.CultureInfo.InvariantCulture), 0, 9));
            Assert.All(rows, row => Assert.True(int.Parse(row[7], System.Globalization.CultureInfo.InvariantCulture) > 0));
            Assert.All(rows, row => Assert.Equal(row[2] == "Planner3", row[5].Length > 0));
            foreach (SimSlot.Simulated level in pack.Simulated)
            {
                foreach (SimProfileRuns profile in level.Profiles)
                {
                    int listed = rows.Where(row => row[0] == level.Id.Value && row[2] == profile.Profile.Name)
                        .Sum(row => int.Parse(row[7], System.Globalization.CultureInfo.InvariantCulture));
                    Assert.Equal(profile.Fails, listed);
                }
            }
        }

        [Fact]
        public void A_sample_record_replays_to_the_same_fail()
        {
            SimPack pack = Simulated.Value;
            SimSlot.Simulated level = pack.Simulated.First(slot => Profile(slot, BotProfileId.Planner3).Fails > 0);
            SimRun failed = Profile(level, BotProfileId.Planner3).Runs.First(run => run.Failed);

            ActionRecord<Exit> record = LotSimFiles.RecordOf(pack.Pack, level, failed);
            var reread = Assert.IsType<RecordReadResult<Exit>.Read>(RecordJson.Read(RecordJson.Write(record, ExitJson.Instance), ExitJson.Instance)).Record;
            var rules = new JamRules(level.Built.Level);
            ReplayReport report = Replay.Run(rules, rules.Start, reread.Mode, reread.Actions);

            Assert.Equal(LotText.Fingerprint(level.Built.Level), reread.Level.Fingerprint.Hex);
            var valid = Assert.IsType<ReplayVerdict.Valid>(report.Verdict);
            Assert.Equal(new Outcome.Failed(FailKind.SlotsFull), valid.Outcome);
            Assert.Equal(new MoveCount(failed.Moves.Count), valid.Moves);
        }

        [Fact]
        public void Gens_greedy_win_is_the_greedy_bots_won_per_mille_in_sim()
        {
            SimSlot.Simulated level = Simulated.Value.Simulated.First();

            Assert.Equal(Profile(level, BotProfileId.Greedy).WonPerMille, LotSim.GreedyWinPerMille(CampaignLevels.Pack, level.Slot, level.Built));
        }

        [Fact]
        public void Greedy_always_wins_free_cars_by_finishing_each_colour_it_started()
        {
            // Greedy keeps tapping the colour already waiting, so with every car free it never mixes three colours.
            LotLevel level = Jam.Boards.Load(Jam.Boards.FreeCars(
                4, Jam.Boards.Blue, Jam.Boards.Red, Jam.Boards.Yellow, Jam.Boards.Blue, Jam.Boards.Red, Jam.Boards.Yellow,
                Jam.Boards.Blue, Jam.Boards.Red, Jam.Boards.Yellow));
            var built = new BuiltLevel(level, default, ReadOnlyArray.Empty<Exit>(), Option.None<Search.DeductionGrade.Graded>());

            Assert.Equal(new PerMille(1000), LotSim.GreedyWinPerMille(CampaignLevels.Pack, new LevelIndex(0), built));
        }

        [Fact]
        public void Sim_needs_a_worker_a_slot_count_of_zero_or_more_and_room_for_the_level_ids()
        {
            // Level ids are {packId}-{slot:0000} with at most 64 characters, so a 60-character pack id leaves no room.
            PackId longPack = PackId.Parse(new string('p', 60)).Or(default);

            Assert.Throws<ArgumentOutOfRangeException>(() => LotSim.Run(CampaignLevels.Pack, 1, 0, withFailCauses: false));
            Assert.Throws<ArgumentOutOfRangeException>(() => LotSim.Run(CampaignLevels.Pack, -1, 1, withFailCauses: false));
            Assert.Throws<ArgumentException>(() => LotSim.Run(longPack, 1, 1, withFailCauses: false));
            Assert.Empty(LotSim.Run(CampaignLevels.Pack, 0, 1, withFailCauses: false).Slots);
        }

        private static SimProfileRuns Profile(SimSlot.Simulated level, BotProfileId id) =>
            level.Profiles.Single(profile => profile.Profile.Id == id);

        private static string Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

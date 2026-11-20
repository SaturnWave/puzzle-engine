using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// sim's driver and reports (Q81, Q84, TC21, TC24) on five calibration slots, given out of order: a plain level (0),
    /// slot 62, which the builder rejects, and covered levels read at D1 (24), D2 (44) and D3 (64), so the Planners both
    /// read every cover and guess.
    /// </summary>
    public sealed class LotSimTests
    {
        private static readonly LevelIndex[] Sample = { new(0), new(62), new(24), new(44), new(64) };

        private static readonly Lazy<SimPack> Simulated = new(() => LotSim.Run(CampaignLevels.Pack, Sample, 4, withFailCauses: true));

        [Fact]
        public void Every_profile_runs_200_times_and_every_run_is_won_or_fills_the_bays()
        {
            SimPack pack = Simulated.Value;

            Assert.Equal(Sample, pack.Slots.Select(slot => slot.Slot));
            Assert.IsType<SimSlot.Rejected>(pack.Slots[1]);
            Assert.Equal(new[] { 0, 24, 44, 64 }, pack.Simulated.Select(level => level.Slot.Value));
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
            SimSlot.Simulated level = Simulated.Value.Simulated.Single(slot => slot.Slot.Value == 44);
            SimProfileRuns planner1 = Profile(level, BotProfileId.Planner1);

            for (int run = 0; run < 30; run++)
            {
                SimRun kept = planner1.Runs[run];
                LotBotPolicy policy = LotBots.PolicyFor(LotBots.Planner1, level.Built);
                var replayed = BotRunner.Run(
                    new JamRules(level.Built.Level), LotState.Start(level.Built.Level), LotBots.Planner1, policy,
                    BotStreams.For(CampaignLevels.Pack, level.Slot, LotBots.Planner1, run), LotBots.MoveCapFor(level.Built.Level));

                Assert.Equal(replayed.Steps.Select(step => step.Move), kept.Moves);
                Assert.Equal(replayed.EndReason, kept.EndReason);
                Assert.Equal(replayed.Steps.Count(step => step.WasMistake), kept.Mistakes);
                Assert.Equal(replayed.Steps[^1].WasMistake, kept.LastMoveWasMistake);
                Assert.Equal(policy.Guesses, kept.Guesses);
                Assert.Equal(LotProgress.Cleared(level.Built.Level, replayed.Attempt.Current), kept.Cleared);
            }

            Assert.Contains(planner1.Runs.Take(30), run => run.Mistakes > 0 && !run.LastMoveWasMistake);
        }

        [Fact]
        public void A_fail_is_late_from_700_per_mille_cleared()
        {
            SimRun fail = Simulated.Value.Simulated.SelectMany(level => level.Profiles).SelectMany(profile => profile.Runs).First(run => run.Failed);

            Assert.True((fail with { Cleared = new PerMille(700) }).IsLateFail);
            Assert.False((fail with { Cleared = new PerMille(699) }).IsLateFail);
            Assert.False((fail with { EndReason = BotEndReason.Won, Cleared = new PerMille(1000) }).IsLateFail);
        }

        [Fact]
        public void An_expert_that_loses_on_its_par_path_is_an_engine_fault()
        {
            // The free-cars board with a par path that parks three colours and then a fourth car: the bays fill.
            LotLevel level = Jam.Boards.Load(Jam.Boards.FreeCars(
                4, Jam.Boards.Blue, Jam.Boards.Red, Jam.Boards.Yellow, Jam.Boards.Blue, Jam.Boards.Red, Jam.Boards.Yellow,
                Jam.Boards.Blue, Jam.Boards.Red, Jam.Boards.Yellow));
            var losing = new BuiltLevel(
                level, default, ReadOnlyArray.CopyOf(new[] { 0, 1, 2, 3 }.Select(piece => new Exit(new PieceId((byte)piece)))), Option.None<Search.DeductionGrade.Graded>());

            Assert.Throws<InvalidOperationException>(() => LotSim.Play(CampaignLevels.Pack, new LevelIndex(0), losing, LotBots.Expert, withFailCauses: false));
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
            Assert.Equal(Text(fails.Count(run => run.LastMoveWasMistake && run.IsNearMiss)), slotsFull[12]);
            Assert.Equal(Text(fails.Count(run => run.LastMoveWasMistake)), slotsFull[14]);
            Assert.Equal(Text(fails.Sum(run => run.Guesses.Or(new GuessTally(0, 0)).Right)), slotsFull[15]);
            Assert.Equal(Text(fails.Sum(run => run.Guesses.Or(new GuessTally(0, 0)).Wrong)), slotsFull[16]);
            Assert.True(fails.Sum(run => run.Guesses.Or(new GuessTally(0, 0)).Wrong) > 0, "The D3 level must show wrong guesses.");
        }

        [Fact]
        public void The_fails_report_counts_each_profiles_fails_by_near_miss_tenth_and_planner3s_cause()
        {
            SimPack pack = Simulated.Value;
            string[] lines = LotSimCsv.WriteFails(pack).Split('\n');
            List<string[]> rows = lines.Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')).ToList();

            Assert.Equal(LotSimCsv.FailsHeader, lines[0]);
            Assert.All(rows, row => Assert.Equal(8, row.Length));
            Assert.All(rows, row => Assert.InRange(Number(row[4]), 0, 9));
            Assert.All(rows, row => Assert.True(Number(row[7]) > 0));
            Assert.All(rows, row => Assert.Equal(row[2] == "Planner3", row[5].Length > 0));
            string[] nearMisses = { "Yes", "No", "SpareUnavailable", "Unknown" };
            string[] causes = { string.Empty, "Capacity", "Deduction", "Unknown" };
            foreach (SimSlot.Simulated level in pack.Simulated)
            {
                foreach (SimProfileRuns profile in level.Profiles)
                {
                    List<string[]> own = rows.Where(row => row[0] == level.Id.Value && row[2] == profile.Profile.Name).ToList();
                    var keys = own.Select(row => (Array.IndexOf(nearMisses, row[3]), Number(row[4]), Array.IndexOf(causes, row[5]), row[6].Length == 0 ? -1 : Number(row[6]))).ToList();

                    Assert.Equal(profile.Fails, own.Sum(row => Number(row[7])));
                    Assert.Equal(profile.LateFails, own.Where(row => Number(row[4]) >= 7).Sum(row => Number(row[7])));
                    Assert.Equal(profile.NearMisses, own.Where(row => row[3] == "Yes").Sum(row => Number(row[7])));
                    for (int tenth = 0; tenth < 10; tenth++)
                    {
                        Assert.Equal(
                            profile.Runs.Count(run => run.Failed && run.Cleared.Value / 100 == tenth),
                            own.Where(row => Number(row[4]) == tenth).Sum(row => Number(row[7])));
                    }
                    Assert.Equal(keys.OrderBy(key => key), keys);
                    Assert.Equal(keys.Count, keys.Distinct().Count());
                }
            }

            Assert.Contains(rows, row => row[2] == "Planner3" && row[6].Length > 0);
        }

        [Fact]
        public void A_fail_is_counted_in_the_tenth_of_its_cleared_permille()
        {
            // Every calibration level has five bays, so its fails stop at cleared values that never test the edges of a
            // tenth; copies of one fail with chosen values do: 0-99 is tenth 0, 100 is tenth 1, 904 is tenth 9.
            SimPack pack = Simulated.Value;
            SimSlot.Simulated level = pack.Simulated.First();
            SimProfileRuns greedy = Profile(level, BotProfileId.Greedy);
            SimRun fail = greedy.Runs.First(run => run.Failed);
            SimRun[] runs = { fail with { Cleared = new PerMille(904) }, fail with { Cleared = new PerMille(99) }, fail with { Cleared = new PerMille(100) } };
            var chosen = new SimPack(pack.Pack, ReadOnlyArray.Of<SimSlot>(level with { Profiles = ReadOnlyArray.Of(greedy with { Runs = ReadOnlyArray.CopyOf(runs) }) }));

            List<string[]> rows = LotSimCsv.WriteFails(chosen).Split('\n').Skip(1).Where(line => line.Length > 0).Select(line => line.Split(',')).ToList();

            Assert.Equal(new[] { "0", "1", "9" }, rows.Select(row => row[4]));
            Assert.All(rows, row => Assert.Equal("1", row[7]));
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
            Assert.Equal((GameKind.Jam, PlayMode.Campaign, LotText.RulesVersion), (reread.Game, reread.Mode, reread.RulesVersion));
            Assert.Equal((pack.Pack, level.Id), (reread.Level.Pack, reread.Level.Level));
            Assert.All(reread.Actions, action => Assert.Equal(new ElapsedMs(0), action.SincePrevious));
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

        [Fact]
        public void Sim_writes_its_reports_and_planner3s_first_fail_of_each_level_as_utf8_without_a_mark()
        {
            SimPack pack = Simulated.Value;
            string directory = Path.Combine(Path.GetTempPath(), "puzzle-sim-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                LotSimFiles.Write(pack, directory);

                var expected = new List<string> { "campaign-jam.sim-fails.csv", "campaign-jam.sim.csv" };
                foreach (SimSlot.Simulated level in pack.Simulated)
                {
                    if (Profile(level, BotProfileId.Planner3).Runs.FirstOrDefault(run => run.Failed) is SimRun failed)
                    {
                        expected.Add(level.Id.Value + ".pc-lot");
                        expected.Add(level.Id.Value + ".planner3-" + failed.Run.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".pc-record.json");
                        Assert.Equal(
                            Encoding.UTF8.GetBytes(RecordJson.Write(LotSimFiles.RecordOf(pack.Pack, level, failed), ExitJson.Instance)),
                            File.ReadAllBytes(Path.Combine(directory, LotSimFiles.RecordFileName(level.Id, failed.Run))));
                        Assert.Equal(Encoding.UTF8.GetBytes(LotText.Write(level.Built.Level)), File.ReadAllBytes(Path.Combine(directory, level.Id.Value + ".pc-lot")));
                    }
                }

                Assert.Equal(
                    expected.OrderBy(name => name, StringComparer.Ordinal),
                    Directory.GetFiles(directory).Select(path => Path.GetFileName(path)).OrderBy(name => name, StringComparer.Ordinal));
                Assert.Equal(Encoding.UTF8.GetBytes(LotSimCsv.Write(pack)), File.ReadAllBytes(Path.Combine(directory, "campaign-jam.sim.csv")));
                Assert.Equal(Encoding.UTF8.GetBytes(LotSimCsv.WriteFails(pack)), File.ReadAllBytes(Path.Combine(directory, "campaign-jam.sim-fails.csv")));
                Assert.True(expected.Count > 2, "Planner3 must fail somewhere in the sample.");
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private static int Number(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        private static SimProfileRuns Profile(SimSlot.Simulated level, BotProfileId id) =>
            level.Profiles.Single(profile => profile.Profile.Id == id);

        private static string Text(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

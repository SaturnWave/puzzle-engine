using System;
using System.Collections.Generic;
using System.IO;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Tests.Support;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>Golden replays, the record format, the validator's mode checks and the rewind property (shared engine §4).</summary>
    public sealed class JamReplayTests
    {
        private static readonly LevelRef SixBySixRef = new(
            PackId.Parse("golden-jam").Or(default),
            LevelId.Parse("six-by-six").Or(default),
            LevelFingerprint.Parse(LotTextTests.SixBySixFingerprint).Or(default));

        [Fact]
        public void The_hand_written_winning_record_replays_to_valid_won()
        {
            ReplayReport report = ReplayGolden("six-by-six-win.pc-record.json");

            Assert.Equal(new ReplayVerdict.Valid(new Outcome.Won(), new MoveCount(9), SpareUsed: false), report.Verdict);
            Assert.Equal(15, report.Events.Count); // 9 exits, 2 covers lifted, 3 sets, 1 win
            Assert.Equal(new PuzzleEvent.Won(), report.Events[report.Events.Count - 1]);
        }

        [Fact]
        public void The_hand_written_rescue_record_fails_rewinds_two_moves_and_wins()
        {
            ReplayReport report = ReplayGolden("six-by-six-rescue.pc-record.json");

            Assert.Equal(new ReplayVerdict.Valid(new Outcome.Won(), new MoveCount(9), SpareUsed: false), report.Verdict);
            Assert.Contains(new PuzzleEvent.Failed(FailKind.SlotsFull), report.Events);
            Assert.Contains(new PuzzleEvent.Rewound(new MoveCount(2)), report.Events);
        }

        [Fact]
        public void The_win_and_rescue_records_end_in_the_same_won_state()
        {
            Assert.Equal(ReplayGolden("six-by-six-win.pc-record.json").Final, ReplayGolden("six-by-six-rescue.pc-record.json").Final);
        }

        [Fact]
        public void A_rescue_rewinding_one_move_past_the_last_reveal_is_refused()
        {
            // Piece 3 lifts its cover on move 2 and the bays fill on move 4, so the rescue's rewind of
            // two moves is the longest the fence allows.
            var actions = new List<RecordAction<Exit>>(ReadGolden("six-by-six-rescue.pc-record.json").Actions);
            Assert.Equal(new MoveCount(2), Assert.IsType<RecordAction<Exit>.Rewind>(actions[4]).Moves);
            actions[4] = RewindOf(3);

            Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(4), InvalidReason.RewindOutOfRange), RunCampaign(actions).Verdict);
        }

        [Fact]
        public void The_rescue_record_from_before_the_fence_is_refused_at_its_rewind()
        {
            // Its failing move lifts piece 3's cover, so the fence leaves no rewind at all.
            ReplayReport report = ReplayGolden("six-by-six-rewind-across-reveal.pc-record.json");

            Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(4), InvalidReason.RewindOutOfRange), report.Verdict);
        }

        [Fact]
        public void A_record_written_and_read_back_is_unchanged_and_canonical()
        {
            ActionRecord<Exit> record = ReadGolden("six-by-six-win.pc-record.json");

            string written = RecordJson.Write(record, ExitJson.Instance);
            var reread = Assert.IsType<RecordReadResult<Exit>.Read>(RecordJson.Read(written, ExitJson.Instance)).Record;

            Assert.Equal(written, RecordJson.Write(reread, ExitJson.Instance));
            Assert.StartsWith("{\"actions\":[{\"piece\":0,\"sincePreviousMs\":2100,\"t\":\"Move\"}", written, StringComparison.Ordinal);
        }

        [Fact]
        public void Unknown_fields_and_a_foreign_game_are_rejected()
        {
            string golden = File.ReadAllText(GoldenPath("six-by-six-win.pc-record.json"));

            Assert.IsType<RecordReadResult<Exit>.Invalid>(RecordJson.Read(golden.Replace("\"rulesVersion\": 1,", "\"rulesVersion\": 1, \"cheat\": true,", StringComparison.Ordinal), ExitJson.Instance));
            Assert.IsType<RecordReadResult<Exit>.Invalid>(RecordJson.Read(golden.Replace("\"Jam\"", "\"Sort\"", StringComparison.Ordinal), ExitJson.Instance));
            Assert.IsType<RecordReadResult<Exit>.UnsupportedVersion>(RecordJson.Read(golden.Replace("\"formatVersion\": 1", "\"formatVersion\": 2", StringComparison.Ordinal), ExitJson.Instance));
        }

        [Fact]
        public void An_illegal_move_injected_into_a_winning_line_is_invalid_at_that_index()
        {
            var actions = new List<RecordAction<Exit>>(ReadGolden("six-by-six-win.pc-record.json").Actions);
            actions.Insert(3, MoveOf(6)); // R^ is still behind Y> (piece 2) at this point

            ReplayReport report = RunCampaign(actions);

            Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(3), InvalidReason.IllegalMove), report.Verdict);
        }

        [Theory]
        [InlineData(PlayMode.DailyProof)]
        [InlineData(PlayMode.Casebook)]
        [InlineData(PlayMode.WeeklyTwist)]
        [InlineData(PlayMode.Competitive)]
        [InlineData(PlayMode.Siege)]
        public void Boosters_outside_campaign_are_refused_before_anything_else(PlayMode mode)
        {
            foreach (RecordAction<Exit> booster in new RecordAction<Exit>[] { Spare(), RewindOf(1), Insight(3) })
            {
                ReplayReport report = Replay.Run(Rules(), Rules().Start, mode, ReadOnlyArray.Of(MoveOf(0), booster));

                Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(1), InvalidReason.BoosterInCompetitiveMode), report.Verdict);
            }
        }

        [Fact]
        public void A_spare_or_rewind_while_still_in_play_is_refused()
        {
            Assert.Equal(InvalidReason.SpareOutsideFail, Reason(RunCampaign(MoveOf(0), Spare())));
            Assert.Equal(InvalidReason.RewindOutsideFail, Reason(RunCampaign(MoveOf(0), RewindOf(1))));
        }

        [Fact]
        public void A_second_spare_in_one_attempt_is_refused()
        {
            // Four free cars fill four bays; the spare makes five, then two more misplaced cars fill them again.
            var rules = new JamRules(Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow)));

            ReplayReport report = Replay.Run(rules, rules.Start, PlayMode.Campaign,
                ReadOnlyArray.Of(MoveOf(0), MoveOf(1), MoveOf(2), MoveOf(4), Spare(), MoveOf(5), Spare()));

            Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(6), InvalidReason.SecondSpare), report.Verdict);
        }

        [Fact]
        public void A_rewind_outside_one_to_five_or_beyond_the_line_is_out_of_range()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow)));
            ReadOnlyArray<RecordAction<Exit>> fail = ReadOnlyArray.Of(MoveOf(0), MoveOf(1), MoveOf(2), MoveOf(4));

            foreach (int moves in new[] { 0, 5, 6 })
            {
                var actions = new List<RecordAction<Exit>>(fail) { RewindOf(moves) };
                ReplayReport report = Replay.Run(rules, rules.Start, PlayMode.Campaign, ReadOnlyArray.CopyOf(actions));

                Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(4), InvalidReason.RewindOutOfRange), report.Verdict);
            }
        }

        [Fact]
        public void A_second_insight_and_any_action_after_the_win_are_refused()
        {
            Assert.Equal(InvalidReason.InsightLimit, Reason(RunCampaign(Insight(3), Insight(7))));

            var afterWin = new List<RecordAction<Exit>>(ReadGolden("six-by-six-win.pc-record.json").Actions) { MoveOf(0) };
            Assert.Equal(InvalidReason.ActionAfterEnd, Reason(RunCampaign(afterWin)));
            Assert.Equal(InvalidReason.ActionAfterEnd, Reason(RunCampaign(MoveOf(0), new RecordAction<Exit>.Concede(Ms()), MoveOf(1))));
        }

        [Fact]
        public void Every_kind_of_action_after_a_win_or_a_concede_is_refused()
        {
            var won = new List<RecordAction<Exit>>(ReadGolden("six-by-six-win.pc-record.json").Actions);
            var conceded = new List<RecordAction<Exit>> { MoveOf(0), new RecordAction<Exit>.Concede(Ms()) };
            var lateActions = new RecordAction<Exit>[] { MoveOf(1), Spare(), RewindOf(1), Insight(3), new RecordAction<Exit>.Concede(Ms()) };

            foreach (List<RecordAction<Exit>> ended in new[] { won, conceded })
            {
                foreach (RecordAction<Exit> late in lateActions)
                {
                    ReplayReport report = RunCampaign(new List<RecordAction<Exit>>(ended) { late });

                    Assert.Equal(new ReplayVerdict.Invalid(new ActionIndex(ended.Count), InvalidReason.ActionAfterEnd), report.Verdict);
                }
            }
        }

        [Fact]
        public void A_spare_taken_before_a_rewind_stays_taken()
        {
            var rules = new JamRules(Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow)));
            var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, rules.Start, PlayMode.Campaign);
            foreach (int piece in new[] { 0, 1, 2, 4 })
            {
                attempt.Move(new Exit(new PieceId((byte)piece)));
            }

            attempt.TakeSpare();
            attempt.Move(new Exit(new PieceId(5)));
            Assert.IsType<Outcome.Failed>(attempt.Outcome);

            Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(4)));

            Assert.Equal(5, attempt.Current.Capacity.Value);
            Assert.True(attempt.SpareUsed);
        }

        [Fact]
        public void Rewind_restores_the_exact_earlier_state_and_the_seen_set_only_grows()
        {
            // Covered Lot's fence keeps every rewind at or after the last reveal, so no lifted cover comes back.
            LotLevel level = Load(SixBySix());
            RewindCases cases = RewindRandomLines(level, new JamRules(level));

            Assert.True(cases.Rewinds > 0, "The property never rewound.");
            Assert.Equal(0, cases.CoversBroughtBack);
        }

        [Fact]
        public void Without_a_fence_a_rewind_that_brings_back_a_lifted_cover_keeps_its_colour_seen()
        {
            // The shared contract keeps RewindFence.None, where a rewind may undo a reveal; the SeenSet must still only grow.
            LotLevel level = Load(SixBySix());
            RewindCases cases = RewindRandomLines(level, new UnfencedJamRules(level));

            Assert.True(cases.CoversBroughtBack > 0, "No rewind brought back a lifted cover, so that case was never checked.");
        }

        /// <summary>
        /// Plays seeded random legal lines on the level; after each fail, rewinds by a random allowed length and
        /// checks that the exact earlier state returns and that no seen cell is lost.
        /// </summary>
        private static RewindCases RewindRandomLines(LotLevel level, IPuzzleRules<LotState, Exit, ExitRejection> rules)
        {
            var cases = new RewindCases();
            for (int run = 0; run < 200; run++)
            {
                Pcg32 random = Pcg32.Seeded((ulong)run, 7UL);
                var attempt = new Attempt<LotState, Exit, ExitRejection>(rules, LotState.Start(level), PlayMode.Campaign);
                var keys = new List<ExactKey> { attempt.Current.Exact };
                while (attempt.Outcome is Outcome.InPlay)
                {
                    var moves = new List<Exit>();
                    rules.AppendLegalMoves(attempt.Current, moves);
                    attempt.Move(moves[random.NextIndex(moves.Count)]);
                    keys.Add(attempt.Current.Exact);
                }

                if (!(attempt.Outcome is Outcome.Failed))
                {
                    continue;
                }

                int limit = attempt.RewindLimit.Value;
                if (limit == 0)
                {
                    continue;
                }

                LotState failed = attempt.Current;
                ulong seenBefore = attempt.Seen.Mask;
                int back = 1 + random.NextIndex(limit);
                Assert.IsType<StepResult<ExitRejection>.Done>(attempt.Rewind(new MoveCount(back)));
                cases.Rewinds++;
                cases.CoversBroughtBack += CoveredCarsBackInTheLot(level, failed, attempt.Current);

                Assert.Equal(keys[keys.Count - 1 - back], attempt.Current.Exact);
                Assert.Equal(seenBefore, attempt.Seen.Mask & seenBefore);
            }

            return cases;
        }

        /// <summary>Covered cars that had left the lot in <paramref name="before"/> but are in it again in <paramref name="after"/>.</summary>
        private static int CoveredCarsBackInTheLot(LotLevel level, LotState before, LotState after)
        {
            int back = 0;
            foreach (Vehicle vehicle in level.Vehicles)
            {
                bool isBack = vehicle.StartsCovered && before.HasExited(vehicle.Id) && !after.HasExited(vehicle.Id);
                back += isBack ? 1 : 0;
            }

            return back;
        }

        private static JamRules Rules() => new(Load(SixBySix()));

        private static ReplayReport ReplayGolden(string file)
        {
            ActionRecord<Exit> record = ReadGolden(file);
            Assert.Equal(SixBySixRef, record.Level);
            JamRules rules = Rules();
            return Replay.Run(rules, rules.Start, record.Mode, record.Actions);
        }

        private static ActionRecord<Exit> ReadGolden(string file) =>
            Assert.IsType<RecordReadResult<Exit>.Read>(RecordJson.Read(File.ReadAllText(GoldenPath(file)), ExitJson.Instance)).Record;

        private static string GoldenPath(string file) => RepositoryLayout.FullPath("packs/jam/golden/" + file);

        private static ReplayReport RunCampaign(params RecordAction<Exit>[] actions) => RunCampaign((IEnumerable<RecordAction<Exit>>)actions);

        private static ReplayReport RunCampaign(IEnumerable<RecordAction<Exit>> actions) =>
            Replay.Run(Rules(), Rules().Start, PlayMode.Campaign, ReadOnlyArray.CopyOf(actions));

        private static InvalidReason Reason(ReplayReport report) => Assert.IsType<ReplayVerdict.Invalid>(report.Verdict).Reason;

        private static ElapsedMs Ms() => new(500);

        private static RecordAction<Exit> MoveOf(int piece) => new RecordAction<Exit>.Move(new Exit(new PieceId((byte)piece)), Ms());

        private static RecordAction<Exit> RewindOf(int moves) => new RecordAction<Exit>.Rewind(new MoveCount(moves), Ms());

        private static RecordAction<Exit> Spare() => new RecordAction<Exit>.TakeSpare(Ms());

        private static RecordAction<Exit> Insight(int cell) => new RecordAction<Exit>.UseInsight(new HiddenCell((byte)cell), Ms());

        /// <summary>How often the rewind property met each case, so it cannot pass without exercising them.</summary>
        private sealed class RewindCases
        {
            public int Rewinds { get; set; }

            /// <summary>Rewinds that put a car whose cover had lifted back into the lot (possible only without a fence).</summary>
            public int CoversBroughtBack { get; set; }
        }
    }
}

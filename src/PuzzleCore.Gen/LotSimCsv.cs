using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PuzzleCore.Bot;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Play;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// sim's two reports (Q81, Q84), written like the grading report: a header line, then long-form rows of contract
    /// names and invariant integers that never need quoting, each ending with '\n'.
    /// <para>
    /// {packId}.sim.csv has one row per simulated level, profile and end reason, every reason listed whether any run
    /// ended that way or not: the runs; on the fail reasons, the near-miss counts and the Yes per mille of those fails
    /// (empty when there were none), and the fails whose last move was a mistake with a Yes near miss; the runs with a
    /// mistake and those whose last move was one; and the guesses that turned out right and wrong (empty for a bot that
    /// never guesses). The near-miss columns are empty on the other reasons.
    /// </para>
    /// <para>
    /// {packId}.sim-fails.csv counts the failed runs of each level and profile by near miss, by tenth of cleared_permille
    /// (0 for 0-99, up to 9 for 900-999) and, for Planner3 when sim worked causes out, by fail cause and losing_move_back
    /// (empty otherwise, and empty back for an Unknown cause). Only counts above zero have a row.
    /// </para>
    /// </summary>
    public static class LotSimCsv
    {
        public const string Header =
            "level_id,slot,kind,rung,profile,bot_end_reason,runs,near_miss_yes,near_miss_no,near_miss_spare_unavailable,near_miss_unknown,"
            + "near_miss_yes_permille,last_move_mistake_near_miss_yes,runs_with_mistake,last_move_mistake,guesses_right,guesses_wrong";

        public const string FailsHeader = "level_id,slot,profile,near_miss,cleared_tenth,fail_cause,losing_move_back,fails";

        // Every reason in the closed set, in its fixed order, whether a run ended that way or not (Q81).
        private static readonly BotEndReason[] EndReasons = Enum.GetValues<BotEndReason>();

        public static string Write(SimPack pack)
        {
            var csv = new StringBuilder();
            csv.Append(Header).Append('\n');
            foreach (SimSlot.Simulated level in pack.Simulated)
            {
                foreach (SimProfileRuns profile in level.Profiles)
                {
                    foreach (BotEndReason reason in EndReasons)
                    {
                        csv.Append(string.Join(",", Fields(level, profile, reason))).Append('\n');
                    }
                }
            }

            return csv.ToString();
        }

        public static string WriteFails(SimPack pack)
        {
            var csv = new StringBuilder();
            csv.Append(FailsHeader).Append('\n');
            foreach (SimSlot.Simulated level in pack.Simulated)
            {
                foreach (SimProfileRuns profile in level.Profiles)
                {
                    IEnumerable<IGrouping<FailKey, SimRun>> groups = profile.Runs
                        .Where(run => run.Failed)
                        .GroupBy(KeyOf)
                        .OrderBy(group => group.Key.NearMissRank)
                        .ThenBy(group => group.Key.ClearedTenth)
                        .ThenBy(group => group.Key.CauseRank)
                        .ThenBy(group => group.Key.LosingMoveBack);
                    foreach (IGrouping<FailKey, SimRun> group in groups)
                    {
                        csv.Append(string.Join(",", FailFields(level, profile, group.Key, group.Count()))).Append('\n');
                    }
                }
            }

            return csv.ToString();
        }

        private static string[] Fields(SimSlot.Simulated level, SimProfileRuns profile, BotEndReason reason)
        {
            List<SimRun> runs = profile.Runs.Where(run => run.EndReason == reason).ToList();
            bool isFail = LotSim.IsFail(reason);
            bool guesses = profile.Runs.Count > 0 && profile.Runs[0].Guesses.HasValue;
            return new[]
            {
                level.Id.Value,
                Number(level.Slot.Value),
                ContractNames.Of(level.Built.Level.IsCoveredLevel ? LevelKind.Think : LevelKind.Plan),
                level.Built.Deduction.TryGet(out DeductionGrade.Graded? grade) ? ContractNames.Of(grade.Rung) : string.Empty,
                profile.Profile.Name,
                BotNames.Of(reason),
                Number(runs.Count),
                isFail ? Number(runs.Count(run => Is<NearMiss.Yes>(run))) : string.Empty,
                isFail ? Number(runs.Count(run => Is<NearMiss.No>(run))) : string.Empty,
                isFail ? Number(runs.Count(run => Is<NearMiss.SpareUnavailable>(run))) : string.Empty,
                isFail ? Number(runs.Count(run => Is<NearMiss.Unknown>(run))) : string.Empty,
                isFail && runs.Count > 0 ? Number(PerMille.Of(runs.Count(run => Is<NearMiss.Yes>(run)), runs.Count).Value) : string.Empty,
                isFail ? Number(runs.Count(run => run.LastMoveWasMistake && Is<NearMiss.Yes>(run))) : string.Empty,
                Number(runs.Count(run => run.Mistakes > 0)),
                Number(runs.Count(run => run.LastMoveWasMistake)),
                guesses ? Number(runs.Sum(run => Tally(run).Right)) : string.Empty,
                guesses ? Number(runs.Sum(run => Tally(run).Wrong)) : string.Empty,
            };
        }

        private static string[] FailFields(SimSlot.Simulated level, SimProfileRuns profile, FailKey key, int fails) => new[]
        {
            level.Id.Value,
            Number(level.Slot.Value),
            profile.Profile.Name,
            key.NearMissName,
            Number(key.ClearedTenth),
            key.Cause,
            key.LosingMoveBack < 0 ? string.Empty : Number(key.LosingMoveBack),
            Number(fails),
        };

        private static FailKey KeyOf(SimRun run)
        {
            NearMiss nearMiss = run.NearMiss.TryGet(out NearMiss? found) ? found : throw new InvalidOperationException("A failed run always has a near miss.");
            (string cause, int causeRank, int back) = run.Cause.TryGet(out FailCause? failCause) ? CauseOf(failCause) : (string.Empty, 0, -1);
            return new FailKey(SimNames.Of(nearMiss), SimNames.RankOf(nearMiss), run.Cleared.Value / 100, cause, causeRank, back);
        }

        private static (string Name, int Rank, int LosingMoveBack) CauseOf(FailCause cause) => cause switch
        {
            FailCause.Capacity capacity => (SimNames.Of(cause), 1, capacity.LosingMoveBack.Value),
            FailCause.Deduction deduction => (SimNames.Of(cause), 2, deduction.LosingMoveBack.Value),
            FailCause.Unknown => (SimNames.Of(cause), 3, -1),
            _ => throw new ArgumentOutOfRangeException(nameof(cause), "Unknown fail cause."),
        };

        private static bool Is<TNearMiss>(SimRun run) where TNearMiss : NearMiss => run.NearMiss.TryGet(out NearMiss? found) && found is TNearMiss;

        private static GuessTally Tally(SimRun run) => run.Guesses.TryGet(out GuessTally? tally) ? tally : throw new InvalidOperationException("Checked above.");

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary>One row of the fails report; LosingMoveBack -1 when there is none.</summary>
        private sealed record FailKey(string NearMissName, int NearMissRank, int ClearedTenth, string Cause, int CauseRank, int LosingMoveBack);
    }

    /// <summary>The fixed names sim writes for near misses and fail causes, and their order in the reports.</summary>
    public static class SimNames
    {
        public static string Of(NearMiss nearMiss) => nearMiss switch
        {
            NearMiss.Yes => "Yes",
            NearMiss.No => "No",
            NearMiss.SpareUnavailable => "SpareUnavailable",
            NearMiss.Unknown => "Unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(nearMiss), "Unknown near miss."),
        };

        public static int RankOf(NearMiss nearMiss) => nearMiss switch
        {
            NearMiss.Yes => 0,
            NearMiss.No => 1,
            NearMiss.SpareUnavailable => 2,
            NearMiss.Unknown => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(nearMiss), "Unknown near miss."),
        };

        public static string Of(FailCause cause) => cause switch
        {
            FailCause.Capacity => "Capacity",
            FailCause.Deduction => "Deduction",
            FailCause.Unknown => "Unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(cause), "Unknown fail cause."),
        };
    }
}

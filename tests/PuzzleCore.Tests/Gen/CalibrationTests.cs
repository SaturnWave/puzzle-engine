using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PuzzleCore.Bot;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Gen;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using Xunit;

namespace PuzzleCore.Tests.Gen
{
    /// <summary>
    /// The bots' calibration, held in the suite (Q82, TC25): campaign-jam slots 0-99 played by the four bots, 200 runs each.
    /// The pack must be the one calibrated, every run must keep the invariants, the run digest must be the one recorded and
    /// the nine bands must hold. The bands and pins were recorded with the user's approval on the first calibration
    /// (docs/grilling/p5: calibration-table.md, bands.md, stop-rule-result.md). Planner3's fail causes stay outside.
    /// </summary>
    public sealed class CalibrationTests
    {
        /// <summary>TC25: the SHA-256 of the pack's 100 lines, "{slot:0000} {fingerprint}" or "{slot:0000} REJECTED {reason}".</summary>
        private const string PackPin = "59c6cb6499eb1d2b3f7527e4399450d0da84e9b2bfbebe59e82b4780b114dabe";

        /// <summary>Q82: the SHA-256 of every run's line (<see cref="RunLine"/>), in slot, profile and run order.</summary>
        private const string RunsDigest = "439d214020d54f262fc7d63d5e7b5f3cf4d16271158959cacf57103560b75312";

        private static readonly PackId Pack = PackId.Parse("campaign-jam").Or(default);

        /// <summary>
        /// Q82's nine bands, per mille, recorded value and bounds. Each half-width is 3.29 × √2 standard errors over the
        /// fixed pack (bands.py.txt): a redraw of the Bot streams trips any one band 0.1% of the time, and all nine under 1%.
        /// </summary>
        private static readonly Band[] Bands =
        {
            new("Greedy Won, all levels", 518, 504, 532, pack => WonPerMille(pack, BotProfileId.Greedy, AllLevels)),
            new("Planner1 Won, all levels", 420, 406, 434, pack => WonPerMille(pack, BotProfileId.Planner1, AllLevels)),
            new("Planner3 Won, all levels", 758, 745, 771, pack => WonPerMille(pack, BotProfileId.Planner3, AllLevels)),
            new("Greedy near-miss Yes of fails, all levels", 476, 453, 499, pack => FailShare(pack, BotProfileId.Greedy, AllLevels, run => run.IsNearMiss)),
            new("Planner1 near-miss Yes of fails, all levels", 610, 589, 631, pack => FailShare(pack, BotProfileId.Planner1, AllLevels, run => run.IsNearMiss)),
            new("Planner3 near-miss Yes of fails, all levels", 746, 718, 774, pack => FailShare(pack, BotProfileId.Planner3, AllLevels, run => run.IsNearMiss)),
            new("Planner3 Won, covered levels", 674, 639, 709, pack => WonPerMille(pack, BotProfileId.Planner3, CoveredLevels)),
            new("Planner3 near-miss Yes of fails, covered levels", 785, 729, 841, pack => FailShare(pack, BotProfileId.Planner3, CoveredLevels, run => run.IsNearMiss)),
            new("Planner3 late fails of fails, all levels", 265, 236, 294, pack => FailShare(pack, BotProfileId.Planner3, AllLevels, run => run.IsLateFail)),
        };

        [Fact]
        public void The_calibration_pack_plays_as_recorded()
        {
            SimPack sim = LotSim.Run(Pack, 100, Environment.ProcessorCount, withFailCauses: false);

            // The pack first: if the generator changed it, nothing below compares like with like (TC25). Stop and ask.
            string packPin = Sha256(PackLines(sim));
            Assert.True(packPin == PackPin, "The calibration pack changed (pin " + packPin + "): stop and ask before re-pinning (TC25).");

            // The invariants (Q82). sim already throws on the first two; the test says them again.
            List<SimRun> runs = sim.Simulated.SelectMany(level => level.Profiles).SelectMany(profile => profile.Runs).ToList();
            Assert.Equal(98 * 4 * LotSim.RunsPerProfile, runs.Count);
            Assert.All(runs, run => Assert.True(run.EndReason is BotEndReason.Won or BotEndReason.SlotsFull));
            Assert.All(runs, run => Assert.False(run.NearMiss.TryGet(out NearMiss? found) && found is NearMiss.Unknown));
            Assert.All(sim.Simulated.Select(level => Profile(level, BotProfileId.Expert)), expert => Assert.All(expert.Runs, run => Assert.Equal(BotEndReason.Won, run.EndReason)));
            AssertEvery97thRunReplays(sim);

            // The digest says whether anything moved; the bands say whether it matters.
            var measured = Bands.Select(band => (Band: band, Value: band.Measure(sim))).ToList();
            List<string> tripped = measured
                .Where(figure => figure.Value < figure.Band.Lower || figure.Value > figure.Band.Upper)
                .Select(figure => figure.Band.Name + " " + Number(figure.Value) + " outside " + Number(figure.Band.Lower) + "-" + Number(figure.Band.Upper))
                .ToList();
            string figures = string.Join("; ", measured.Select(figure => figure.Band.Name + " " + Number(figure.Value) + " (recorded " + Number(figure.Band.Recorded) + ")"));
            string digest = Sha256(RunLines(sim));
            Assert.True(
                tripped.Count == 0,
                "A band tripped: re-record the bands with the user's approval, and never settle it by changing levels before P10 (Q82). "
                + string.Join("; ", tripped) + ". Digest " + digest + ".");
            Assert.True(
                digest == RunsDigest,
                "The runs moved but every band held (digest " + digest + "): re-pin it as 'bands unchanged' with the user's approval (Q82). "
                + figures + ".");
        }

        /// <summary>One line per slot: its level's fingerprint, or REJECTED and the builder's reason (TC25).</summary>
        private static IEnumerable<string> PackLines(SimPack sim) => sim.Slots.Select(slot => slot switch
        {
            SimSlot.Simulated level => Number4(level.Slot.Value) + " " + LotText.Fingerprint(level.Built.Level),
            SimSlot.Rejected rejected => Number4(rejected.Slot.Value) + " REJECTED " + rejected.Reason.ToString(),
            _ => throw new InvalidOperationException("Unknown sim slot."),
        });

        private static IEnumerable<string> RunLines(SimPack sim) =>
            from level in sim.Simulated
            from profile in level.Profiles
            from run in profile.Runs
            select RunLine(level, profile, run);

        /// <summary>
        /// A run as the digest sees it: slot, profile, run, end reason, cleared_permille, near miss, mistakes, whether the
        /// last move was one, guesses right and wrong, and every move's PieceId.
        /// </summary>
        private static string RunLine(SimSlot.Simulated level, SimProfileRuns profile, SimRun run) => string.Join(" ",
            Number4(level.Slot.Value),
            profile.Profile.Name,
            Number(run.Run),
            BotNames.Of(run.EndReason),
            Number(run.Cleared.Value),
            run.NearMiss.TryGet(out NearMiss? nearMiss) ? SimNames.Of(nearMiss) : "-",
            Number(run.Mistakes),
            run.LastMoveWasMistake ? "1" : "0",
            run.Guesses.TryGet(out GuessTally? guesses) ? Number(guesses.Right) + "/" + Number(guesses.Wrong) : "-",
            string.Join(".", run.Moves.Select(move => Number(move.Piece.Value))));

        /// <summary>Every 97th run, as a record, replays through the shared validator to the same end and length.</summary>
        private static void AssertEvery97thRunReplays(SimPack sim)
        {
            int index = 0;
            foreach (SimSlot.Simulated level in sim.Simulated)
            {
                foreach (SimProfileRuns profile in level.Profiles)
                {
                    foreach (SimRun run in profile.Runs)
                    {
                        if (index++ % 97 != 0)
                        {
                            continue;
                        }

                        var rules = new JamRules(level.Built.Level);
                        ActionRecord<Exit> record = LotSimFiles.RecordOf(sim.Pack, level, run);
                        var valid = Assert.IsType<ReplayVerdict.Valid>(Replay.Run(rules, rules.Start, record.Mode, record.Actions).Verdict);
                        Assert.Equal(run.EndReason == BotEndReason.Won, valid.Outcome is Outcome.Won);
                        Assert.Equal(new MoveCount(run.Moves.Count), valid.Moves);
                    }
                }
            }
        }

        private static bool AllLevels(SimSlot.Simulated level) => true;

        private static bool CoveredLevels(SimSlot.Simulated level) => level.Built.Level.IsCoveredLevel;

        private static int WonPerMille(SimPack sim, BotProfileId id, Func<SimSlot.Simulated, bool> keep)
        {
            List<SimRun> runs = sim.Simulated.Where(keep).SelectMany(level => Profile(level, id).Runs).ToList();
            return runs.Count(run => run.EndReason == BotEndReason.Won) * PerMille.Whole / runs.Count;
        }

        private static int FailShare(SimPack sim, BotProfileId id, Func<SimSlot.Simulated, bool> keep, Func<SimRun, bool> hit)
        {
            List<SimRun> fails = sim.Simulated.Where(keep).SelectMany(level => Profile(level, id).Runs).Where(run => run.Failed).ToList();
            return fails.Count(hit) * PerMille.Whole / fails.Count;
        }

        private static SimProfileRuns Profile(SimSlot.Simulated level, BotProfileId id) => level.Profiles.Single(profile => profile.Profile.Id == id);

        private static string Sha256(IEnumerable<string> lines) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)))).ToLowerInvariant();

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Number4(int value) => value.ToString("0000", CultureInfo.InvariantCulture);

        private sealed record Band(string Name, int Recorded, int Lower, int Upper, Func<SimPack, int> Measure);
    }
}

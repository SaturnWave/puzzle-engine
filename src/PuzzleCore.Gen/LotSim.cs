using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using PuzzleCore.Bot;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Search;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// One bot run as sim keeps it (Q81): how it ended, how far it got, its near miss when it failed, its mistakes and
    /// guesses, Planner3's fail cause when sim works causes out, and the moves it played.
    /// </summary>
    public sealed record SimRun(
        int Run,
        BotEndReason EndReason,
        PerMille Cleared,
        Option<NearMiss> NearMiss,
        int Mistakes,
        bool LastMoveWasMistake,
        Option<GuessTally> Guesses,
        Option<FailCause> Cause,
        ReadOnlyArray<Exit> Moves)
    {
        public bool Failed => EndReason is BotEndReason.SlotsFull or BotEndReason.NoLegalMove;

        /// <summary>A late fail (Q81): a fail with at least seven tenths of the vehicles out, cleared_permille of 700 or more.</summary>
        public bool IsLateFail => Failed && Cleared.Value >= LotSim.LateFailFrom;

        /// <summary>A failed run the +1 spare would have won (the shared near miss).</summary>
        public bool IsNearMiss => NearMiss.TryGet(out Play.NearMiss? found) && found is Play.NearMiss.Yes;
    }

    /// <summary>A profile's runs on one level, in run order.</summary>
    public sealed record SimProfileRuns(BotProfile Profile, ReadOnlyArray<SimRun> Runs)
    {
        public int Count(BotEndReason reason) => Runs.Count(run => run.EndReason == reason);

        public int Fails => Runs.Count(run => run.Failed);

        public int NearMisses => Runs.Count(run => run.IsNearMiss);

        public int LateFails => Runs.Count(run => run.IsLateFail);

        /// <summary>Won per mille of the runs; for Greedy this is the level's GreedyWinPerMille (Q80).</summary>
        public PerMille WonPerMille => PerMille.Of(Count(BotEndReason.Won), Runs.Count);
    }

    /// <summary>One slot of a simulated pack: a built level with every profile's runs, or the builder's reason to reject it. Closed set.</summary>
    public abstract record SimSlot
    {
        private SimSlot()
        {
        }

        public abstract LevelIndex Slot { get; }

        public sealed record Simulated(LevelIndex Slot, LevelId Id, BuiltLevel Built, ReadOnlyArray<SimProfileRuns> Profiles) : SimSlot
        {
            public override LevelIndex Slot { get; } = Slot;
        }

        public sealed record Rejected(LevelIndex Slot, BuildFailure Reason) : SimSlot
        {
            public override LevelIndex Slot { get; } = Slot;
        }
    }

    /// <summary>A pack's slots, in the order sim was given them.</summary>
    public sealed record SimPack(PackId Pack, ReadOnlyArray<SimSlot> Slots)
    {
        public IEnumerable<SimSlot.Simulated> Simulated => Slots.OfType<SimSlot.Simulated>();
    }

    /// <summary>
    /// sim's driver (Q81, Q84, TC20, TC24): every built slot of a pack played by Covered Lot's four bots, 200 runs each,
    /// through the shared runner with each run's own Bot stream. Work is spread over (level, profile) pairs and gathered by
    /// index, so any number of workers gives the same result. Expert draws nothing, so it is played once and counted for
    /// every run (Q74). Anything other than a won or SlotsFull run, an Expert run that is not won, and an Unknown near miss
    /// are engine faults: they throw InvalidOperationException.
    /// </summary>
    public static class LotSim
    {
        public const int RunsPerProfile = 200;

        /// <summary>The cleared_permille from which a fail counts as late (Q81).</summary>
        public const int LateFailFrom = 700;

        /// <summary>The near-miss solve's node budget inside sim (TC24); the prototype's deepest solve took 161 nodes.</summary>
        public static NodeBudget NearMissBudget { get; } = new(20_000_000);

        /// <summary>The fail cause's budget per entry it solves; Planner3's causes are reported outside the band test.</summary>
        public static NodeBudget FailCauseBudget { get; } = new(20_000_000);

        /// <summary>Slots 0 to <paramref name="slotCount"/> - 1 of the pack, as the sim command plays them.</summary>
        public static SimPack Run(PackId pack, int slotCount, int workers, bool withFailCauses)
        {
            if (slotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount), slotCount, "A slot count is never negative.");
            }

            return Run(pack, Enumerable.Range(0, slotCount).Select(slot => new LevelIndex(slot)).ToList(), workers, withFailCauses);
        }

        /// <summary>
        /// The given slots of the pack, in the given order, on up to <paramref name="workers"/> threads. With
        /// <paramref name="withFailCauses"/>, Planner3's failed runs also get their fail cause.
        /// </summary>
        public static SimPack Run(PackId pack, IReadOnlyList<LevelIndex> slots, int workers, bool withFailCauses)
        {
            if (workers < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(workers), workers, "sim needs at least one worker.");
            }

            var ids = new LevelId[slots.Count];
            for (int index = 0; index < slots.Count; index++)
            {
                ids[index] = LevelId.For(pack, slots[index]).TryGet(out LevelId id)
                    ? id
                    : throw new ArgumentException("The pack id leaves no room for the level id of slot " + slots[index].ToString() + ".", nameof(pack));
            }

            var options = new ParallelOptions { MaxDegreeOfParallelism = workers };
            var builds = new BuildResult[slots.Count];
            InParallel(slots.Count, options, index => builds[index] = LotPackBuilder.Build(pack, slots[index]));

            List<int> built = Enumerable.Range(0, slots.Count).Where(index => builds[index] is BuildResult.Built).ToList();
            ReadOnlyArray<BotProfile> profiles = LotBots.Profiles;
            var runs = new SimProfileRuns[built.Count * profiles.Count];
            InParallel(runs.Length, options, index =>
            {
                int slot = built[index / profiles.Count];
                runs[index] = Play(pack, slots[slot], ((BuildResult.Built)builds[slot]).Level, profiles[index % profiles.Count], withFailCauses);
            });

            var simulated = new List<SimSlot>();
            int next = 0;
            for (int index = 0; index < slots.Count; index++)
            {
                switch (builds[index])
                {
                    case BuildResult.Built level:
                        simulated.Add(new SimSlot.Simulated(slots[index], ids[index], level.Level, ReadOnlyArray.CopyOf(runs.Skip(next * profiles.Count).Take(profiles.Count))));
                        next++;
                        break;
                    case BuildResult.Rejected rejected:
                        simulated.Add(new SimSlot.Rejected(slots[index], rejected.Reason));
                        break;
                    default:
                        throw new InvalidOperationException("BuildResult is a closed set.");
                }
            }

            return new SimPack(pack, ReadOnlyArray.CopyOf(simulated));
        }

        /// <summary>GreedyWinPerMille of one built level (Q80): Greedy's Won per mille over its 200 runs, as sim plays them.</summary>
        public static PerMille GreedyWinPerMille(PackId pack, LevelIndex slot, BuiltLevel built) =>
            Play(pack, slot, built, LotBots.Greedy, withFailCauses: false).WonPerMille;

        /// <summary>
        /// One profile's 200 runs on a built level, keyed by the pack and slot it was built for. With
        /// <paramref name="withFailCauses"/>, Planner3's failed runs also get their fail cause.
        /// </summary>
        public static SimProfileRuns Play(PackId pack, LevelIndex slot, BuiltLevel built, BotProfile profile, bool withFailCauses)
        {
            if (profile.Search is BotSearch.FullSolver)
            {
                SimRun once = RunOnce(pack, slot, built, profile, 0, withFailCause: false);
                if (once.EndReason != BotEndReason.Won)
                {
                    throw new InvalidOperationException("Expert lost slot " + slot.ToString() + " on its certified par path.");
                }

                return new SimProfileRuns(profile, ReadOnlyArray.CopyOf(Enumerable.Range(0, RunsPerProfile).Select(run => once with { Run = run })));
            }

            bool workCausesOut = withFailCauses && profile.Id == BotProfileId.Planner3;
            return new SimProfileRuns(
                profile,
                ReadOnlyArray.CopyOf(Enumerable.Range(0, RunsPerProfile).Select(run => RunOnce(pack, slot, built, profile, run, workCausesOut))));
        }

        private static SimRun RunOnce(PackId pack, LevelIndex slot, BuiltLevel built, BotProfile profile, int run, bool withFailCause)
        {
            LotLevel level = built.Level;
            LotBotPolicy policy = LotBots.PolicyFor(profile, built);
            BotRun<LotState, Exit, ExitRejection> played = BotRunner.Run(
                new JamRules(level), LotState.Start(level), profile, policy, BotStreams.For(pack, slot, profile, run), LotBots.MoveCapFor(level));
            if (played.EndReason is not (BotEndReason.Won or BotEndReason.SlotsFull))
            {
                throw new InvalidOperationException("A Jam bot run ended " + BotNames.Of(played.EndReason) + " on slot " + slot.ToString() + ".");
            }

            var solver = new LotPositionSolver(level);
            Option<NearMiss> nearMiss = played.NearMissWith(solver, NearMissBudget);
            if (nearMiss.TryGet(out NearMiss? found) && found is NearMiss.Unknown)
            {
                throw new InvalidOperationException("A near-miss solve ran out of its budget on slot " + slot.ToString() + ".");
            }

            Option<FailCause> cause = withFailCause && played.Attempt.Outcome is Outcome.Failed
                ? Option.Some(FailAnalysis.CauseOf(played.Attempt, solver, new LotHiddenMoveCheck(level), FailCauseBudget))
                : Option.None<FailCause>();
            int mistakes = played.Steps.Count(step => step.WasMistake);
            bool lastWasMistake = played.Steps.Count > 0 && played.Steps[played.Steps.Count - 1].WasMistake;
            return new SimRun(
                run,
                played.EndReason,
                LotProgress.Cleared(level, played.Attempt.Current),
                nearMiss,
                mistakes,
                lastWasMistake,
                policy.Guesses,
                cause,
                ReadOnlyArray.CopyOf(played.Steps.Select(step => step.Move)));
        }

        /// <summary>
        /// Runs the body for 0..count-1 on up to the options' workers. The first fault the workers report is rethrown as
        /// itself, so one worker and many fail with the same exception type.
        /// </summary>
        private static void InParallel(int count, ParallelOptions options, Action<int> body)
        {
            try
            {
                Parallel.For(0, count, options, body);
            }
            catch (AggregateException failure)
            {
                ExceptionDispatchInfo.Capture(failure.InnerExceptions[0]).Throw();
            }
        }
    }
}

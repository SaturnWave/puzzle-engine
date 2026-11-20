using System;
using System.Collections.Generic;
using System.Linq;
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
        public bool Failed => LotSim.IsFail(EndReason);

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

    /// <summary>The slots of a pack as the builder left them, before any bot plays them: the order sim was given them.</summary>
    public sealed record SimBuild(PackId Pack, ReadOnlyArray<LevelIndex> Slots, ReadOnlyArray<LevelId> Ids, ReadOnlyArray<BuildResult> Builds);

    /// <summary>How many threads sim may use: at least one.</summary>
    public readonly struct WorkerCount : IEquatable<WorkerCount>
    {
        public WorkerCount(int value)
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "sim needs at least one worker.");
            }

            Value = value;
        }

        /// <summary>One worker per processor of this machine.</summary>
        public static WorkerCount EveryProcessor => new(Environment.ProcessorCount);

        public int Value { get; }

        public bool Equals(WorkerCount other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is WorkerCount other && Equals(other);

        public override int GetHashCode() => Value;

        public static bool operator ==(WorkerCount left, WorkerCount right) => left.Equals(right);

        public static bool operator !=(WorkerCount left, WorkerCount right) => !left.Equals(right);
    }

    /// <summary>
    /// How sim plays: on how many workers, whether Planner3's fails also get their fail cause (outside the band test), and
    /// the near-miss solve's budget (TC24's constant unless a test lowers it).
    /// </summary>
    public sealed record SimSettings(WorkerCount Workers, bool WithFailCauses)
    {
        public NodeBudget NearMissBudget { get; init; } = LotSim.NearMissBudget;
    }

    /// <summary>
    /// sim's driver (Q81, Q84, TC20, TC24): the built slots of a pack played by Covered Lot's four bots, 200 runs each,
    /// through the shared runner with each run's own Bot stream. Work is spread over (level, profile) pairs and gathered by
    /// index, so any number of workers gives the same result. Expert draws nothing, so it is played once and counted for
    /// every run (Q74). Any end but Won or SlotsFull, an Expert run that is not won, and an Unknown near miss are engine
    /// faults: InvalidOperationException, naming the slot, profile and run. When several work items fault, the one with
    /// the lowest index is reported, with the number of others, so one worker and many report the same fault.
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

        /// <summary>Slots 0 to <paramref name="count"/> - 1, as the sim command plays them.</summary>
        public static IReadOnlyList<LevelIndex> FirstSlots(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "A slot count is never negative.");
            }

            return Enumerable.Range(0, count).Select(slot => new LevelIndex(slot)).ToList();
        }

        /// <summary>Builds the given slots of the pack and plays them: <see cref="Build"/>, then <see cref="Play"/>.</summary>
        public static SimPack Run(PackId pack, IReadOnlyList<LevelIndex> slots, SimSettings settings) =>
            Play(Build(pack, slots, settings.Workers), settings);

        /// <summary>The given slots of the pack, built in the given order. Refuses a pack id that leaves no room for a slot's level id.</summary>
        public static SimBuild Build(PackId pack, IReadOnlyList<LevelIndex> slots, WorkerCount workers)
        {
            var ids = new LevelId[slots.Count];
            for (int index = 0; index < slots.Count; index++)
            {
                ids[index] = LevelId.For(pack, slots[index]).TryGet(out LevelId id)
                    ? id
                    : throw new ArgumentException("The pack id leaves no room for the level id of slot " + slots[index].ToString() + ".", nameof(pack));
            }

            var builds = new BuildResult[slots.Count];
            InParallel(slots.Count, workers, index => builds[index] = LotPackBuilder.Build(pack, slots[index]));
            return new SimBuild(pack, ReadOnlyArray.CopyOf(slots), ReadOnlyArray.Of(ids), ReadOnlyArray.Of(builds));
        }

        /// <summary>Every built slot of the build played by the four profiles; a rejected slot keeps the builder's reason.</summary>
        public static SimPack Play(SimBuild build, SimSettings settings)
        {
            List<int> built = Enumerable.Range(0, build.Slots.Count).Where(index => build.Builds[index] is BuildResult.Built).ToList();
            ReadOnlyArray<BotProfile> profiles = LotBots.Profiles;
            var runs = new SimProfileRuns[built.Count * profiles.Count];
            InParallel(runs.Length, settings.Workers, index =>
            {
                int slot = built[index / profiles.Count];
                runs[index] = PlayProfile(build.Pack, build.Slots[slot], ((BuildResult.Built)build.Builds[slot]).Level, profiles[index % profiles.Count], settings);
            });

            var simulated = new List<SimSlot>();
            int next = 0;
            for (int index = 0; index < build.Slots.Count; index++)
            {
                switch (build.Builds[index])
                {
                    case BuildResult.Built level:
                        simulated.Add(new SimSlot.Simulated(build.Slots[index], build.Ids[index], level.Level, ReadOnlyArray.CopyOf(runs.Skip(next * profiles.Count).Take(profiles.Count))));
                        next++;
                        break;
                    case BuildResult.Rejected rejected:
                        simulated.Add(new SimSlot.Rejected(build.Slots[index], rejected.Reason));
                        break;
                    default:
                        throw new InvalidOperationException("BuildResult is a closed set.");
                }
            }

            return new SimPack(build.Pack, ReadOnlyArray.CopyOf(simulated));
        }

        /// <summary>GreedyWinPerMille of one built level (Q80): Greedy's Won per mille over its 200 runs, as sim plays them.</summary>
        public static PerMille GreedyWinPerMille(PackId pack, LevelIndex slot, BuiltLevel built) =>
            PlayProfile(pack, slot, built, LotBots.Greedy, new SimSettings(new WorkerCount(1), WithFailCauses: false)).WonPerMille;

        /// <summary>
        /// One profile's 200 runs on a built level, keyed by the pack and slot it was built for. With the settings'
        /// WithFailCauses, Planner3's failed runs also get their fail cause.
        /// </summary>
        public static SimProfileRuns PlayProfile(PackId pack, LevelIndex slot, BuiltLevel built, BotProfile profile, SimSettings settings)
        {
            if (profile.Search is BotSearch.FullSolver)
            {
                SimRun once = RunNamed(pack, slot, built, profile, 0, settings, withFailCause: false);
                if (once.EndReason != BotEndReason.Won)
                {
                    throw new InvalidOperationException("Slot " + slot.ToString() + ", Expert: it lost on its certified par path.");
                }

                return new SimProfileRuns(profile, ReadOnlyArray.CopyOf(Enumerable.Range(0, RunsPerProfile).Select(run => once with { Run = run })));
            }

            bool workCausesOut = settings.WithFailCauses && profile.Id == BotProfileId.Planner3;
            return new SimProfileRuns(
                profile,
                ReadOnlyArray.CopyOf(Enumerable.Range(0, RunsPerProfile).Select(run => RunNamed(pack, slot, built, profile, run, settings, workCausesOut))));
        }

        /// <summary>Whether a run that ended this way failed: SlotsFull or NoLegalMove.</summary>
        internal static bool IsFail(BotEndReason reason) => reason is BotEndReason.SlotsFull or BotEndReason.NoLegalMove;

        /// <summary>One run; an engine fault in it is reported with the slot, the profile and the run.</summary>
        private static SimRun RunNamed(PackId pack, LevelIndex slot, BuiltLevel built, BotProfile profile, int run, SimSettings settings, bool withFailCause)
        {
            try
            {
                return RunOnce(pack, slot, built, profile, run, settings, withFailCause);
            }
            catch (InvalidOperationException fault)
            {
                throw new InvalidOperationException(
                    "Slot " + slot.ToString() + ", " + profile.Name + ", run " + run.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + fault.Message,
                    fault);
            }
        }

        private static SimRun RunOnce(PackId pack, LevelIndex slot, BuiltLevel built, BotProfile profile, int run, SimSettings settings, bool withFailCause)
        {
            LotLevel level = built.Level;
            LotBotPolicy policy = LotBots.PolicyFor(profile, built);
            BotRun<LotState, Exit, ExitRejection> played = BotRunner.Run(
                new JamRules(level), LotState.Start(level), profile, policy, BotStreams.For(pack, slot, profile, run), LotBots.MoveCapFor(level));
            if (played.EndReason is not (BotEndReason.Won or BotEndReason.SlotsFull))
            {
                throw new InvalidOperationException("A Jam bot run ended " + BotNames.Of(played.EndReason) + ".");
            }

            var solver = new LotPositionSolver(level);
            Option<NearMiss> nearMiss = played.NearMissWith(solver, settings.NearMissBudget);
            if (nearMiss.TryGet(out NearMiss? found) && found is NearMiss.Unknown)
            {
                throw new InvalidOperationException("A near-miss solve ran out of its budget.");
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
        /// Runs the body for 0..count-1 on up to <paramref name="workers"/> threads. Each item's engine fault is kept by its
        /// index; afterwards the lowest index's fault is thrown, with how many others there were, so the report does not
        /// depend on the number of workers or on timing.
        /// </summary>
        private static void InParallel(int count, WorkerCount workers, Action<int> body)
        {
            var faults = new InvalidOperationException?[count];
            Parallel.For(0, count, new ParallelOptions { MaxDegreeOfParallelism = workers.Value }, index =>
            {
                try
                {
                    body(index);
                }
                catch (InvalidOperationException fault)
                {
                    faults[index] = fault;
                }
            });

            List<InvalidOperationException> raised = faults.OfType<InvalidOperationException>().ToList();
            if (raised.Count > 0)
            {
                string others = raised.Count == 1 ? string.Empty : " (and " + (raised.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " other faults)";
                throw new InvalidOperationException(raised[0].Message + others, raised[0]);
            }
        }
    }
}

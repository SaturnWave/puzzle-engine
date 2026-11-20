using System;
using System.Collections.Generic;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Search;

namespace PuzzleCore.Bot
{
    /// <summary>
    /// One game's way of choosing a bot's moves: how it reads the board, guesses and looks ahead (Covered Lot's live in
    /// PuzzleCore.Bot.Jam). It reads only what its profile may see. <see cref="BotRunner"/> owns everything else: the
    /// attempt, the mistakes and the guards.
    /// </summary>
    public interface IBotPolicy<TState, TMove>
        where TState : struct, IPuzzleState<TState>
        where TMove : struct, IEquatable<TMove>
    {
        /// <summary>Notes what the move just played revealed, from its events. It draws nothing.</summary>
        void Observe(ReadOnlyArray<PuzzleEvent> events);

        /// <summary>
        /// The move the bot means to play from <paramref name="state"/>: one of <paramref name="legal"/>, which holds at
        /// least one move, in the rules' order. Its draws come from <paramref name="random"/>, the run's Bot stream.
        /// </summary>
        TMove Intend(TState state, IReadOnlyList<TMove> legal, Pcg32 random);
    }

    /// <summary>One move of a bot run, and whether it was a mistake rather than the move the bot intended.</summary>
    public readonly struct BotStep<TMove> : IEquatable<BotStep<TMove>>
        where TMove : struct, IEquatable<TMove>
    {
        public BotStep(TMove move, bool wasMistake)
        {
            Move = move;
            WasMistake = wasMistake;
        }

        public TMove Move { get; }

        public bool WasMistake { get; }

        public bool Equals(BotStep<TMove> other) => Move.Equals(other.Move) && WasMistake == other.WasMistake;

        public override bool Equals(object? obj) => obj is BotStep<TMove> other && Equals(other);

        // Hash mixing wraps on purpose: a game's move may hash to any int (shared-design.md §3).
        public override int GetHashCode() => unchecked((Move.GetHashCode() * 2) + (WasMistake ? 1 : 0));

        public static bool operator ==(BotStep<TMove> left, BotStep<TMove> right) => left.Equals(right);

        public static bool operator !=(BotStep<TMove> left, BotStep<TMove> right) => !left.Equals(right);
    }

    /// <summary>A finished bot run: how it ended, the attempt it played, and each move it made.</summary>
    public sealed class BotRun<TState, TMove, TRejection>
        where TState : struct, IPuzzleState<TState>
        where TMove : struct, IEquatable<TMove>
        where TRejection : class
    {
        internal BotRun(BotEndReason endReason, Attempt<TState, TMove, TRejection> attempt, ReadOnlyArray<BotStep<TMove>> steps)
        {
            EndReason = endReason;
            Attempt = attempt;
            Steps = steps;
        }

        public BotEndReason EndReason { get; }

        public Attempt<TState, TMove, TRejection> Attempt { get; }

        public ReadOnlyArray<BotStep<TMove>> Steps { get; }

        /// <summary>
        /// The near miss of a run that ended on Failed, by the shared definition (<see cref="FailAnalysis.NearMissOf"/>);
        /// None for a run that ended any other way, without a solve.
        /// </summary>
        public Option<NearMiss> NearMissWith(IPositionSolver<TState, TMove> solver, NodeBudget budget) =>
            Attempt.Outcome is Outcome.Failed
                ? Option.Some(FailAnalysis.NearMissOf(Attempt, solver, budget))
                : Option.None<NearMiss>();
    }

    /// <summary>
    /// Plays bot runs through the shared <see cref="Attempt{TState, TMove, TRejection}"/> (shared-design.md §2), so the
    /// near miss keeps one definition. A run is played in Campaign mode with moves only, never a spare, a rewind, Insight
    /// or a concession. It ends when the attempt is Won or Failed, when a position repeats (Cycle) or at the move cap
    /// (StepCap). On each move with two or more legal moves and a mistake rate above 0, the runner draws a number below
    /// 1,000 after the policy has chosen; under the rate it plays a uniformly drawn legal move other than the intended
    /// one, drawing only when two or more remain. Every draw comes from the run's Bot stream.
    /// </summary>
    public static class BotRunner
    {
        public static BotRun<TState, TMove, TRejection> Run<TState, TMove, TRejection>(
            IPuzzleRules<TState, TMove, TRejection> rules, TState start, BotProfile profile, IBotPolicy<TState, TMove> policy, Pcg32 random, MoveCount moveCap)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, IEquatable<TMove>
            where TRejection : class
        {
            var attempt = new Attempt<TState, TMove, TRejection>(rules, start, PlayMode.Campaign);
            var guard = new RunGuard(start.Exact, moveCap);
            var legal = new List<TMove>();
            var steps = new List<BotStep<TMove>>();
            while (attempt.Outcome is Outcome.InPlay)
            {
                legal.Clear();
                rules.AppendLegalMoves(attempt.Current, legal);
                if (legal.Count == 0)
                {
                    throw new InvalidOperationException("The rules list no legal move for a position in play.");
                }

                TMove intended = policy.Intend(attempt.Current, legal, random);
                if (!legal.Contains(intended))
                {
                    throw new InvalidOperationException("The bot's policy intended a move that is not legal.");
                }

                BotStep<TMove> step = WithMistakes(intended, legal, profile.Mistakes, random);
                ReadOnlyArray<PuzzleEvent> events = Play(attempt, step.Move);
                steps.Add(step);
                policy.Observe(events);
                if (attempt.Outcome is Outcome.InPlay && guard.After(attempt.Current.Exact).TryGet(out RunStop stop))
                {
                    return new BotRun<TState, TMove, TRejection>(ReasonFor(stop), attempt, ReadOnlyArray.CopyOf(steps));
                }
            }

            return new BotRun<TState, TMove, TRejection>(ReasonFor(attempt.Outcome), attempt, ReadOnlyArray.CopyOf(steps));
        }

        private static BotStep<TMove> WithMistakes<TMove>(TMove intended, List<TMove> legal, PerMille mistakes, Pcg32 random)
            where TMove : struct, IEquatable<TMove>
        {
            bool mayMistake = mistakes.Value > 0 && legal.Count >= 2;
            if (!mayMistake || random.NextBelow(PerMille.Whole) >= (uint)mistakes.Value)
            {
                return new BotStep<TMove>(intended, wasMistake: false);
            }

            var others = new List<TMove>(legal.Count - 1);
            foreach (TMove move in legal)
            {
                if (!move.Equals(intended))
                {
                    others.Add(move);
                }
            }

            TMove mistaken = others.Count == 1 ? others[0] : others[random.NextIndex(others.Count)];
            return new BotStep<TMove>(mistaken, wasMistake: true);
        }

        private static ReadOnlyArray<PuzzleEvent> Play<TState, TMove, TRejection>(Attempt<TState, TMove, TRejection> attempt, TMove move)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, IEquatable<TMove>
            where TRejection : class => attempt.Move(move) switch
            {
                StepResult<TRejection>.Done done => done.Events,
                StepResult<TRejection>.Illegal => throw new InvalidOperationException("The rules refused a move they listed as legal."),
                StepResult<TRejection>.Refused => throw new InvalidOperationException("The attempt refused a move in play."),
                _ => throw new InvalidOperationException("Unknown step result."),
            };

        private static BotEndReason ReasonFor(Outcome outcome) => outcome switch
        {
            Outcome.Won => BotEndReason.Won,
            Outcome.Failed failed => failed.Kind switch
            {
                FailKind.NoLegalMove => BotEndReason.NoLegalMove,
                FailKind.SlotsFull => BotEndReason.SlotsFull,
                _ => throw new InvalidOperationException("Unknown fail kind."),
            },
            _ => throw new InvalidOperationException("A finished run is Won or Failed."),
        };

        private static BotEndReason ReasonFor(RunStop stop) => stop switch
        {
            RunStop.Cycle => BotEndReason.Cycle,
            RunStop.StepCap => BotEndReason.StepCap,
            _ => throw new InvalidOperationException("Unknown run stop."),
        };
    }

    /// <summary>A run's Bot stream (shared-design.md §2): seeded by its pack and keyed "Bot|{packId}:{slot}:{profile}:{run}".</summary>
    public static class BotStreams
    {
        public static Pcg32 For(PackId pack, LevelIndex slot, BotProfile profile, int run) =>
            SeedDerivation.Stream(Seed.ForPack(pack), StreamKey.ForBot(pack, slot, profile.Name, run));
    }
}

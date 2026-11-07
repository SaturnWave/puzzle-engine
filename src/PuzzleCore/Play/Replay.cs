using System.Collections.Generic;
using PuzzleCore.Records;
using PuzzleCore.Rules;

namespace PuzzleCore.Play
{
    /// <summary>
    /// The server's answer for one record. Closed set. A Jam Failed(NoLegalMove) is still Valid,
    /// but callers log it as an engine fault.
    /// </summary>
    public abstract record ReplayVerdict
    {
        private ReplayVerdict()
        {
        }

        /// <summary>Moves is the length of the final line (rewound moves are not counted).</summary>
        public sealed record Valid(Outcome Outcome, MoveCount Moves, bool SpareUsed) : ReplayVerdict;

        public sealed record Invalid(ActionIndex At, InvalidReason Reason) : ReplayVerdict;

        public sealed record UnknownLevel : ReplayVerdict;

        public sealed record UnsupportedVersion : ReplayVerdict;
    }

    /// <summary>A verdict plus what golden replays compare: the event sequence and the final ExactKey.</summary>
    public sealed record ReplayReport(ReplayVerdict Verdict, ReadOnlyArray<PuzzleEvent> Events, ExactKey Final);

    /// <summary>Validates a recorded attempt by replaying it; it never solves the board again (time linear in the actions).</summary>
    public interface IReplayValidator
    {
        ReplayVerdict Validate(string recordJson);
    }

    /// <summary>Replays recorded actions through an <see cref="Attempt{TState, TMove, TRejection}"/>.</summary>
    public static class Replay
    {
        public static ReplayReport Run<TState, TMove, TRejection>(
            IPuzzleRules<TState, TMove, TRejection> rules,
            TState start,
            PlayMode mode,
            ReadOnlyArray<RecordAction<TMove>> actions)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class
        {
            var attempt = new Attempt<TState, TMove, TRejection>(rules, start, mode);
            var events = new List<PuzzleEvent>();
            for (int index = 0; index < actions.Count; index++)
            {
                StepResult<TRejection> step = Apply(attempt, actions[index]);
                switch (step)
                {
                    case StepResult<TRejection>.Done done:
                        events.AddRange(done.Events);
                        break;
                    case StepResult<TRejection>.Illegal:
                        return Stop(new ActionIndex(index), InvalidReason.IllegalMove, events, attempt);
                    case StepResult<TRejection>.Refused refused:
                        return Stop(new ActionIndex(index), refused.Reason, events, attempt);
                }
            }

            var verdict = new ReplayVerdict.Valid(attempt.Outcome, attempt.Moves, attempt.SpareUsed);
            return new ReplayReport(verdict, ReadOnlyArray.CopyOf(events), attempt.Current.Exact);
        }

        private static StepResult<TRejection> Apply<TState, TMove, TRejection>(Attempt<TState, TMove, TRejection> attempt, RecordAction<TMove> action)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class => action switch
        {
            RecordAction<TMove>.Move move => attempt.Move(move.Played),
            RecordAction<TMove>.Rewind rewind => attempt.Rewind(rewind.Moves),
            RecordAction<TMove>.TakeSpare => attempt.TakeSpare(),
            RecordAction<TMove>.UseInsight insight => attempt.UseInsight(insight.Cell),
            RecordAction<TMove>.Concede => attempt.Concede(),
            _ => throw new System.ArgumentOutOfRangeException(nameof(action), "Unknown record action."),
        };

        private static ReplayReport Stop<TState, TMove, TRejection>(
            ActionIndex at, InvalidReason reason, List<PuzzleEvent> events, Attempt<TState, TMove, TRejection> attempt)
            where TState : struct, IPuzzleState<TState>
            where TMove : struct, System.IEquatable<TMove>
            where TRejection : class =>
            new ReplayReport(new ReplayVerdict.Invalid(at, reason), ReadOnlyArray.CopyOf(events), attempt.Current.Exact);
    }
}

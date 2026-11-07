using System.Collections.Generic;
using PuzzleCore.Records;
using PuzzleCore.Rules;

namespace PuzzleCore.Play
{
    /// <summary>
    /// Why an action is not valid in an attempt. Closed set shared with the replay validator;
    /// the mode is checked first. Add new reasons at the end only.
    /// </summary>
    public enum InvalidReason
    {
        IllegalMove,
        SpareOutsideFail,
        SecondSpare,
        RewindOutsideFail,
        RewindOutOfRange,
        BoosterInCompetitiveMode,
        InsightLimit,
        ActionAfterEnd,
    }

    /// <summary>What one action did: Done(events), Illegal(the game's rejection: a free bump) or Refused(reason).</summary>
    public abstract record StepResult<TRejection>
        where TRejection : class
    {
        private StepResult()
        {
        }

        public sealed record Done(ReadOnlyArray<PuzzleEvent> Events) : StepResult<TRejection>;

        /// <summary>The move broke the game rules. A client shows a bump; it is not recorded and costs nothing.</summary>
        public sealed record Illegal(TRejection Reason) : StepResult<TRejection>;

        /// <summary>The action is not allowed in this mode or at this point of the attempt.</summary>
        public sealed record Refused(InvalidReason Reason) : StepResult<TRejection>;
    }

    /// <summary>
    /// Hidden cells the player has seen revealed or shown by Insight in this attempt. It only grows
    /// (a rewind never hides a seen cell again), lives outside the rules state, and the rules never
    /// read it. Hidden cells are 0-63 (Sort uses vial × 4 + layer ≤ 55; Jam uses PieceId ≤ 23).
    /// </summary>
    public readonly struct SeenSet : System.IEquatable<SeenSet>
    {
        private SeenSet(ulong mask) => Mask = mask;

        public ulong Mask { get; }

        public bool Contains(HiddenCell cell) => cell.Value < 64 && (Mask & (1UL << cell.Value)) != 0;

        public SeenSet With(HiddenCell cell) => cell.Value < 64 ? new SeenSet(Mask | (1UL << cell.Value)) : this;

        public bool Equals(SeenSet other) => Mask == other.Mask;

        public override bool Equals(object? obj) => obj is SeenSet other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Mask ^ (Mask >> 32)));

        public static bool operator ==(SeenSet left, SeenSet right) => left.Equals(right);

        public static bool operator !=(SeenSet left, SeenSet right) => !left.Equals(right);
    }

    /// <summary>
    /// One attempt at a level: the current line of states, the one-spare and one-insight limits,
    /// rewind and the SeenSet. The client and the replay validator both drive this class, so a
    /// record valid on the server is exactly a session the client allowed.
    /// </summary>
    public sealed class Attempt<TState, TMove, TRejection>
        where TState : struct, IPuzzleState<TState>
        where TMove : struct, System.IEquatable<TMove>
        where TRejection : class
    {
        public const int MaxRewind = 5;

        private readonly IPuzzleRules<TState, TMove, TRejection> _rules;

        // _line[k] is the state after k moves on the current line; _line[0] is the start.
        private readonly List<TState> _line = new List<TState>();

        // The line index at which the spare was added; a rewind behind it re-adds the spare.
        private int _spareAtIndex = -1;

        public Attempt(IPuzzleRules<TState, TMove, TRejection> rules, TState start, PlayMode mode)
        {
            _rules = rules;
            Mode = mode;
            _line.Add(start);
        }

        public PlayMode Mode { get; }

        public TState Current => _line[_line.Count - 1];

        public Outcome Outcome => _rules.Evaluate(Current);

        /// <summary>Moves on the current line (rewound moves are not counted).</summary>
        public MoveCount Moves => new MoveCount(_line.Count - 1);

        public bool SpareUsed => _spareAtIndex >= 0;

        public bool InsightUsed { get; private set; }

        public bool Conceded { get; private set; }

        /// <summary>Won or conceded: no further action is valid.</summary>
        public bool HasEnded => Conceded || Outcome is Outcome.Won;

        public SeenSet Seen { get; private set; }

        public StepResult<TRejection> Move(TMove move)
        {
            if (HasEnded)
            {
                return Refuse(InvalidReason.ActionAfterEnd);
            }

            switch (_rules.TryApply(Current, move))
            {
                case MoveResult<TState, TRejection>.Applied applied:
                    _line.Add(applied.Next);
                    Observe(applied.Events);
                    return new StepResult<TRejection>.Done(applied.Events);
                case MoveResult<TState, TRejection>.Illegal illegal:
                    return new StepResult<TRejection>.Illegal(illegal.Reason);
                default:
                    throw new System.InvalidOperationException("Unknown move result.");
            }
        }

        /// <summary>+1 spare, once per attempt, on a fail only (Campaign only).</summary>
        public StepResult<TRejection> TakeSpare()
        {
            if (BoosterRefusal().TryGet(out InvalidReason refusal))
            {
                return Refuse(refusal);
            }

            if (SpareUsed)
            {
                return Refuse(InvalidReason.SecondSpare);
            }

            if (!(Outcome is Outcome.Failed))
            {
                return Refuse(InvalidReason.SpareOutsideFail);
            }

            // The start caps (13 vials, 7 bays) guarantee the spare fits; None would mean a level outside them.
            if (!_rules.TryWithOneSpare(Current).TryGet(out TState spared))
            {
                return Refuse(InvalidReason.SecondSpare);
            }

            _line[_line.Count - 1] = spared;
            _spareAtIndex = _line.Count - 1;
            return Done(new PuzzleEvent.SpareAdded(_rules.CapacityOf(spared)));
        }

        /// <summary>
        /// Back <paramref name="moves"/> moves (1-5) to the exact earlier state, on a fail only
        /// (Campaign only). A spare already taken stays taken.
        /// </summary>
        public StepResult<TRejection> Rewind(MoveCount moves)
        {
            if (BoosterRefusal().TryGet(out InvalidReason refusal))
            {
                return Refuse(refusal);
            }

            if (!(Outcome is Outcome.Failed))
            {
                return Refuse(InvalidReason.RewindOutsideFail);
            }

            if (!PeekRewind(moves).TryGet(out TState restored))
            {
                return Refuse(InvalidReason.RewindOutOfRange);
            }

            int target = _line.Count - 1 - moves.Value;
            _line.RemoveRange(target + 1, _line.Count - target - 1);
            if (SpareUsed && target < _spareAtIndex)
            {
                _spareAtIndex = target;
            }

            _line[target] = restored;
            return Done(new PuzzleEvent.Rewound(moves));
        }

        /// <summary>
        /// The state a rewind of <paramref name="moves"/> would restore (the spare kept), without
        /// changing the attempt; None when the length is outside 1-5 or beyond the line. Lets an
        /// offer check whether some rewind can still win before it is shown.
        /// </summary>
        public Option<TState> PeekRewind(MoveCount moves)
        {
            bool isInRange = moves.Value >= 1 && moves.Value <= MaxRewind && moves.Value <= Moves.Value;
            if (!isInRange)
            {
                return Option.None<TState>();
            }

            int target = _line.Count - 1 - moves.Value;
            TState restored = _line[target];
            bool needsSpare = SpareUsed && target < _spareAtIndex;
            if (needsSpare && _rules.TryWithOneSpare(restored).TryGet(out TState spared))
            {
                restored = spared;
            }

            return Option.Some(restored);
        }

        /// <summary>Shows one hidden cell (Campaign only, at most once per attempt). It changes nothing in the rules state.</summary>
        public StepResult<TRejection> UseInsight(HiddenCell cell)
        {
            if (BoosterRefusal().TryGet(out InvalidReason refusal))
            {
                return Refuse(refusal);
            }

            if (InsightUsed)
            {
                return Refuse(InvalidReason.InsightLimit);
            }

            InsightUsed = true;
            Seen = Seen.With(cell);
            return new StepResult<TRejection>.Done(ReadOnlyArray.Empty<PuzzleEvent>());
        }

        public StepResult<TRejection> Concede()
        {
            if (HasEnded)
            {
                return Refuse(InvalidReason.ActionAfterEnd);
            }

            Conceded = true;
            return new StepResult<TRejection>.Done(ReadOnlyArray.Empty<PuzzleEvent>());
        }

        /// <summary>The mode is checked first (K7: no boosters outside Campaign), then the end of the attempt.</summary>
        private Option<InvalidReason> BoosterRefusal()
        {
            if (Mode != PlayMode.Campaign)
            {
                return Option.Some(InvalidReason.BoosterInCompetitiveMode);
            }

            return HasEnded ? Option.Some(InvalidReason.ActionAfterEnd) : Option.None<InvalidReason>();
        }

        private void Observe(ReadOnlyArray<PuzzleEvent> events)
        {
            foreach (PuzzleEvent item in events)
            {
                if (item is PuzzleEvent.CoverLifted lifted)
                {
                    Seen = Seen.With(new HiddenCell(lifted.Piece.Value));
                }
            }
        }

        private static StepResult<TRejection> Refuse(InvalidReason reason) => new StepResult<TRejection>.Refused(reason);

        private static StepResult<TRejection> Done(PuzzleEvent single) => new StepResult<TRejection>.Done(ReadOnlyArray.Of(single));
    }
}

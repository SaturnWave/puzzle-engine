using System;
using System.Collections.Generic;

namespace PuzzleCore.Rules
{
    /// <summary>
    /// A puzzle state: a readonly struct packed into integers. Applying a move returns a new
    /// value, so a state is never shared or mutated.
    /// </summary>
    public interface IPuzzleState<TSelf> : IEquatable<TSelf>
        where TSelf : struct, IPuzzleState<TSelf>
    {
        ExactKey Exact { get; }

        CanonicalKey Canonical { get; }
    }

    /// <summary>Why a state can no longer be won without help. Closed set.</summary>
    public enum FailKind
    {
        /// <summary>No legal move is left. Cannot happen on a valid Jam level; evaluated defensively.</summary>
        NoLegalMove,

        /// <summary>Jam: every holding slot is full after set clears and the lot is not empty.</summary>
        SlotsFull,
    }

    /// <summary>The state of play. Closed set: InPlay, Won or Failed(kind).</summary>
    public abstract record Outcome
    {
        private Outcome()
        {
        }

        public sealed record InPlay : Outcome;

        public sealed record Won : Outcome;

        public sealed record Failed(FailKind Kind) : Outcome;
    }

    /// <summary>The result of trying one move: Applied(next, events) or Illegal(reason). Illegal moves cost nothing.</summary>
    public abstract record MoveResult<TState, TRejection>
        where TState : struct
        where TRejection : class
    {
        private MoveResult()
        {
        }

        public sealed record Applied(TState Next, ReadOnlyArray<PuzzleEvent> Events) : MoveResult<TState, TRejection>;

        public sealed record Illegal(TRejection Reason) : MoveResult<TState, TRejection>;
    }

    /// <summary>
    /// The rules of one game. The client never decides the rules: the game, generator, bots
    /// and server validator all call this.
    /// </summary>
    public interface IPuzzleRules<TState, TMove, TRejection>
        where TState : struct, IPuzzleState<TState>
        where TMove : struct, IEquatable<TMove>
        where TRejection : class
    {
        /// <summary>Appends every legal move in a stable order (ascending ids).</summary>
        void AppendLegalMoves(in TState state, List<TMove> into);

        MoveResult<TState, TRejection> TryApply(in TState state, TMove move);

        Outcome Evaluate(in TState state);

        /// <summary>The state with one more spare (Sort: an empty vial; Jam: a slot), or None at the packing limit.</summary>
        Option<TState> TryWithOneSpare(in TState state);
    }
}

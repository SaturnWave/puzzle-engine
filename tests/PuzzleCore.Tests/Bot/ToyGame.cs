using System;
using System.Collections.Generic;
using PuzzleCore.Bot;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>A toy position for the bot runner's tests: just a number.</summary>
    internal readonly struct ToyState : IPuzzleState<ToyState>
    {
        public ToyState(int value) => Value = value;

        public int Value { get; }

        public ExactKey Exact => new ExactKey(0UL, unchecked((ulong)Value));

        public CanonicalKey Canonical => new CanonicalKey(0UL, unchecked((ulong)Value));

        public bool Equals(ToyState other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ToyState other && Equals(other);

        public override int GetHashCode() => Value;
    }

    /// <summary>A toy move: add <see cref="Step"/> to the number.</summary>
    internal readonly struct ToyMove : IEquatable<ToyMove>
    {
        public ToyMove(int step) => Step = step;

        public int Step { get; }

        public bool Equals(ToyMove other) => Step == other.Step;

        public override bool Equals(object? obj) => obj is ToyMove other && Equals(other);

        public override int GetHashCode() => Step;
    }

    /// <summary>
    /// Toy rules: the test says which steps are legal at each number and how each number ends. A move adds its step and
    /// raises no event; a spare leaves the number as it is, so a near miss can be asked for.
    /// </summary>
    internal sealed class ToyRules : IPuzzleRules<ToyState, ToyMove, string>
    {
        private readonly Func<int, int[]> _steps;
        private readonly Func<int, Outcome> _outcome;

        public ToyRules(Func<int, int[]> steps, Func<int, Outcome> outcome)
        {
            _steps = steps;
            _outcome = outcome;
        }

        public static Outcome InPlay { get; } = new Outcome.InPlay();

        public static Outcome Won { get; } = new Outcome.Won();

        public RewindFence RewindFence => RewindFence.None;

        /// <summary>Counting up by one from 0; won at 3.</summary>
        public static ToyRules CountToThree() => new(value => new[] { 1 }, value => value >= 3 ? Won : InPlay);

        public void AppendLegalMoves(in ToyState state, List<ToyMove> into)
        {
            if (!(Evaluate(state) is Outcome.InPlay))
            {
                return;
            }

            foreach (int step in _steps(state.Value))
            {
                into.Add(new ToyMove(step));
            }
        }

        public MoveResult<ToyState, string> TryApply(in ToyState state, ToyMove move)
        {
            if (!(Evaluate(state) is Outcome.InPlay) || Array.IndexOf(_steps(state.Value), move.Step) < 0)
            {
                return new MoveResult<ToyState, string>.Illegal("not a move here");
            }

            return new MoveResult<ToyState, string>.Applied(new ToyState(state.Value + move.Step), ReadOnlyArray.Empty<PuzzleEvent>());
        }

        public Outcome Evaluate(in ToyState state) => _outcome(state.Value);

        public Option<ToyState> TryWithOneSpare(in ToyState state) => Option.Some(state);

        public CapacityCount CapacityOf(in ToyState state) => new CapacityCount(1);
    }

    /// <summary>Rules that list the toy's moves but refuse every one of them: a broken rules implementation.</summary>
    internal sealed class RefusingRules : IPuzzleRules<ToyState, ToyMove, string>
    {
        private readonly ToyRules _listed = ToyRules.CountToThree();

        public RewindFence RewindFence => RewindFence.None;

        public void AppendLegalMoves(in ToyState state, List<ToyMove> into) => _listed.AppendLegalMoves(state, into);

        public MoveResult<ToyState, string> TryApply(in ToyState state, ToyMove move) => new MoveResult<ToyState, string>.Illegal("refused");

        public Outcome Evaluate(in ToyState state) => _listed.Evaluate(state);

        public Option<ToyState> TryWithOneSpare(in ToyState state) => _listed.TryWithOneSpare(state);

        public CapacityCount CapacityOf(in ToyState state) => _listed.CapacityOf(state);
    }

    /// <summary>A test policy: the test's function picks the intended move; it counts the moves it was told about.</summary>
    internal sealed class ScriptedPolicy : IBotPolicy<ToyState, ToyMove>
    {
        private readonly Func<IReadOnlyList<ToyMove>, Pcg32, ToyMove> _choose;

        public ScriptedPolicy(Func<IReadOnlyList<ToyMove>, Pcg32, ToyMove> choose) => _choose = choose;

        public int Observed { get; private set; }

        /// <summary>Intends the first legal move and draws nothing.</summary>
        public static ScriptedPolicy First() => new((legal, random) => legal[0]);

        /// <summary>Always intends the given step, legal or not, and draws nothing.</summary>
        public static ScriptedPolicy Intending(int step) => new((legal, random) => new ToyMove(step));

        public void Observe(ReadOnlyArray<PuzzleEvent> events) => Observed++;

        public ToyMove Intend(ToyState state, IReadOnlyList<ToyMove> legal, Pcg32 random) => _choose(legal, random);
    }

    /// <summary>A toy solver that finds nothing and counts its calls.</summary>
    internal sealed class CountingSolver : IPositionSolver<ToyState, ToyMove>
    {
        public int Calls { get; private set; }

        public SolveResult<ToyMove> SolveFrom(in ToyState state, NodeBudget budget)
        {
            Calls++;
            return new SolveResult<ToyMove>.Unsolvable();
        }
    }
}

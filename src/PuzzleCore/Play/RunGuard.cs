using System;
using System.Collections.Generic;
using PuzzleCore.Rules;

namespace PuzzleCore.Play
{
    /// <summary>Why a guarded run was stopped while still in play. Closed set.</summary>
    public enum RunStop
    {
        /// <summary>A position came back within the run.</summary>
        Cycle,

        /// <summary>The run reached its move cap.</summary>
        StepCap,
    }

    /// <summary>
    /// Keeps a scripted run (a bot run, a random playout) from going on forever, with the shared endings of
    /// shared-design.md §2: a position repeated within the run is a Cycle, and reaching the move cap (Par × 3 by the shared
    /// estimate) is a StepCap. A repeated position is reported before the cap. Jam runs never trip it, because every Jam
    /// move sends a vehicle out (P5).
    /// </summary>
    public sealed class RunGuard
    {
        // Lookups only: the set is never enumerated.
        private readonly HashSet<ExactKey> _positions = new HashSet<ExactKey>();
        private readonly int _moveCap;
        private int _moves;

        public RunGuard(ExactKey start, MoveCount moveCap)
        {
            if (moveCap.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(moveCap), moveCap.Value, "A run needs room for at least one move.");
            }

            _moveCap = moveCap.Value;
            _positions.Add(start);
        }

        /// <summary>Counts one move that reached <paramref name="position"/>, still in play, and says whether the run must stop.</summary>
        public Option<RunStop> After(ExactKey position)
        {
            _moves++;
            if (!_positions.Add(position))
            {
                return Option.Some(RunStop.Cycle);
            }

            return _moves >= _moveCap ? Option.Some(RunStop.StepCap) : Option.None<RunStop>();
        }
    }
}

using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Rules;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The Jam rules with the AtReveal rewind fence switched on: Covered Lot as it would play if it
    /// adopted Sealed Vials' fence (open question 8). Lets the shared fence code run on a real game.
    /// </summary>
    internal sealed class FencedJamRules : IPuzzleRules<LotState, Exit, ExitRejection>
    {
        private readonly JamRules _jam;

        public FencedJamRules(LotLevel level) => _jam = new JamRules(level);

        public RewindFence RewindFence => RewindFence.AtReveal;

        public void AppendLegalMoves(in LotState state, List<Exit> into) => _jam.AppendLegalMoves(state, into);

        public MoveResult<LotState, ExitRejection> TryApply(in LotState state, Exit move) => _jam.TryApply(state, move);

        public Outcome Evaluate(in LotState state) => _jam.Evaluate(state);

        public Option<LotState> TryWithOneSpare(in LotState state) => _jam.TryWithOneSpare(state);

        public CapacityCount CapacityOf(in LotState state) => _jam.CapacityOf(state);
    }
}

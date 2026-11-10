using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Rules;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The Jam rules with the rewind fence switched off (<see cref="RewindFence.None"/>). Both games
    /// fence their rewinds at the last reveal, but the shared contract keeps the unfenced value, so
    /// this keeps Attempt's unfenced path tested on a real game.
    /// </summary>
    internal sealed class UnfencedJamRules : IPuzzleRules<LotState, Exit, ExitRejection>
    {
        private readonly JamRules _jam;

        public UnfencedJamRules(LotLevel level) => _jam = new JamRules(level);

        public RewindFence RewindFence => RewindFence.None;

        public void AppendLegalMoves(in LotState state, List<Exit> into) => _jam.AppendLegalMoves(state, into);

        public MoveResult<LotState, ExitRejection> TryApply(in LotState state, Exit move) => _jam.TryApply(state, move);

        public Outcome Evaluate(in LotState state) => _jam.Evaluate(state);

        public Option<LotState> TryWithOneSpare(in LotState state) => _jam.TryWithOneSpare(state);

        public CapacityCount CapacityOf(in LotState state) => _jam.CapacityOf(state);
    }
}

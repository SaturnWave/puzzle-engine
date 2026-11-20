using System;
using System.Collections.Generic;
using PuzzleCore.Jam;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// How a Planner ranks the end of a line of moves (Q79): a win first; then a line still in play, fewer bays in use first
    /// and then fewer colours waiting; a line that fills the bays last, ordered the same way. One move ahead, this is
    /// Greedy's habit: completing a set beats joining a colour already waiting, which beats starting a new one.
    /// </summary>
    public readonly struct LineScore : IEquatable<LineScore>, IComparable<LineScore>
    {
        // Within a band, fewer bays in use rank higher, then fewer colours waiting: 0 to PackingLimit bays, and 0 to
        // MaxValue colours (one per colour id).
        private const int ColourSteps = ColourId.MaxValue + 1;
        private const int BandWidth = (BayCount.PackingLimit + 1) * ColourSteps;
        private const int BaysFullBand = 0;
        private const int InPlayBand = BandWidth;
        private const int WonValue = 2 * BandWidth;

        private readonly int _value;

        private LineScore(int value) => _value = value;

        /// <summary>The line wins: every vehicle has left.</summary>
        public static LineScore Won { get; } = new LineScore(WonValue);

        /// <summary>The line ends with the level still in play.</summary>
        public static LineScore InPlay(int baysInUse, int coloursWaiting) => new(InPlayBand + WithinBand(baysInUse, coloursWaiting));

        /// <summary>The line fills every bay: the rules' SlotsFull.</summary>
        public static LineScore BaysFull(int baysInUse, int coloursWaiting) => new(BaysFullBand + WithinBand(baysInUse, coloursWaiting));

        public int CompareTo(LineScore other) => _value.CompareTo(other._value);

        public bool Equals(LineScore other) => _value == other._value;

        public override bool Equals(object? obj) => obj is LineScore other && Equals(other);

        public override int GetHashCode() => _value;

        public static bool operator ==(LineScore left, LineScore right) => left.Equals(right);

        public static bool operator !=(LineScore left, LineScore right) => !left.Equals(right);

        public static bool operator <(LineScore left, LineScore right) => left._value < right._value;

        public static bool operator >(LineScore left, LineScore right) => left._value > right._value;

        public static bool operator <=(LineScore left, LineScore right) => left._value <= right._value;

        public static bool operator >=(LineScore left, LineScore right) => left._value >= right._value;

        private static int WithinBand(int baysInUse, int coloursWaiting)
        {
            if (baysInUse < 0 || baysInUse > BayCount.PackingLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(baysInUse), baysInUse, "A lot has 0 to 8 bays in use.");
            }

            if (coloursWaiting < 0 || coloursWaiting > ColourId.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(coloursWaiting), coloursWaiting, "At most 15 colours can wait.");
            }

            return ((BayCount.PackingLimit - baysInUse) * ColourSteps) + (ColourId.MaxValue - coloursWaiting);
        }
    }

    /// <summary>
    /// A Planner's look ahead (Q79): it tries every line of up to k moves on the colours it believes, and each first move
    /// takes the score of its best line. A line ends early when it wins or fills the bays.
    /// </summary>
    public static class Lookahead
    {
        /// <summary>
        /// The first moves whose best line of up to <paramref name="plies"/> moves scores highest, in the order of
        /// <paramref name="legal"/>, written into <paramref name="into"/>.
        /// </summary>
        public static void BestFirstMoves(
            LotModel model, ColourView colours, LotPosition position, MoveCount plies, IReadOnlyList<Exit> legal, List<Exit> into)
        {
            if (plies.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(plies), plies.Value, "A Planner looks at least one move ahead.");
            }

            into.Clear();
            LineScore best = default;
            for (int index = 0; index < legal.Count; index++)
            {
                Exit move = legal[index];
                LineScore score = BestLine(model, colours, position.After(move.Piece, colours.Of(move.Piece)), plies.Value - 1);
                if (into.Count == 0 || score > best)
                {
                    best = score;
                    into.Clear();
                }

                if (score == best)
                {
                    into.Add(move);
                }
            }
        }

        /// <summary>The score of the best line of up to <paramref name="movesLeft"/> more moves from the position.</summary>
        private static LineScore BestLine(LotModel model, ColourView colours, LotPosition position, int movesLeft)
        {
            if (model.IsWon(position))
            {
                return LineScore.Won;
            }

            if (position.BaysFull)
            {
                return LineScore.BaysFull(position.BaysInUse, position.ColoursWaiting);
            }

            if (movesLeft == 0)
            {
                return LineScore.InPlay(position.BaysInUse, position.ColoursWaiting);
            }

            Option<LineScore> best = Option.None<LineScore>();
            for (int index = 0; index < model.VehicleCount; index++)
            {
                var piece = new PieceId((byte)index);
                if (!model.CanLeave(position, piece))
                {
                    continue;
                }

                LineScore score = BestLine(model, colours, position.After(piece, colours.Of(piece)), movesLeft - 1);
                if (!best.TryGet(out LineScore sofar) || score > sofar)
                {
                    best = Option.Some(score);
                }
            }

            // A loaded level's vehicles never wait on each other in a cycle (LoadProblem.BlockingCycle), so one can leave.
            return best.TryGet(out LineScore found)
                ? found
                : throw new InvalidOperationException("No vehicle can leave a lot still in play.");
        }
    }
}

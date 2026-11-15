using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PuzzleCore.Rules;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// A mean in thousandths, rounded down: 5380 means 5.38. Never negative. Grader features are integers
    /// (CoveredLot.md, "Grader features"); unlike <see cref="PerMille"/>, which is a share, it has no upper bound.
    /// </summary>
    public readonly struct Thousandths : IEquatable<Thousandths>
    {
        public const int PerUnit = 1000;

        public Thousandths(int value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A mean in thousandths is never negative.");
            }

            Value = value;
        }

        public int Value { get; }

        /// <summary>total / count in thousandths, rounded down. count must be positive and total not negative.</summary>
        public static Thousandths MeanOf(int total, int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "A mean needs at least one value.");
            }

            if (total < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(total), total, "The total is never negative.");
            }

            return new Thousandths((int)((long)total * PerUnit / count));
        }

        public bool Equals(Thousandths other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is Thousandths other && Equals(other);

        public override int GetHashCode() => Value;

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(Thousandths left, Thousandths right) => left.Equals(right);

        public static bool operator !=(Thousandths left, Thousandths right) => !left.Equals(right);
    }

    /// <summary>
    /// The Jam-only grader features of the grading CSV (CoveredLot.md, "Grader features"; Q62, Q63). They stay
    /// outside PlanGrade (shared-design.md §2).
    /// </summary>
    public static class LotFeatures
    {
        /// <summary>
        /// Branching: the mean number of vehicles that can leave, covered ones included, before each move of the par
        /// path, the start included (Q62). Along a winning path every such position is in play, so these are exactly
        /// the legal moves. Throws when the par path does not replay to a win.
        /// </summary>
        public static Thousandths Branching(BuiltLevel built)
        {
            var rules = new JamRules(built.Level);
            var legalMoves = new List<Exit>();
            LotState state = rules.Start;
            int choices = 0;
            foreach (Exit exit in built.ParPath)
            {
                legalMoves.Clear();
                rules.AppendLegalMoves(state, legalMoves);
                choices += legalMoves.Count;
                if (!(rules.TryApply(state, exit) is MoveResult<LotState, ExitRejection>.Applied applied))
                {
                    throw new ArgumentException("The par path must be legal.", nameof(built));
                }

                state = applied.Next;
            }

            if (!(rules.Evaluate(state) is Outcome.Won))
            {
                throw new ArgumentException("The par path must win.", nameof(built));
            }

            return Thousandths.MeanOf(choices, built.ParPath.Count);
        }

        /// <summary>
        /// Buried third: the mean exit depth of the last vehicle of each set (Q63). A vehicle's exit depth is how many
        /// vehicles must leave before it can, at the start: those on its path, those on theirs, and so on. Each
        /// colour's vehicles are ranked shallowest first, and the 3rd, 6th... are the last of each set, because that
        /// is the earliest a player could complete it.
        /// </summary>
        public static Thousandths BuriedThird(LotLevel level)
        {
            int[] depths = ExitDepths(level);
            int total = 0;
            int sets = 0;
            for (int colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                int[] ranked = Enumerable.Range(0, level.VehicleCount)
                    .Where(piece => level.Vehicles[piece].Colour.Value == colour)
                    .Select(piece => depths[piece])
                    .OrderBy(depth => depth)
                    .ToArray();
                for (int last = JamRules.SetSize - 1; last < ranked.Length; last += JamRules.SetSize)
                {
                    total += ranked[last];
                    sets++;
                }
            }

            return Thousandths.MeanOf(total, sets);
        }

        /// <summary>Each vehicle's exit depth at the start, by PieceId.</summary>
        private static int[] ExitDepths(LotLevel level)
        {
            var mustLeaveFirst = new Option<uint>[level.VehicleCount];
            var depths = new int[level.VehicleCount];
            for (int piece = 0; piece < level.VehicleCount; piece++)
            {
                depths[piece] = CountOf(MustLeaveBefore(level, new PieceId((byte)piece), mustLeaveFirst));
            }

            return depths;
        }

        /// <summary>
        /// The vehicles that must leave before this one can, as a PieceId mask: those on its path and, recursively,
        /// those on theirs. The start blocking graph is acyclic (LotLoader), so the recursion ends.
        /// </summary>
        private static uint MustLeaveBefore(LotLevel level, PieceId piece, Option<uint>[] known)
        {
            if (known[piece.Value].TryGet(out uint found))
            {
                return found;
            }

            uint mask = 0;
            foreach (LotCell cell in level.PathOf(piece))
            {
                if (level.PieceAt(cell).TryGet(out PieceId blocker))
                {
                    mask |= (1U << blocker.Value) | MustLeaveBefore(level, blocker, known);
                }
            }

            known[piece.Value] = Option.Some(mask);
            return mask;
        }

        private static int CountOf(uint pieces)
        {
            int count = 0;
            for (uint rest = pieces; rest != 0; rest &= rest - 1)
            {
                count++;
            }

            return count;
        }
    }
}

using System.Collections.Generic;
using PuzzleCore.Rules;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>The Jam events that golden tests expect, built the same way in every test.</summary>
    internal static class JamEvents
    {
        /// <summary>The brief's winning line on the 6×6 board.</summary>
        public static readonly int[] BriefsWinningLine = { 0, 3, 4, 2, 8, 5, 1, 6, 7 };

        /// <summary>The events of <see cref="BriefsWinningLine"/>: 9 exits, 2 covers lifted, 3 sets cleared and the win.</summary>
        public static PuzzleEvent[] OfTheBriefsWinningLine() => new PuzzleEvent[]
        {
            Exited(0, 0),
            Exited(3, 1), new PuzzleEvent.CoverLifted(new PieceId(3), Blue),
            Exited(4, 2), Cleared(Blue, 0, 1, 2),
            Exited(2, 0),
            Exited(8, 1),
            Exited(5, 2), Cleared(Yellow, 0, 1, 2),
            Exited(1, 0),
            Exited(6, 1),
            Exited(7, 2), new PuzzleEvent.CoverLifted(new PieceId(7), Red), Cleared(Red, 0, 1, 2),
            new PuzzleEvent.Won(),
        };

        public static PuzzleEvent Exited(int piece, int slot) => new PuzzleEvent.Exited(new PieceId((byte)piece), new SlotIndex((byte)slot));

        public static PuzzleEvent Cleared(ColourId colour, params int[] slots)
        {
            var indices = new List<SlotIndex>();
            foreach (int slot in slots)
            {
                indices.Add(new SlotIndex((byte)slot));
            }

            return new PuzzleEvent.SetCleared(colour, ReadOnlyArray.CopyOf(indices));
        }
    }
}

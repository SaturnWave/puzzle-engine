using System;
using System.Collections.Generic;
using PuzzleCore.Jam;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>Hand-made boards for the rules tests (CoveredLot.md). None is a graded level.</summary>
    internal static class Boards
    {
        public static readonly ColourId Red = new(1);
        public static readonly ColourId Blue = new(2);
        public static readonly ColourId Yellow = new(3);

        /// <summary>
        /// The brief's 6×6 illustration: pillar at cell 4, covers on pieces 3 (?v, blue) and 7 (?&lt;, red),
        /// tag Not(3, R). Pieces in anchor order: 0 B^ 1 R&gt; 2 Y&gt; 3 ?v 4 B&gt; 5 Y^ 6 R^ 7 ?&lt; 8 Y&gt;.
        /// </summary>
        public static LotDefinition SixBySix(int pillar = 4) => new(
            Width: 6,
            Height: 6,
            Bays: 4,
            Pillars: ReadOnlyArray.Of(pillar),
            Vehicles: ReadOnlyArray.Of(
                Car(Blue, 1, Facing.North),
                Car(Red, 8, Facing.East),
                Car(Yellow, 12, Facing.East),
                Car(Blue, 14, Facing.South, covered: true),
                Car(Blue, 16, Facing.East),
                Car(Yellow, 23, Facing.North),
                Car(Red, 24, Facing.North),
                Car(Red, 27, Facing.West, covered: true),
                Car(Yellow, 33, Facing.East)),
            Clues: ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), Red)));

        /// <summary>
        /// A 5×5 lot of up to ten cars that can all leave at once, so the bay order is entirely the
        /// player's choice. Piece i sits in row i/2: even pieces face west at columns 0-1, odd pieces
        /// face east at columns 3-4, so every front is already at the lot edge.
        /// </summary>
        public static LotDefinition FreeCars(int bays, params ColourId[] colours)
        {
            var vehicles = new List<VehicleSpec>();
            for (int index = 0; index < colours.Length; index++)
            {
                int row = index / 2;
                bool facesWest = index % 2 == 0;
                vehicles.Add(Car(colours[index], (row * 5) + (facesWest ? 0 : 3), facesWest ? Facing.West : Facing.East));
            }

            return new LotDefinition(5, 5, bays, ReadOnlyArray.Empty<int>(), ReadOnlyArray.CopyOf(vehicles), ReadOnlyArray.Empty<LotClue>());
        }

        public static VehicleSpec Car(ColourId colour, int anchor, Facing facing, bool covered = false) =>
            new(colour, anchor, VehicleLength.Two, facing, covered);

        public static LotLevel Load(LotDefinition definition) => LotLoader.Load(definition) switch
        {
            LoadResult.Loaded loaded => loaded.Level,
            LoadResult.Rejected rejected => throw new InvalidOperationException("Test board rejected: " + rejected.Problem),
            _ => throw new InvalidOperationException("Unknown load result."),
        };
    }
}

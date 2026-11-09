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

        /// <summary>The free-cars board B R Y three times with four bays, with these cars under covers.</summary>
        public static LotLevel FreeCarsWithCoversOn(params int[] pieces)
        {
            LotDefinition plain = FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow);
            var vehicles = new List<VehicleSpec>(plain.Vehicles);
            foreach (int piece in pieces)
            {
                vehicles[piece] = vehicles[piece] with { StartsCovered = true };
            }

            return Load(plain with { Vehicles = ReadOnlyArray.CopyOf(vehicles) });
        }

        /// <summary>
        /// A 5×5 lot with four bays where the third blue and the third red each wait behind a yellow
        /// car. Pieces in anchor order: 0 B&lt; 1 R&gt; 2 B&lt; 3 R&gt; 4 Y&lt; 5 B&lt; (behind 4) 6 R&gt; (behind 7)
        /// 7 Y&gt; 8 Y&lt;. Parking 0, 2, 1, 3 fills the bays B B R R, and one more bay cannot rescue it:
        /// only yellows can leave, and a yellow fills the fifth bay.
        /// </summary>
        public static LotDefinition ThirdsBehindYellows() => new(
            Width: 5,
            Height: 5,
            Bays: 4,
            Pillars: ReadOnlyArray.Empty<int>(),
            Vehicles: ReadOnlyArray.Of(
                Car(Blue, 0, Facing.West),
                Car(Red, 3, Facing.East),
                Car(Blue, 5, Facing.West),
                Car(Red, 8, Facing.East),
                Car(Yellow, 10, Facing.West),
                Car(Blue, 12, Facing.West),
                Car(Red, 16, Facing.East),
                Car(Yellow, 18, Facing.East),
                Car(Yellow, 20, Facing.West)),
            Clues: ReadOnlyArray.Empty<LotClue>());

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

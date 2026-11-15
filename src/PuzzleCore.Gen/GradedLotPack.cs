using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Packs;
using PuzzleCore.Randomness;
using PuzzleCore.Records;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// The graded levels of one pack, in strictly ascending slot order, each with its slot's level id: what the grading
    /// report and the presentation file are written from. Rejected slots are left out. The pack's
    /// <see cref="PresentationFile"/> is built at once, and it refuses levels that break those rules.
    /// </summary>
    public sealed class GradedLotPack
    {
        public GradedLotPack(PackId pack, IEnumerable<GradedLot> levels)
        {
            Levels = ReadOnlyArray.CopyOf(levels);
            Presentation = new PresentationFile(GameKind.Jam, pack, ReadOnlyArray.CopyOf(Levels.Select(level =>
                new PresentationLevel(level.Id, level.Slot, level.Fingerprint, level.Grade))));
        }

        public PackId Pack => Presentation.Pack;

        public ReadOnlyArray<GradedLot> Levels { get; }

        public PresentationFile Presentation { get; }
    }
}

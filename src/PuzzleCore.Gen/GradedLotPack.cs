using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Jam;
using PuzzleCore.Packs;
using PuzzleCore.Randomness;
using PuzzleCore.Records;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// The graded levels of one pack, in strictly ascending slot order, each with its slot's level id and the
    /// fingerprint of its canonical text: what the level files, the presentation file and the grading report are
    /// written from. Rejected slots are left out. Gen writes each level's text beside the presentation file that names
    /// its fingerprint, so a level whose fingerprint is not its text's is refused, and so is a level the text cannot
    /// write (<see cref="LotText"/> has letters for colours 1-6 only). The pack's <see cref="PresentationFile"/> is
    /// built at once, and it refuses levels that break its rules.
    /// </summary>
    public sealed class GradedLotPack
    {
        public GradedLotPack(PackId pack, IEnumerable<GradedLot> levels)
        {
            Levels = ReadOnlyArray.CopyOf(levels);
            foreach (GradedLot level in Levels)
            {
                if (level.Fingerprint != LotGrading.FingerprintOf(level.Built.Level))
                {
                    throw new ArgumentException("A level's fingerprint is the fingerprint of its canonical text.", nameof(levels));
                }
            }

            Presentation = new PresentationFile(GameKind.Jam, pack, ReadOnlyArray.CopyOf(Levels.Select(level =>
                new PresentationLevel(level.Id, level.Slot, level.Fingerprint, level.Grade))));
        }

        public PackId Pack => Presentation.Pack;

        public ReadOnlyArray<GradedLot> Levels { get; }

        public PresentationFile Presentation { get; }
    }
}

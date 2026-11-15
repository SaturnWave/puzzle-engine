using System;
using PuzzleCore.Json;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Packs
{
    /// <summary>One level of a presentation file: which level, and its grades.</summary>
    public sealed record PresentationLevel(LevelId Level, LevelIndex Slot, LevelFingerprint Fingerprint, LevelGrade Grade);

    /// <summary>
    /// A pack's presentation file, {packId}.presentation.json: the grades of the pack's levels, kept apart from the
    /// pack so a changed grade never makes a record unverifiable (Q29, TC11). Players see no tiers or labels from it
    /// until the difficulty pass. The game is Sort or Jam and the pack id is set. The levels are in strictly
    /// ascending slot order, each level's id is the one <see cref="LevelId.For"/> gives for its slot, and each level
    /// has a fingerprint, so <see cref="PresentationJson.Write"/> can write every file this constructor accepts. A Jam
    /// level has a peak par and a Sort level none (<see cref="HasPeakPar"/>).
    /// </summary>
    public sealed record PresentationFile
    {
        public PresentationFile(GameKind game, PackId pack, ReadOnlyArray<PresentationLevel> levels)
        {
            if (game is not (GameKind.Sort or GameKind.Jam))
            {
                throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown game.");
            }

            if (pack == default)
            {
                throw new ArgumentException("A presentation file belongs to a pack.", nameof(pack));
            }

            for (int index = 0; index < levels.Count; index++)
            {
                PresentationLevel level = levels[index];
                if (index > 0 && level.Slot.Value <= levels[index - 1].Slot.Value)
                {
                    throw new ArgumentException("The levels are in strictly ascending slot order.", nameof(levels));
                }

                if (LevelId.For(pack, level.Slot) != Option.Some(level.Level))
                {
                    throw new ArgumentException("A level's id is its pack id and its slot: {packId}-{slot:0000}.", nameof(levels));
                }

                if (level.Fingerprint == default)
                {
                    throw new ArgumentException("Every level has the fingerprint of its text.", nameof(levels));
                }

                if (level.Grade.Plan.PeakPar.HasValue != HasPeakPar(game))
                {
                    throw new ArgumentException("A Jam level has a peak par and a Sort level none.", nameof(levels));
                }
            }

            Game = game;
            Pack = pack;
            Levels = levels;
        }

        /// <summary>Whether this game's levels have a peak par (shared-design.md §2, "Grade fields"): Jam's do, Sort's never.</summary>
        public static bool HasPeakPar(GameKind game) => game switch
        {
            GameKind.Sort => false,
            GameKind.Jam => true,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown game."),
        };

        public GameKind Game { get; }

        public PackId Pack { get; }

        public ReadOnlyArray<PresentationLevel> Levels { get; }
    }

    /// <summary>Read(file), UnsupportedVersion, or Invalid(problems with their JSON paths).</summary>
    public abstract record PresentationReadResult
    {
        private PresentationReadResult()
        {
        }

        public sealed record Read(PresentationFile File) : PresentationReadResult;

        /// <summary>The format version is not one this reader knows.</summary>
        public sealed record UnsupportedVersion : PresentationReadResult;

        public sealed record Invalid(ReadOnlyArray<FileProblem> Problems) : PresentationReadResult;
    }
}

using System;
using System.Globalization;
using PuzzleCore.Randomness;

namespace PuzzleCore.Records
{
    /// <summary>The play modes. Names are a public contract (records, analytics); add new modes at the end only.</summary>
    public enum PlayMode
    {
        Campaign,
        DailyProof,
        Competitive,
        Casebook,
        WeeklyTwist,
        Siege,
    }

    /// <summary>Which game a record belongs to.</summary>
    public enum GameKind
    {
        Sort,
        Jam,
    }

    /// <summary>The fixed contract names of modes and games. Never use the enum's ToString or ordinal.</summary>
    public static class ContractNames
    {
        public static string Of(PlayMode mode) => mode switch
        {
            PlayMode.Campaign => "Campaign",
            PlayMode.DailyProof => "DailyProof",
            PlayMode.Competitive => "Competitive",
            PlayMode.Casebook => "Casebook",
            PlayMode.WeeklyTwist => "WeeklyTwist",
            PlayMode.Siege => "Siege",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown play mode."),
        };

        public static string Of(GameKind game) => game switch
        {
            GameKind.Sort => "Sort",
            GameKind.Jam => "Jam",
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unknown game."),
        };

        public static Option<PlayMode> ModeNamed(string name)
        {
            foreach (PlayMode mode in new[] { PlayMode.Campaign, PlayMode.DailyProof, PlayMode.Competitive, PlayMode.Casebook, PlayMode.WeeklyTwist, PlayMode.Siege })
            {
                if (string.Equals(Of(mode), name, StringComparison.Ordinal))
                {
                    return Option.Some(mode);
                }
            }

            return Option.None<PlayMode>();
        }

        public static Option<GameKind> GameNamed(string name)
        {
            foreach (GameKind game in new[] { GameKind.Sort, GameKind.Jam })
            {
                if (string.Equals(Of(game), name, StringComparison.Ordinal))
                {
                    return Option.Some(game);
                }
            }

            return Option.None<GameKind>();
        }
    }

    /// <summary>Milliseconds since the previous action, stamped by the client. Never negative.</summary>
    public readonly struct ElapsedMs : IEquatable<ElapsedMs>
    {
        public ElapsedMs(long value)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Elapsed milliseconds are never negative.");
            }

            Value = value;
        }

        public long Value { get; }

        public bool Equals(ElapsedMs other) => Value == other.Value;

        public override bool Equals(object? obj) => obj is ElapsedMs other && Equals(other);

        public override int GetHashCode() => unchecked((int)(Value ^ (Value >> 32)));

        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);

        public static bool operator ==(ElapsedMs left, ElapsedMs right) => left.Equals(right);

        public static bool operator !=(ElapsedMs left, ElapsedMs right) => !left.Equals(right);
    }

    /// <summary>A level id inside its pack: lowercase ASCII letters, digits and '-', 1-64 characters.</summary>
    public readonly struct LevelId : IEquatable<LevelId>
    {
        private LevelId(string value) => Value = value;

        public string Value { get; }

        public static Option<LevelId> Parse(string text) =>
            PackId.Parse(text).HasValue ? Option.Some(new LevelId(text)) : Option.None<LevelId>();

        public bool Equals(LevelId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is LevelId other && Equals(other);

        public override int GetHashCode() => unchecked((int)SeedDerivation.Fnv1a64(Value ?? string.Empty));

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(LevelId left, LevelId right) => left.Equals(right);

        public static bool operator !=(LevelId left, LevelId right) => !left.Equals(right);
    }

    /// <summary>A level fingerprint: 64 lowercase hex digits of SHA-256 over the level's canonical text.</summary>
    public readonly struct LevelFingerprint : IEquatable<LevelFingerprint>
    {
        public const int Length = 64;

        private LevelFingerprint(string hex) => Hex = hex;

        public string Hex { get; }

        public static Option<LevelFingerprint> Parse(string text)
        {
            if (text.Length != Length)
            {
                return Option.None<LevelFingerprint>();
            }

            foreach (char digit in text)
            {
                bool isHex = (digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f');
                if (!isHex)
                {
                    return Option.None<LevelFingerprint>();
                }
            }

            return Option.Some(new LevelFingerprint(text));
        }

        public bool Equals(LevelFingerprint other) => string.Equals(Hex, other.Hex, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is LevelFingerprint other && Equals(other);

        public override int GetHashCode() => unchecked((int)SeedDerivation.Fnv1a64(Hex ?? string.Empty));

        public override string ToString() => Hex ?? string.Empty;

        public static bool operator ==(LevelFingerprint left, LevelFingerprint right) => left.Equals(right);

        public static bool operator !=(LevelFingerprint left, LevelFingerprint right) => !left.Equals(right);
    }

    /// <summary>Which level a record was played on: its pack, its id and the fingerprint of its text.</summary>
    public sealed record LevelRef(PackId Pack, LevelId Level, LevelFingerprint Fingerprint);

    /// <summary>
    /// One recorded action. Closed set; Undo is reserved and may be added later by addition.
    /// Illegal moves are never recorded.
    /// </summary>
    public abstract record RecordAction<TMove>
        where TMove : struct
    {
        private RecordAction()
        {
        }

        public abstract ElapsedMs SincePrevious { get; }

        public sealed record Move(TMove Played, ElapsedMs SincePrevious) : RecordAction<TMove>
        {
            public override ElapsedMs SincePrevious { get; } = SincePrevious;
        }

        /// <summary>Back <paramref name="Moves"/> Move actions (1-5), on the fail screen only.</summary>
        public sealed record Rewind(MoveCount Moves, ElapsedMs SincePrevious) : RecordAction<TMove>
        {
            public override ElapsedMs SincePrevious { get; } = SincePrevious;
        }

        public sealed record TakeSpare(ElapsedMs SincePrevious) : RecordAction<TMove>
        {
            public override ElapsedMs SincePrevious { get; } = SincePrevious;
        }

        public sealed record UseInsight(HiddenCell Cell, ElapsedMs SincePrevious) : RecordAction<TMove>
        {
            public override ElapsedMs SincePrevious { get; } = SincePrevious;
        }

        public sealed record Concede(ElapsedMs SincePrevious) : RecordAction<TMove>
        {
            public override ElapsedMs SincePrevious { get; } = SincePrevious;
        }
    }

    /// <summary>An attempt as the client recorded it: the pc-record contract (format version 1).</summary>
    public sealed record ActionRecord<TMove>(
        GameKind Game,
        PlayMode Mode,
        LevelRef Level,
        int RulesVersion,
        ReadOnlyArray<RecordAction<TMove>> Actions)
        where TMove : struct;
}

using System.Collections.Generic;
using PuzzleCore.Json;
using PuzzleCore.Randomness;

namespace PuzzleCore.Records
{
    /// <summary>How one game writes and reads the payload of its Move action (Jam: "piece"; Sort: "from", "to").</summary>
    public interface IMoveJson<TMove>
        where TMove : struct
    {
        GameKind Game { get; }

        IReadOnlyList<JsonMember> Write(TMove move);

        /// <summary>The move, or None after reporting the problem on <paramref name="fields"/>.</summary>
        Option<TMove> Read(JsonFields fields);
    }

    /// <summary>Read(record) or Invalid(problems with their JSON paths).</summary>
    public abstract record RecordReadResult<TMove>
        where TMove : struct
    {
        private RecordReadResult()
        {
        }

        public sealed record Read(ActionRecord<TMove> Record) : RecordReadResult<TMove>;

        /// <summary>The format version is not one this reader knows; the validator answers UnsupportedVersion.</summary>
        public sealed record UnsupportedVersion : RecordReadResult<TMove>;

        public sealed record Invalid(ReadOnlyArray<FileProblem> Problems) : RecordReadResult<TMove>;
    }

    /// <summary>
    /// The pc-record JSON (a public contract: it changes only by addition, with a version bump):
    /// <code>
    /// {
    ///   "format": "pc-record", "formatVersion": 1, "game": "Jam", "mode": "Campaign", "rulesVersion": 1,
    ///   "level": { "packId": "…", "levelId": "…", "fingerprint": "&lt;64 hex&gt;" },
    ///   "actions": [ { "t": "Move", "piece": 3, "sincePreviousMs": 850 }, { "t": "Rewind", "n": 2, … },
    ///                { "t": "TakeSpare", … }, { "t": "UseInsight", "cell": 7, … }, { "t": "Concede", … } ]
    /// }
    /// </code>
    /// Reading is strict: unknown fields, another game, bad values are errors. Writing gives the
    /// canonical text (<see cref="JsonWriter"/>), so a record's bytes are the same everywhere.
    /// </summary>
    public static class RecordJson
    {
        public const string Format = "pc-record";
        public const int FormatVersion = 1;

        /// <summary>A day of thinking is plenty; larger gaps are a client bug.</summary>
        private const long MaxElapsedMs = 86_400_000L;

        public static string Write<TMove>(ActionRecord<TMove> record, IMoveJson<TMove> moves)
            where TMove : struct
        {
            var actions = new List<JsonValue>();
            foreach (RecordAction<TMove> action in record.Actions)
            {
                actions.Add(ActionJson(action, moves));
            }

            var json = new JsonObject(new[]
            {
                new JsonMember("format", new JsonString(Format)),
                new JsonMember("formatVersion", new JsonInteger(FormatVersion)),
                new JsonMember("game", new JsonString(ContractNames.Of(record.Game))),
                new JsonMember("mode", new JsonString(ContractNames.Of(record.Mode))),
                new JsonMember("rulesVersion", new JsonInteger(record.RulesVersion)),
                new JsonMember("level", new JsonObject(new[]
                {
                    new JsonMember("packId", new JsonString(record.Level.Pack.Value)),
                    new JsonMember("levelId", new JsonString(record.Level.Level.Value)),
                    new JsonMember("fingerprint", new JsonString(record.Level.Fingerprint.Hex)),
                })),
                new JsonMember("actions", new JsonArray(actions)),
            });
            return JsonWriter.Canonical(json);
        }

        public static RecordReadResult<TMove> Read<TMove>(string text, IMoveJson<TMove> moves)
            where TMove : struct
        {
            var problems = new List<FileProblem>();
            if (!(JsonReader.Parse(text) is JsonParseResult.Parsed parsed))
            {
                problems.Add(new FileProblem("$", "not valid JSON"));
                return Invalid<TMove>(problems);
            }

            JsonFields? root = JsonFields.Of(parsed.Value, "$", problems);
            if (root is null)
            {
                return Invalid<TMove>(problems);
            }

            string? format = root.RequiredString("format");
            long? version = root.RequiredInteger("formatVersion", 0, int.MaxValue);
            if (format != Format)
            {
                root.Report("format", "expected \"" + Format + "\"");
                return Invalid<TMove>(problems);
            }

            if (version != FormatVersion)
            {
                return version is null ? Invalid<TMove>(problems) : new RecordReadResult<TMove>.UnsupportedVersion();
            }

            Option<GameKind> game = ContractNames.GameNamed(root.RequiredString("game") ?? string.Empty);
            if (!game.TryGet(out GameKind gameKind) || gameKind != moves.Game)
            {
                root.Report("game", "expected \"" + ContractNames.Of(moves.Game) + "\"");
            }

            Option<PlayMode> mode = ContractNames.ModeNamed(root.RequiredString("mode") ?? string.Empty);
            if (!mode.HasValue)
            {
                root.Report("mode", "unknown play mode");
            }

            long? rulesVersion = root.RequiredInteger("rulesVersion", 1, int.MaxValue);
            Option<LevelRef> level = ReadLevel(root.RequiredObject("level"));
            var actions = new List<RecordAction<TMove>>();
            JsonArray? actionArray = root.RequiredArray("actions");
            for (int index = 0; actionArray is not null && index < actionArray.Items.Count; index++)
            {
                JsonFields? actionFields = JsonFields.Of(actionArray.Items[index], JsonPath.Index(root.PathOf("actions"), index), problems);
                if (ReadAction(actionFields, moves).TryGet(out RecordAction<TMove>? action))
                {
                    actions.Add(action);
                }
            }

            root.RejectUnknownFields();
            bool hasMode = mode.TryGet(out PlayMode playMode);
            bool hasLevel = level.TryGet(out LevelRef? levelRef);
            if (problems.Count > 0 || !hasMode || !hasLevel || levelRef is null || rulesVersion is null)
            {
                return Invalid<TMove>(problems);
            }

            return new RecordReadResult<TMove>.Read(new ActionRecord<TMove>(
                gameKind, playMode, levelRef, (int)rulesVersion.Value, ReadOnlyArray.CopyOf(actions)));
        }

        private static JsonValue ActionJson<TMove>(RecordAction<TMove> action, IMoveJson<TMove> moves)
            where TMove : struct
        {
            var members = new List<JsonMember>();
            switch (action)
            {
                case RecordAction<TMove>.Move move:
                    members.Add(new JsonMember("t", new JsonString("Move")));
                    members.AddRange(moves.Write(move.Played));
                    break;
                case RecordAction<TMove>.Rewind rewind:
                    members.Add(new JsonMember("t", new JsonString("Rewind")));
                    members.Add(new JsonMember("n", new JsonInteger(rewind.Moves.Value)));
                    break;
                case RecordAction<TMove>.TakeSpare:
                    members.Add(new JsonMember("t", new JsonString("TakeSpare")));
                    break;
                case RecordAction<TMove>.UseInsight insight:
                    members.Add(new JsonMember("t", new JsonString("UseInsight")));
                    members.Add(new JsonMember("cell", new JsonInteger(insight.Cell.Value)));
                    break;
                case RecordAction<TMove>.Concede:
                    members.Add(new JsonMember("t", new JsonString("Concede")));
                    break;
            }

            members.Add(new JsonMember("sincePreviousMs", new JsonInteger(action.SincePrevious.Value)));
            return new JsonObject(members);
        }

        private static Option<RecordAction<TMove>> ReadAction<TMove>(JsonFields? fields, IMoveJson<TMove> moves)
            where TMove : struct
        {
            if (fields is null)
            {
                return Option.None<RecordAction<TMove>>();
            }

            string? type = fields.RequiredString("t");
            long? elapsed = fields.RequiredInteger("sincePreviousMs", 0, MaxElapsedMs);
            Option<RecordAction<TMove>> action = Option.None<RecordAction<TMove>>();
            ElapsedMs since = new ElapsedMs(elapsed ?? 0);
            switch (type)
            {
                case "Move":
                    if (moves.Read(fields).TryGet(out TMove played))
                    {
                        action = Option.Some<RecordAction<TMove>>(new RecordAction<TMove>.Move(played, since));
                    }

                    break;
                case "Rewind":
                    long? count = fields.RequiredInteger("n", 1, int.MaxValue);
                    if (count is not null)
                    {
                        action = Option.Some<RecordAction<TMove>>(new RecordAction<TMove>.Rewind(new MoveCount((int)count.Value), since));
                    }

                    break;
                case "TakeSpare":
                    action = Option.Some<RecordAction<TMove>>(new RecordAction<TMove>.TakeSpare(since));
                    break;
                case "UseInsight":
                    long? cell = fields.RequiredInteger("cell", 0, byte.MaxValue);
                    if (cell is not null)
                    {
                        action = Option.Some<RecordAction<TMove>>(new RecordAction<TMove>.UseInsight(new HiddenCell((byte)cell.Value), since));
                    }

                    break;
                case "Concede":
                    action = Option.Some<RecordAction<TMove>>(new RecordAction<TMove>.Concede(since));
                    break;
                case null:
                    break;
                default:
                    fields.Report("t", "unknown action type");
                    break;
            }

            fields.RejectUnknownFields();
            return elapsed is null ? Option.None<RecordAction<TMove>>() : action;
        }

        private static Option<LevelRef> ReadLevel(JsonFields? fields)
        {
            if (fields is null)
            {
                return Option.None<LevelRef>();
            }

            Option<PackId> pack = PackId.Parse(fields.RequiredString("packId") ?? string.Empty);
            Option<LevelId> level = LevelId.Parse(fields.RequiredString("levelId") ?? string.Empty);
            Option<LevelFingerprint> fingerprint = LevelFingerprint.Parse(fields.RequiredString("fingerprint") ?? string.Empty);
            fields.RejectUnknownFields();
            if (!pack.TryGet(out PackId packId))
            {
                fields.Report("packId", "expected lowercase letters, digits and '-'");
            }

            if (!level.TryGet(out LevelId levelId))
            {
                fields.Report("levelId", "expected lowercase letters, digits and '-'");
            }

            if (!fingerprint.TryGet(out LevelFingerprint print))
            {
                fields.Report("fingerprint", "expected 64 lowercase hex digits");
            }

            return pack.HasValue && level.HasValue && fingerprint.HasValue
                ? Option.Some(new LevelRef(packId, levelId, print))
                : Option.None<LevelRef>();
        }

        private static RecordReadResult<TMove> Invalid<TMove>(List<FileProblem> problems)
            where TMove : struct => new RecordReadResult<TMove>.Invalid(ReadOnlyArray.CopyOf(problems));
    }
}

using System.Collections.Generic;
using PuzzleCore.Json;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Packs
{
    /// <summary>
    /// The pc-presentation JSON (a public contract: it changes only by addition, with a version bump):
    /// <code>
    /// {
    ///   "format": "pc-presentation", "formatVersion": 1, "game": "Jam", "packId": "campaign-jam",
    ///   "levels": [ { "levelId": "campaign-jam-0024", "slot": 24, "fingerprint": "&lt;64 hex&gt;", "kind": "Think",
    ///                 "plan": { "par": 15, "deadEnd": 892, "spareNeed": 0, "minFreeCapacity": 1, "peakPar": 4 },
    ///                 "deduction": { "rung": "D1", "steps": 2 } } ]
    /// }
    /// </code>
    /// An absent value is left out, never written as null: a Plan level has no "deduction", and a Sort level no
    /// "peakPar", which a Jam level always has. Names come from <see cref="ContractNames"/>. Writing gives the canonical
    /// text (<see cref="JsonWriter"/>). Reading is strict, like <see cref="RecordJson"/>: unknown fields, bad values
    /// and levels that break the <see cref="PresentationFile"/> rules are reported with their JSON paths.
    /// </summary>
    public static class PresentationJson
    {
        public const string Format = "pc-presentation";
        public const int FormatVersion = 1;

        public static string Write(PresentationFile file)
        {
            var levels = new List<JsonValue>();
            foreach (PresentationLevel level in file.Levels)
            {
                levels.Add(LevelJson(level));
            }

            var json = new JsonObject(new[]
            {
                new JsonMember("format", new JsonString(Format)),
                new JsonMember("formatVersion", new JsonInteger(FormatVersion)),
                new JsonMember("game", new JsonString(ContractNames.Of(file.Game))),
                new JsonMember("packId", new JsonString(file.Pack.Value)),
                new JsonMember("levels", new JsonArray(levels)),
            });
            return JsonWriter.Canonical(json);
        }

        public static PresentationReadResult Read(string text)
        {
            var problems = new List<FileProblem>();
            if (!(JsonReader.Parse(text) is JsonParseResult.Parsed parsed))
            {
                problems.Add(new FileProblem("$", "not valid JSON"));
                return Invalid(problems);
            }

            JsonFields? root = JsonFields.Of(parsed.Value, "$", problems);
            if (root is null)
            {
                return Invalid(problems);
            }

            string? format = root.RequiredString("format");
            long? version = root.RequiredInteger("formatVersion", 0, int.MaxValue);
            if (format != Format)
            {
                root.Report("format", "expected \"" + Format + "\"");
                return Invalid(problems);
            }

            if (version != FormatVersion)
            {
                return version is null ? Invalid(problems) : new PresentationReadResult.UnsupportedVersion();
            }

            Option<GameKind> game = ContractNames.GameNamed(root.RequiredString("game") ?? string.Empty);
            if (!game.HasValue)
            {
                root.Report("game", "unknown game");
            }

            Option<PackId> pack = PackId.Parse(root.RequiredString("packId") ?? string.Empty);
            if (!pack.HasValue)
            {
                root.Report("packId", "expected lowercase letters, digits and '-'");
            }

            ReadOnlyArray<PresentationLevel> levels = ReadLevels(root, game, pack, problems);
            root.RejectUnknownFields();
            bool hasGame = game.TryGet(out GameKind gameKind);
            bool hasPack = pack.TryGet(out PackId packId);
            if (problems.Count > 0 || !hasGame || !hasPack)
            {
                return Invalid(problems);
            }

            return new PresentationReadResult.Read(new PresentationFile(gameKind, packId, levels));
        }

        private static JsonValue LevelJson(PresentationLevel level)
        {
            var members = new List<JsonMember>
            {
                new JsonMember("levelId", new JsonString(level.Level.Value)),
                new JsonMember("slot", new JsonInteger(level.Slot.Value)),
                new JsonMember("fingerprint", new JsonString(level.Fingerprint.Hex)),
                new JsonMember("kind", new JsonString(ContractNames.Of(level.Grade.Kind))),
                new JsonMember("plan", PlanJson(level.Grade.Plan)),
            };
            if (level.Grade.Deduction.TryGet(out DeductionGrade.Graded? deduction))
            {
                members.Add(new JsonMember("deduction", DeductionJson(deduction)));
            }

            return new JsonObject(members);
        }

        private static JsonValue PlanJson(PlanGrade plan)
        {
            var members = new List<JsonMember>
            {
                new JsonMember("par", new JsonInteger(plan.Par.Value)),
                new JsonMember("deadEnd", new JsonInteger(plan.DeadEnd.Value)),
                new JsonMember("spareNeed", new JsonInteger(plan.SpareNeed.Value)),
                new JsonMember("minFreeCapacity", new JsonInteger(plan.MinFreeCapacity.Value)),
            };
            if (plan.PeakPar.TryGet(out CapacityCount peakPar))
            {
                members.Add(new JsonMember("peakPar", new JsonInteger(peakPar.Value)));
            }

            return new JsonObject(members);
        }

        private static JsonValue DeductionJson(DeductionGrade.Graded deduction) => new JsonObject(new[]
        {
            new JsonMember("rung", new JsonString(ContractNames.Of(deduction.Rung))),
            new JsonMember("steps", new JsonInteger(deduction.Steps.Value)),
        });

        private static ReadOnlyArray<PresentationLevel> ReadLevels(JsonFields root, Option<GameKind> game, Option<PackId> pack, List<FileProblem> problems)
        {
            var levels = new List<PresentationLevel>();
            JsonArray? array = root.RequiredArray("levels");
            for (int index = 0; array is not null && index < array.Items.Count; index++)
            {
                JsonFields? fields = JsonFields.Of(array.Items[index], JsonPath.Index(root.PathOf("levels"), index), problems);
                if (fields is not null && ReadLevel(fields, game, problems).TryGet(out PresentationLevel? level))
                {
                    CheckPlaceInPack(fields, level, pack, levels);
                    levels.Add(level);
                }
            }

            return ReadOnlyArray.CopyOf(levels);
        }

        private static Option<PresentationLevel> ReadLevel(JsonFields fields, Option<GameKind> game, List<FileProblem> problems)
        {
            Option<LevelId> id = LevelId.Parse(fields.RequiredString("levelId") ?? string.Empty);
            long? slot = fields.RequiredInteger("slot", 0, int.MaxValue);
            Option<LevelFingerprint> fingerprint = LevelFingerprint.Parse(fields.RequiredString("fingerprint") ?? string.Empty);
            Option<LevelGrade> grade = ReadGrade(fields, game, problems);
            fields.RejectUnknownFields();
            if (!id.TryGet(out LevelId levelId))
            {
                fields.Report("levelId", "expected lowercase letters, digits and '-'");
            }

            if (!fingerprint.TryGet(out LevelFingerprint print))
            {
                fields.Report("fingerprint", "expected 64 lowercase hex digits");
            }

            return id.HasValue && slot is not null && fingerprint.HasValue && grade.TryGet(out LevelGrade? levelGrade)
                ? Option.Some(new PresentationLevel(levelId, new LevelIndex((int)slot.Value), print, levelGrade))
                : Option.None<PresentationLevel>();
        }

        /// <summary>The kind, the plan grade and the deduction grade, which a Think level needs and a Plan level never has.</summary>
        private static Option<LevelGrade> ReadGrade(JsonFields fields, Option<GameKind> game, List<FileProblem> problems)
        {
            Option<LevelKind> kind = ContractNames.KindNamed(fields.RequiredString("kind") ?? string.Empty);
            Option<PlanGrade> plan = ReadPlan(fields.RequiredObject("plan"), game);
            JsonValue? deductionJson = fields.Optional("deduction");
            Option<DeductionGrade.Graded> deduction = deductionJson is null
                ? Option.None<DeductionGrade.Graded>()
                : ReadDeduction(JsonFields.Of(deductionJson, fields.PathOf("deduction"), problems));
            if (!kind.TryGet(out LevelKind levelKind))
            {
                fields.Report("kind", "unknown level kind");
                return Option.None<LevelGrade>();
            }

            bool isThink = levelKind == LevelKind.Think;
            bool hasDeduction = deductionJson is not null;
            if (isThink != hasDeduction)
            {
                fields.Report("deduction", isThink ? "a Think level needs a deduction grade" : "a Plan level has no deduction grade");
                return Option.None<LevelGrade>();
            }

            return plan.TryGet(out PlanGrade? planGrade) && deduction.HasValue == hasDeduction
                ? Option.Some(new LevelGrade(planGrade, deduction, levelKind))
                : Option.None<LevelGrade>();
        }

        /// <summary>The five plan fields. A Jam level has a peak par and a Sort level none; with an unknown game, already reported, that is not checked.</summary>
        private static Option<PlanGrade> ReadPlan(JsonFields? fields, Option<GameKind> game)
        {
            if (fields is null)
            {
                return Option.None<PlanGrade>();
            }

            long? par = fields.RequiredInteger("par", 0, int.MaxValue);
            long? deadEnd = fields.RequiredInteger("deadEnd", 0, PerMille.Whole);
            long? spareNeed = fields.RequiredInteger("spareNeed", 0, int.MaxValue);
            long? minFreeCapacity = fields.RequiredInteger("minFreeCapacity", 0, int.MaxValue);
            JsonValue? peakParJson = fields.Optional("peakPar");
            long? peakPar = peakParJson is null ? null : fields.AsInteger(peakParJson, fields.PathOf("peakPar"), 0, int.MaxValue);
            fields.RejectUnknownFields();
            if (game.TryGet(out GameKind gameKind) && (peakParJson is not null) != PresentationFile.HasPeakPar(gameKind))
            {
                fields.Report("peakPar", gameKind == GameKind.Jam ? "a Jam level needs a peak par" : "a Sort level has no peak par");
                return Option.None<PlanGrade>();
            }

            // Any missing or bad field has been reported; a present but bad peakPar too.
            if (par is null || deadEnd is null || spareNeed is null || minFreeCapacity is null || (peakParJson is not null && peakPar is null))
            {
                return Option.None<PlanGrade>();
            }

            return Option.Some(new PlanGrade(
                Par: new MoveCount((int)par.Value),
                DeadEnd: new PerMille((int)deadEnd.Value),
                SpareNeed: new CapacityCount((int)spareNeed.Value),
                MinFreeCapacity: new CapacityCount((int)minFreeCapacity.Value),
                PeakPar: peakPar is null ? Option.None<CapacityCount>() : Option.Some(new CapacityCount((int)peakPar.Value))));
        }

        private static Option<DeductionGrade.Graded> ReadDeduction(JsonFields? fields)
        {
            if (fields is null)
            {
                return Option.None<DeductionGrade.Graded>();
            }

            Option<DeductionRung> rung = ContractNames.RungNamed(fields.RequiredString("rung") ?? string.Empty);
            long? steps = fields.RequiredInteger("steps", 0, int.MaxValue);
            fields.RejectUnknownFields();
            if (!rung.TryGet(out DeductionRung deductionRung))
            {
                fields.Report("rung", "unknown deduction rung");
            }

            return rung.HasValue && steps is not null
                ? Option.Some(new DeductionGrade.Graded(deductionRung, new StepCount((int)steps.Value)))
                : Option.None<DeductionGrade.Graded>();
        }

        /// <summary>Reports a level that breaks the PresentationFile rules: strictly ascending slots, ids {packId}-{slot:0000}.</summary>
        private static void CheckPlaceInPack(JsonFields fields, PresentationLevel level, Option<PackId> pack, List<PresentationLevel> before)
        {
            if (before.Count > 0 && level.Slot.Value <= before[before.Count - 1].Slot.Value)
            {
                fields.Report("slot", "slots must be in strictly ascending order");
            }

            if (!pack.TryGet(out PackId packId))
            {
                return;
            }

            Option<LevelId> expected = LevelId.For(packId, level.Slot);
            if (expected != Option.Some(level.Level))
            {
                fields.Report("levelId", expected.TryGet(out LevelId id)
                    ? "expected \"" + id.Value + "\""
                    : "the pack id and the slot make an id longer than 64 characters");
            }
        }

        private static PresentationReadResult Invalid(List<FileProblem> problems) =>
            new PresentationReadResult.Invalid(ReadOnlyArray.CopyOf(problems));
    }
}

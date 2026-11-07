using System;
using System.Collections.Generic;
using System.Globalization;

namespace PuzzleCore.Json
{
    /// <summary>
    /// Reads the fields of one JSON object for a strict file format (levels, records):
    /// each field is asked for by name and type, problems are collected with their paths,
    /// and <see cref="RejectUnknownFields"/> reports every member nobody asked for, so a
    /// misspelt or invented field is an error instead of being silently ignored.
    /// </summary>
    public sealed class JsonFields
    {
        private readonly JsonObject _object;
        private readonly List<FileProblem> _problems;

        // Names asked for so far; only looked up, never enumerated.
        private readonly HashSet<string> _known = new HashSet<string>(StringComparer.Ordinal);

        private JsonFields(JsonObject jsonObject, string path, List<FileProblem> problems)
        {
            _object = jsonObject;
            Path = path;
            _problems = problems;
        }

        public string Path { get; }

        /// <summary>The fields of <paramref name="value"/>, or null after reporting a problem when it is not an object.</summary>
        public static JsonFields? Of(JsonValue value, string path, List<FileProblem> problems)
        {
            if (value is JsonObject jsonObject)
            {
                return new JsonFields(jsonObject, path, problems);
            }

            problems.Add(new FileProblem(path, "expected an object, found " + value.KindName));
            return null;
        }

        public string PathOf(string name) => JsonPath.Member(Path, name);

        /// <summary>The field's value, or null when the object has no such member (absence is allowed).</summary>
        public JsonValue? Optional(string name)
        {
            _known.Add(name);
            return _object.Find(name);
        }

        /// <summary>The field's value, or null after reporting that it is missing.</summary>
        public JsonValue? Required(string name)
        {
            JsonValue? value = Optional(name);
            if (value is null)
            {
                Report(name, "missing field");
            }

            return value;
        }

        public string? RequiredString(string name)
        {
            JsonValue? value = Required(name);
            return value is null ? null : AsString(value, PathOf(name));
        }

        public long? RequiredInteger(string name, long minimum, long maximum)
        {
            JsonValue? value = Required(name);
            return value is null ? null : AsInteger(value, PathOf(name), minimum, maximum);
        }

        public JsonArray? RequiredArray(string name)
        {
            JsonValue? value = Required(name);
            if (value is JsonArray array)
            {
                return array;
            }

            if (value is not null)
            {
                Report(name, "expected an array, found " + value.KindName);
            }

            return null;
        }

        public JsonFields? RequiredObject(string name)
        {
            JsonValue? value = Required(name);
            return value is null ? null : Of(value, PathOf(name), _problems);
        }

        /// <summary>The value as a string, or null after reporting its actual kind.</summary>
        public string? AsString(JsonValue value, string path)
        {
            if (value is JsonString text)
            {
                return text.Value;
            }

            _problems.Add(new FileProblem(path, "expected a string, found " + value.KindName));
            return null;
        }

        /// <summary>The value as an integer in [minimum, maximum], or null after reporting why not.</summary>
        public long? AsInteger(JsonValue value, string path, long minimum, long maximum)
        {
            if (!(value is JsonInteger integer))
            {
                _problems.Add(new FileProblem(path, "expected an integer, found " + value.KindName));
                return null;
            }

            bool isInRange = integer.Value >= minimum && integer.Value <= maximum;
            if (!isInRange)
            {
                _problems.Add(new FileProblem(path, integer.Value.ToString(CultureInfo.InvariantCulture) + " is outside "
                    + minimum.ToString(CultureInfo.InvariantCulture) + ".." + maximum.ToString(CultureInfo.InvariantCulture)));
                return null;
            }

            return integer.Value;
        }

        public void Report(string name, string message) => _problems.Add(new FileProblem(PathOf(name), message));

        /// <summary>Reports, in member order, every member that no Optional/Required call asked for.</summary>
        public void RejectUnknownFields()
        {
            foreach (JsonMember member in _object.Members)
            {
                if (!_known.Contains(member.Name))
                {
                    Report(member.Name, "unknown field");
                }
            }
        }
    }
}

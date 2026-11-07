using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PuzzleCore.Json
{
    /// <summary>
    /// A JSON value as the strict reader produces it. A closed family: the six kinds in this
    /// file are the only ones (the constructor is private to this assembly), so a type switch
    /// over them is complete. Numbers are integers only, because the engine core has no floating
    /// point. Values are immutable, hold only well-formed UTF-16 text and are never nested
    /// deeper than <see cref="JsonReader.MaxDepth"/>, so every value that can be built can
    /// also be written canonically and read back.
    /// </summary>
    public abstract class JsonValue
    {
        private protected JsonValue(int depth)
        {
            Depth = depth;
        }

        /// <summary>Nesting depth: 0 for a scalar, 1 + the deepest child for an array or object.</summary>
        public int Depth { get; }

        /// <summary>The kind in words, for error messages: "an object", "a string"...</summary>
        public abstract string KindName { get; }

        /// <summary>The depth of a container holding <paramref name="children"/>; refuses one deeper than the reader accepts.</summary>
        private protected static int ContainerDepth(IEnumerable<JsonValue> children, string parameterName)
        {
            int depth = 1 + children.Select(child => child.Depth).DefaultIfEmpty(0).Max();
            if (depth > JsonReader.MaxDepth)
            {
                throw new ArgumentException("JSON values nest at most " + JsonReader.MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels deep.", parameterName);
            }

            return depth;
        }
    }

    /// <summary>One member of a JSON object.</summary>
    public sealed class JsonMember
    {
        public JsonMember(string name, JsonValue value)
        {
            JsonText.RequireWellFormed(name, nameof(name));
            Name = name;
            Value = value;
        }

        public string Name { get; }

        public JsonValue Value { get; }
    }

    /// <summary>An object: members in the order they were read or given; names are unique.</summary>
    public sealed class JsonObject : JsonValue
    {
        private readonly IReadOnlyList<JsonMember> _members;

        // Lookups only; never enumerated, because Dictionary order is not part of any contract.
        private readonly Dictionary<string, JsonValue> _byName;

        public JsonObject(IEnumerable<JsonMember> members)
            : this(members.ToList())
        {
        }

        private JsonObject(List<JsonMember> members)
            : base(ContainerDepth(members.Select(member => member.Value), nameof(members)))
        {
            _byName = new Dictionary<string, JsonValue>(StringComparer.Ordinal);
            foreach (JsonMember member in members)
            {
                if (!_byName.TryAdd(member.Name, member.Value))
                {
                    throw new ArgumentException("A JSON object has the member \"" + member.Name + "\" twice.", nameof(members));
                }
            }

            _members = members.AsReadOnly();
        }

        /// <summary>The members in their original order.</summary>
        public IReadOnlyList<JsonMember> Members => _members;

        public override string KindName => "an object";

        /// <summary>The value of the member called <paramref name="name"/>, or null when the object has no such member.</summary>
        public JsonValue? Find(string name) => _byName.TryGetValue(name, out JsonValue? value) ? value : null;
    }

    /// <summary>An array: items in order.</summary>
    public sealed class JsonArray : JsonValue
    {
        private readonly IReadOnlyList<JsonValue> _items;

        public JsonArray(IEnumerable<JsonValue> items)
            : this(items.ToList())
        {
        }

        private JsonArray(List<JsonValue> items)
            : base(ContainerDepth(items, nameof(items)))
        {
            _items = items.AsReadOnly();
        }

        public IReadOnlyList<JsonValue> Items => _items;

        public override string KindName => "an array";
    }

    /// <summary>A string; always well-formed UTF-16 (no unpaired surrogates).</summary>
    public sealed class JsonString : JsonValue
    {
        public JsonString(string value)
            : base(0)
        {
            JsonText.RequireWellFormed(value, nameof(value));
            Value = value;
        }

        public string Value { get; }

        public override string KindName => "a string";
    }

    /// <summary>An integer in the Int64 range: the only kind of JSON number this code base accepts.</summary>
    public sealed class JsonInteger : JsonValue
    {
        public JsonInteger(long value)
            : base(0)
        {
            Value = value;
        }

        public long Value { get; }

        public override string KindName => "an integer";
    }

    /// <summary>true or false.</summary>
    public sealed class JsonBoolean : JsonValue
    {
        private JsonBoolean(bool value)
            : base(0)
        {
            Value = value;
        }

        public static JsonBoolean True { get; } = new JsonBoolean(true);

        public static JsonBoolean False { get; } = new JsonBoolean(false);

        public bool Value { get; }

        public override string KindName => "a boolean";
    }

    /// <summary>null. Our formats never use it, but the reader must still recognise it to reject it with a clear message.</summary>
    public sealed class JsonNull : JsonValue
    {
        private JsonNull()
            : base(0)
        {
        }

        public static JsonNull Instance { get; } = new JsonNull();

        public override string KindName => "null";
    }

    /// <summary>Text checks shared by the value types that hold names or strings.</summary>
    internal static class JsonText
    {
        /// <summary>Refuses text with an unpaired surrogate: it has no UTF-8 form, so it could not be written canonically.</summary>
        public static void RequireWellFormed(string text, string parameterName)
        {
            if (!IsWellFormedUtf16(text))
            {
                throw new ArgumentException("JSON text must not contain unpaired surrogates.", parameterName);
            }
        }

        /// <summary>Whether every high surrogate is directly followed by a low surrogate and every low surrogate directly preceded by a high one.</summary>
        public static bool IsWellFormedUtf16(string text)
        {
            for (int index = 0; index < text.Length; index++)
            {
                char unit = text[index];
                if (char.IsLowSurrogate(unit))
                {
                    return false;
                }

                if (!char.IsHighSurrogate(unit))
                {
                    continue;
                }

                bool hasLowHalf = index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]);
                if (!hasLowHalf)
                {
                    return false;
                }

                // Skip the low half that completes the pair.
                index++;
            }

            return true;
        }
    }
}

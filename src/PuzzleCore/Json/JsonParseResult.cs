using System;
using System.Globalization;

namespace PuzzleCore.Json
{
    /// <summary>A place in JSON text: the 0-based UTF-16 offset and the 1-based line and column it falls on.</summary>
    public readonly struct JsonPosition : IEquatable<JsonPosition>
    {
        private JsonPosition(int offset, int line, int column)
        {
            Offset = offset;
            Line = line;
            Column = column;
        }

        public int Offset { get; }

        /// <summary>1-based; lines end at '\n'.</summary>
        public int Line { get; }

        /// <summary>1-based, counted in UTF-16 code units.</summary>
        public int Column { get; }

        /// <summary>The position of <paramref name="offset"/> in <paramref name="text"/>.</summary>
        public static JsonPosition In(string text, int offset)
        {
            if (offset < 0 || offset > text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "The offset must lie within the text.");
            }

            int line = 1;
            int lineStart = 0;
            for (int index = 0; index < offset; index++)
            {
                if (text[index] == '\n')
                {
                    line++;
                    lineStart = index + 1;
                }
            }

            return new JsonPosition(offset, line, offset - lineStart + 1);
        }

        public bool Equals(JsonPosition other) => Offset == other.Offset && Line == other.Line && Column == other.Column;

        public override bool Equals(object? obj) => obj is JsonPosition other && Equals(other);

        public override int GetHashCode() => Offset;

        /// <summary>For example "line 3, column 7 (offset 41)".</summary>
        public override string ToString() =>
            "line " + Line.ToString(CultureInfo.InvariantCulture)
            + ", column " + Column.ToString(CultureInfo.InvariantCulture)
            + " (offset " + Offset.ToString(CultureInfo.InvariantCulture) + ")";

        public static bool operator ==(JsonPosition left, JsonPosition right) => left.Equals(right);

        public static bool operator !=(JsonPosition left, JsonPosition right) => !left.Equals(right);
    }

    /// <summary>The result of reading JSON text: the value, or the first error and where it is.</summary>
    public abstract record JsonParseResult
    {
        private JsonParseResult()
        {
        }

        public sealed record Parsed(JsonValue Value) : JsonParseResult;

        public sealed record Failed(JsonPosition Position, string Message) : JsonParseResult
        {
            /// <summary>For example "line 1, column 9 (offset 8): fractions are not allowed".</summary>
            public override string ToString() => Position.ToString() + ": " + Message;
        }
    }
}

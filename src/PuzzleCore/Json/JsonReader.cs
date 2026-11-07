using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PuzzleCore.Json
{
    /// <summary>
    /// A strict reader for RFC 8259 JSON text, written without packages so the Unity client,
    /// the server and the tools read rules, levels and records identically. It accepts less
    /// than RFC 8259 allows, never more: numbers must be integers in the Int64 range (no
    /// fraction, exponent, leading zero or "-0"), an object may not repeat a member name,
    /// strings may not hold unpaired surrogates, and only space, tab, line feed and carriage
    /// return count as whitespace. The first problem ends reading and is reported with its
    /// position.
    /// </summary>
    public static class JsonReader
    {
        /// <summary>
        /// The deepest nesting of objects and arrays accepted. Our formats nest at most five
        /// levels; the limit bounds the reader's recursion so hostile input (a record sent to
        /// the server) cannot exhaust the stack of a phone or server thread.
        /// </summary>
        public const int MaxDepth = 64;

        /// <summary>
        /// The largest input accepted, in UTF-8 bytes (4 MiB). Rules files and long records
        /// are tens of kilobytes; the limit bounds memory and time spent on untrusted input
        /// with a wide margin.
        /// </summary>
        public const int MaxInputBytes = 4 * 1024 * 1024;

        public static JsonParseResult Parse(string text)
        {
            // A UTF-8 encoding is never shorter than the UTF-16 length, so the cheap length
            // test settles huge inputs before counting their bytes.
            bool isTooLarge = text.Length > MaxInputBytes || Encoding.UTF8.GetByteCount(text) > MaxInputBytes;
            if (isTooLarge)
            {
                return new JsonParseResult.Failed(JsonPosition.In(text, 0), "the input is larger than 4 MiB (" + MaxInputBytes.ToString(CultureInfo.InvariantCulture) + " bytes)");
            }

            return new JsonParser(text).Document();
        }
    }

    /// <summary>
    /// The recursive-descent parser behind <see cref="JsonReader"/>. Each method returns the
    /// value it read, or null after recording the error; every caller returns at once on
    /// null, so the first error is the one reported. Recursion depth is bounded by
    /// <see cref="JsonReader.MaxDepth"/>.
    /// </summary>
    internal sealed class JsonParser
    {
        /// <summary>The magnitude of long.MinValue, the most negative integer accepted.</summary>
        private const ulong NegativeLimit = 9223372036854775808UL;

        /// <summary>The digits of long.MaxValue; any longer run of digits is out of range.</summary>
        private const int MaxInt64Digits = 19;

        private const char ByteOrderMark = '\uFEFF';

        private readonly string _text;
        private int _index;
        private JsonParseResult.Failed? _failure;

        public JsonParser(string text)
        {
            _text = text;
        }

        private bool AtEnd => _index >= _text.Length;

        public JsonParseResult Document()
        {
            bool startsWithByteOrderMark = _text.Length > 0 && _text[0] == ByteOrderMark;
            JsonValue? value = startsWithByteOrderMark
                ? Fail(0, "a byte order mark (U+FEFF) is not allowed; save the text as UTF-8 without one")
                : ReadValue(0);
            if (value is not null)
            {
                SkipWhitespace();
                if (!AtEnd)
                {
                    value = Fail(_index, "unexpected " + DescribeNext() + " after the JSON value");
                }
            }

            if (value is not null)
            {
                return new JsonParseResult.Parsed(value);
            }

            return _failure ?? throw new InvalidOperationException("The JSON parser stopped without recording an error.");
        }

        /// <summary>A value whose enclosing containers are <paramref name="depth"/> levels deep.</summary>
        private JsonValue? ReadValue(int depth)
        {
            SkipWhitespace();
            if (AtEnd)
            {
                return Fail(_index, "expected a value, found the end of the input");
            }

            char next = _text[_index];
            switch (next)
            {
                case '{':
                    return ReadObject(depth + 1);
                case '[':
                    return ReadArray(depth + 1);
                case '"':
                    string? text = ReadString();
                    return text is null ? null : new JsonString(text);
                case 't':
                    return ReadLiteral("true", JsonBoolean.True);
                case 'f':
                    return ReadLiteral("false", JsonBoolean.False);
                case 'n':
                    return ReadLiteral("null", JsonNull.Instance);
                default:
                    bool startsNumber = next == '-' || IsDigit(next);
                    return startsNumber ? ReadNumber() : Fail(_index, "expected a value, found " + DescribeNext());
            }
        }

        private JsonValue? ReadObject(int depth)
        {
            if (depth > JsonReader.MaxDepth)
            {
                return Fail(_index, "objects and arrays nest more than " + JsonReader.MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels deep");
            }

            _index++;
            var members = new List<JsonMember>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            SkipWhitespace();
            if (Next('}'))
            {
                return new JsonObject(members);
            }

            while (true)
            {
                SkipWhitespace();
                int nameAt = _index;
                if (AtEnd || _text[_index] != '"')
                {
                    return Fail(_index, "expected a member name in double quotes, found " + DescribeNext());
                }

                string? name = ReadString();
                if (name is null)
                {
                    return null;
                }

                if (!names.Add(name))
                {
                    return Fail(nameAt, "the member name " + JsonWriter.Canonical(new JsonString(name)) + " appears twice in one object");
                }

                SkipWhitespace();
                if (!Next(':'))
                {
                    return Fail(_index, "expected ':' after the member name, found " + DescribeNext());
                }

                JsonValue? value = ReadValue(depth);
                if (value is null)
                {
                    return null;
                }

                members.Add(new JsonMember(name, value));
                SkipWhitespace();
                if (Next('}'))
                {
                    return new JsonObject(members);
                }

                if (!Next(','))
                {
                    return Fail(_index, "expected ',' or '}' in an object, found " + DescribeNext());
                }
            }
        }

        private JsonValue? ReadArray(int depth)
        {
            if (depth > JsonReader.MaxDepth)
            {
                return Fail(_index, "objects and arrays nest more than " + JsonReader.MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels deep");
            }

            _index++;
            var items = new List<JsonValue>();
            SkipWhitespace();
            if (Next(']'))
            {
                return new JsonArray(items);
            }

            while (true)
            {
                JsonValue? item = ReadValue(depth);
                if (item is null)
                {
                    return null;
                }

                items.Add(item);
                SkipWhitespace();
                if (Next(']'))
                {
                    return new JsonArray(items);
                }

                if (!Next(','))
                {
                    return Fail(_index, "expected ',' or ']' in an array, found " + DescribeNext());
                }
            }
        }

        /// <summary>
        /// A string starting at the opening quote. Surrogates are paired on the decoded code
        /// units, so a pair may be written raw, escaped as \uD83D\uDE00, or mixed.
        /// </summary>
        private string? ReadString()
        {
            int start = _index;
            _index++;
            var builder = new StringBuilder();
            int? unpairedHighAt = null;
            while (true)
            {
                if (AtEnd)
                {
                    return FailString(start, "the string is not closed");
                }

                char current = _text[_index];
                if (current == '"')
                {
                    _index++;
                    return unpairedHighAt is { } highAt
                        ? FailString(highAt, "a high surrogate must be followed by a low surrogate")
                        : builder.ToString();
                }

                int unitAt = _index;
                char? unit = current == '\\' ? ReadEscape() : ReadPlainUnit(current);
                if (!(unit is { } decoded))
                {
                    return null;
                }

                bool isLowSurrogate = char.IsLowSurrogate(decoded);
                if (unpairedHighAt is { } pendingAt && !isLowSurrogate)
                {
                    return FailString(pendingAt, "a high surrogate must be followed by a low surrogate");
                }

                if (unpairedHighAt is null && isLowSurrogate)
                {
                    return FailString(unitAt, "a low surrogate must follow a high surrogate");
                }

                unpairedHighAt = char.IsHighSurrogate(decoded) ? unitAt : (int?)null;
                builder.Append(decoded);
            }
        }

        private char? ReadPlainUnit(char current)
        {
            if (current < ' ')
            {
                return FailUnit(_index, "the control character " + Describe(current) + " must be escaped in a string");
            }

            _index++;
            return current;
        }

        private char? ReadEscape()
        {
            int backslashAt = _index;
            _index++;
            if (AtEnd)
            {
                return FailUnit(backslashAt, "the escape is not complete");
            }

            char kind = _text[_index];
            _index++;
            switch (kind)
            {
                case '"':
                    return '"';
                case '\\':
                    return '\\';
                case '/':
                    return '/';
                case 'b':
                    return '\b';
                case 'f':
                    return '\f';
                case 'n':
                    return '\n';
                case 'r':
                    return '\r';
                case 't':
                    return '\t';
                case 'u':
                    return ReadHexUnit(backslashAt);
                default:
                    return FailUnit(backslashAt, "unknown escape: a backslash followed by " + Describe(kind));
            }
        }

        /// <summary>The four hexadecimal digits after \u (either case), as one UTF-16 code unit.</summary>
        private char? ReadHexUnit(int backslashAt)
        {
            int value = 0;
            for (int digit = 0; digit < 4; digit++)
            {
                int? nibble = AtEnd ? null : HexValue(_text[_index]);
                if (!(nibble is { } present))
                {
                    return FailUnit(backslashAt, "\\u must be followed by four hexadecimal digits");
                }

                value = (value * 16) + present;
                _index++;
            }

            return (char)value;
        }

        /// <summary>
        /// An integer. All digits are scanned first so the most useful message wins: a
        /// fraction or exponent is named as such before leading zeros, "-0" or range.
        /// </summary>
        private JsonValue? ReadNumber()
        {
            int start = _index;
            bool isNegative = Next('-');
            int digitsStart = _index;
            while (!AtEnd && IsDigit(_text[_index]))
            {
                _index++;
            }

            int digitCount = _index - digitsStart;
            if (digitCount == 0)
            {
                return Fail(start, "a minus sign must be followed by digits");
            }

            if (Peek('.'))
            {
                return Fail(_index, "fractions are not allowed; numbers are integers");
            }

            if (Peek('e') || Peek('E'))
            {
                return Fail(_index, "exponents are not allowed; write the integer in full");
            }

            bool startsWithZero = _text[digitsStart] == '0';
            if (startsWithZero && digitCount > 1)
            {
                return Fail(start, "leading zeros are not allowed");
            }

            if (startsWithZero && isNegative)
            {
                return Fail(start, "-0 is not allowed; write 0");
            }

            long? value = ToInt64(digitsStart, digitCount, isNegative);
            return value is { } integer ? new JsonInteger(integer) : Fail(start, "the number is outside the Int64 range");
        }

        /// <summary>The digits as a signed 64-bit integer, or null when they do not fit.</summary>
        private long? ToInt64(int digitsStart, int digitCount, bool isNegative)
        {
            if (digitCount > MaxInt64Digits)
            {
                return null;
            }

            // 19 decimal digits are below 10^19 < 2^64, so the magnitude itself cannot overflow.
            ulong magnitude = 0;
            for (int index = digitsStart; index < digitsStart + digitCount; index++)
            {
                magnitude = (magnitude * 10) + (uint)(_text[index] - '0');
            }

            if (isNegative)
            {
                if (magnitude > NegativeLimit)
                {
                    return null;
                }

                // long.MinValue has no positive counterpart, so it cannot be negated into place.
                return magnitude == NegativeLimit ? long.MinValue : -(long)magnitude;
            }

            return magnitude > long.MaxValue ? null : (long)magnitude;
        }

        private JsonValue? ReadLiteral(string word, JsonValue value)
        {
            bool matches = _text.Length - _index >= word.Length && string.CompareOrdinal(_text, _index, word, 0, word.Length) == 0;
            if (!matches)
            {
                return Fail(_index, "unknown word; expected true, false or null");
            }

            _index += word.Length;
            return value;
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && IsWhitespace(_text[_index]))
            {
                _index++;
            }
        }

        /// <summary>Consumes <paramref name="expected"/> when it is the next character.</summary>
        private bool Next(char expected)
        {
            if (!Peek(expected))
            {
                return false;
            }

            _index++;
            return true;
        }

        private bool Peek(char expected) => !AtEnd && _text[_index] == expected;

        private string DescribeNext() => AtEnd ? "the end of the input" : Describe(_text[_index]);

        private JsonValue? Fail(int offset, string message)
        {
            Record(offset, message);
            return null;
        }

        private string? FailString(int offset, string message)
        {
            Record(offset, message);
            return null;
        }

        private char? FailUnit(int offset, string message)
        {
            Record(offset, message);
            return null;
        }

        /// <summary>Keeps the first error: later ones are consequences of it.</summary>
        private void Record(int offset, string message) =>
            _failure ??= new JsonParseResult.Failed(JsonPosition.In(_text, offset), message);

        /// <summary>The four whitespace characters of RFC 8259; others (no-break space, BOM...) are errors.</summary>
        private static bool IsWhitespace(char character) =>
            character == ' ' || character == '\t' || character == '\n' || character == '\r';

        private static bool IsDigit(char character) => character >= '0' && character <= '9';

        private static int? HexValue(char character)
        {
            if (IsDigit(character))
            {
                return character - '0';
            }

            if (character >= 'a' && character <= 'f')
            {
                return character - 'a' + 10;
            }

            if (character >= 'A' && character <= 'F')
            {
                return character - 'A' + 10;
            }

            return null;
        }

        /// <summary>'x' for printable ASCII, U+XXXX for everything else, so messages never carry invisible characters.</summary>
        private static string Describe(char character)
        {
            bool isPrintableAscii = character > ' ' && character < '\u007F';
            return isPrintableAscii
                ? "'" + new string(character, 1) + "'"
                : "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture);
        }
    }
}

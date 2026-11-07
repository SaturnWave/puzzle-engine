using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PuzzleCore.Json
{
    /// <summary>
    /// The canonical form of a JSON value: compact (no whitespace), object members sorted by
    /// ordinal UTF-16 comparison of their names, strings escaped minimally ('"' and '\\'
    /// with a backslash, control characters below U+0020 as \u00xx in lower-case hex,
    /// everything else as itself) and integers in invariant form. Two values that mean the
    /// same have the same canonical text, whatever the member order or whitespace of the
    /// file they came from; the canonical bytes are its UTF-8 encoding. Rules fingerprints,
    /// level files and action records are built on this, so it is a public contract.
    /// </summary>
    public static class JsonWriter
    {
        private const string HexDigits = "0123456789abcdef";

        public static string Canonical(JsonValue value)
        {
            var output = new StringBuilder();
            Write(value, output);
            return output.ToString();
        }

        /// <summary>UTF-8 of <see cref="Canonical"/>. Lossless, because JSON values never hold unpaired surrogates.</summary>
        public static byte[] CanonicalUtf8(JsonValue value) => Encoding.UTF8.GetBytes(Canonical(value));

        /// <summary>The lower-case hex SHA-256 of the canonical bytes: the fingerprint of rules files and levels.</summary>
        public static string Fingerprint(JsonValue value)
        {
            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(CanonicalUtf8(value));
            }

            var text = new StringBuilder(digest.Length * 2);
            foreach (byte octet in digest)
            {
                text.Append(HexDigits[octet >> 4]).Append(HexDigits[octet & 0xF]);
            }

            return text.ToString();
        }

        // Recursion is bounded: values never nest deeper than JsonReader.MaxDepth.
        private static void Write(JsonValue value, StringBuilder output)
        {
            switch (value)
            {
                case JsonObject jsonObject:
                    WriteObject(jsonObject, output);
                    break;
                case JsonArray array:
                    WriteArray(array, output);
                    break;
                case JsonString text:
                    WriteString(text.Value, output);
                    break;
                case JsonInteger integer:
                    output.Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                    break;
                case JsonBoolean boolean:
                    output.Append(boolean.Value ? "true" : "false");
                    break;
                case JsonNull _:
                    output.Append("null");
                    break;
                default:
                    throw new NotSupportedException("Unknown JSON value kind: the JsonValue family is closed.");
            }
        }

        private static void WriteObject(JsonObject jsonObject, StringBuilder output)
        {
            output.Append('{');
            bool isFirst = true;
            foreach (JsonMember member in jsonObject.Members.OrderBy(member => member.Name, StringComparer.Ordinal))
            {
                if (!isFirst)
                {
                    output.Append(',');
                }

                WriteString(member.Name, output);
                output.Append(':');
                Write(member.Value, output);
                isFirst = false;
            }

            output.Append('}');
        }

        private static void WriteArray(JsonArray array, StringBuilder output)
        {
            output.Append('[');
            for (int index = 0; index < array.Items.Count; index++)
            {
                if (index > 0)
                {
                    output.Append(',');
                }

                Write(array.Items[index], output);
            }

            output.Append(']');
        }

        private static void WriteString(string text, StringBuilder output)
        {
            output.Append('"');
            foreach (char character in text)
            {
                if (character == '"' || character == '\\')
                {
                    output.Append('\\').Append(character);
                }
                else if (character < ' ')
                {
                    // Control characters fit in one byte: \u00 followed by two hex digits.
                    output.Append("\\u00").Append(HexDigits[character >> 4]).Append(HexDigits[character & 0xF]);
                }
                else
                {
                    output.Append(character);
                }
            }

            output.Append('"');
        }
    }
}

using System.Globalization;

namespace PuzzleCore.Json
{
    /// <summary>
    /// Builds the paths that problems are reported at, in the usual "$.a.b[2]" notation.
    /// A member whose name is not plain (letters, digits, '_' and '-') is written as
    /// $["name"] with the name in canonical JSON form, so every path is unambiguous.
    /// </summary>
    public static class JsonPath
    {
        public const string Root = "$";

        public static string Member(string parent, string name) =>
            IsPlain(name) ? parent + "." + name : parent + "[" + JsonWriter.Canonical(new JsonString(name)) + "]";

        public static string Index(string parent, int index) =>
            parent + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

        private static bool IsPlain(string name)
        {
            if (name.Length == 0)
            {
                return false;
            }

            foreach (char character in name)
            {
                bool isPlain = (character >= 'a' && character <= 'z') || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9') || character == '_' || character == '-';
                if (!isPlain)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

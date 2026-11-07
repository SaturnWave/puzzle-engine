using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PuzzleCore.Jam
{
    /// <summary>What is wrong with a pc-lot text. Closed set; new problems are added at the end.</summary>
    public enum LotTextProblem
    {
        WrongHeader,
        UnsupportedRulesVersion,
        MissingOrMisplacedLine,
        BadNumber,
        BadSize,
        BadVehicle,
        BadColour,
        BadCover,
        BadClue,
        TrailingText,
    }

    /// <summary>Read(definition) or Malformed(line, problem); lines count from 1.</summary>
    public abstract record LotTextResult
    {
        private LotTextResult()
        {
        }

        public sealed record Read(LotDefinition Definition) : LotTextResult;

        public sealed record Malformed(int Line, LotTextProblem Problem) : LotTextResult;
    }

    /// <summary>
    /// The pc-lot level text (CoveredLot.md, "Generator"): UTF-8, '\n' line ends, no trailing
    /// newline, eight lines in a fixed order. Vehicles are colour:anchor:length:facing in PieceId
    /// order; covers and pillars ascending; clues Not(p,C), Ahead(p,Same|Differs), Touch(p,C,n).
    /// A public contract: it changes only by addition, with a new rules version.
    /// <code>
    /// pc-lot
    /// r=1
    /// size=6x6
    /// bays=4
    /// pillars=4
    /// vehicles=B:1:2:N;R:8:2:E;...
    /// covers=3,7
    /// clues=Not(3,R)
    /// </code>
    /// </summary>
    public static class LotText
    {
        public const string Header = "pc-lot";
        public const int RulesVersion = 1;

        /// <summary>Colour letters by ColourId: 1 = R, 2 = B, 3 = Y, 4 = G, 5 = P, 6 = O. Fixed forever.</summary>
        public const string ColourLetters = "RBYGPO";

        private static readonly string[] Keys = { "r", "size", "bays", "pillars", "vehicles", "covers", "clues" };

        /// <summary>The canonical text of a loaded level.</summary>
        public static string Write(LotLevel level)
        {
            var pillars = new List<string>();
            for (int cell = 0; cell <= LotCell.MaxIndex; cell++)
            {
                if ((level.Pillars & (1UL << cell)) != 0)
                {
                    pillars.Add(Number(cell));
                }
            }

            var vehicles = new List<string>();
            var covers = new List<string>();
            foreach (Vehicle vehicle in level.Vehicles)
            {
                vehicles.Add(string.Concat(Letter(vehicle.Colour), ":", Number(vehicle.Anchor.Index), ":", Number((int)vehicle.Length), ":", FacingLetter(vehicle.Facing)));
                if (vehicle.StartsCovered)
                {
                    covers.Add(Number(vehicle.Id.Value));
                }
            }

            IEnumerable<string> clues = level.Clues
                .OrderBy(clue => clue.On.Value)
                .ThenBy(ClueFamilyOrder)
                .Select(ClueText);

            return string.Join("\n", new[]
            {
                Header,
                "r=" + Number(RulesVersion),
                "size=" + level.Size.ToString(),
                "bays=" + level.Bays.ToString(),
                "pillars=" + string.Join(",", pillars),
                "vehicles=" + string.Join(";", vehicles),
                "covers=" + string.Join(",", covers),
                "clues=" + string.Join(";", clues),
            });
        }

        /// <summary>Lower-case hex SHA-256 of the canonical text's UTF-8 bytes: the level fingerprint.</summary>
        public static string Fingerprint(LotLevel level)
        {
            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(Write(level)));
            }

            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest)
            {
                hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }

        /// <summary>Parses the text strictly. The definition still has to pass <see cref="LotLoader"/>.</summary>
        public static LotTextResult Read(string text)
        {
            string[] lines = text.Split('\n');
            if (lines.Length == 0 || lines[0] != Header)
            {
                return Malformed(1, LotTextProblem.WrongHeader);
            }

            if (lines.Length > Keys.Length + 1)
            {
                return Malformed(Keys.Length + 2, LotTextProblem.TrailingText);
            }

            var values = new string[Keys.Length];
            for (int index = 0; index < Keys.Length; index++)
            {
                string prefix = Keys[index] + "=";
                bool isPresent = index + 1 < lines.Length && lines[index + 1].StartsWith(prefix, StringComparison.Ordinal);
                if (!isPresent)
                {
                    return Malformed(index + 2, LotTextProblem.MissingOrMisplacedLine);
                }

                values[index] = lines[index + 1].Substring(prefix.Length);
            }

            if (values[0] != Number(RulesVersion))
            {
                return Malformed(2, LotTextProblem.UnsupportedRulesVersion);
            }

            return Parse(values);
        }

        private static LotTextResult Parse(string[] values)
        {
            string[] size = values[1].Split('x');
            if (size.Length != 2 || !TryNumber(size[0], out int width) || !TryNumber(size[1], out int height))
            {
                return Malformed(3, LotTextProblem.BadSize);
            }

            if (!TryNumber(values[2], out int bays))
            {
                return Malformed(4, LotTextProblem.BadNumber);
            }

            var pillars = new List<int>();
            foreach (string item in Items(values[3], ','))
            {
                if (!TryNumber(item, out int cell))
                {
                    return Malformed(5, LotTextProblem.BadNumber);
                }

                pillars.Add(cell);
            }

            // Covers are listed strictly ascending (canonical order), which also rules out duplicates.
            var covered = new HashSet<int>();
            int highestCover = -1;
            foreach (string item in Items(values[5], ','))
            {
                if (!TryNumber(item, out int piece) || piece <= highestCover)
                {
                    return Malformed(7, LotTextProblem.BadCover);
                }

                covered.Add(piece);
                highestCover = piece;
            }

            var vehicles = new List<VehicleSpec>();
            foreach (string item in Items(values[4], ';'))
            {
                if (!TryVehicle(item, covered.Contains(vehicles.Count), out VehicleSpec? vehicle))
                {
                    return Malformed(6, LotTextProblem.BadVehicle);
                }

                vehicles.Add(vehicle);
            }

            if (highestCover >= vehicles.Count)
            {
                return Malformed(7, LotTextProblem.BadCover);
            }

            var clues = new List<LotClue>();
            foreach (string item in Items(values[6], ';'))
            {
                if (!TryClue(item, out LotClue? clue))
                {
                    return Malformed(8, LotTextProblem.BadClue);
                }

                clues.Add(clue);
            }

            return new LotTextResult.Read(new LotDefinition(
                width, height, bays, ReadOnlyArray.CopyOf(pillars), ReadOnlyArray.CopyOf(vehicles), ReadOnlyArray.CopyOf(clues)));
        }

        private static bool TryVehicle(string text, bool isCovered, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VehicleSpec? vehicle)
        {
            vehicle = null;
            string[] parts = text.Split(':');
            if (parts.Length != 4)
            {
                return false;
            }

            bool isWellFormed = TryColour(parts[0], out ColourId colour)
                & TryNumber(parts[1], out int anchor)
                & TryNumber(parts[2], out int length)
                & TryFacing(parts[3], out Facing facing);
            if (!isWellFormed || (length != 2 && length != 3))
            {
                return false;
            }

            vehicle = new VehicleSpec(colour, anchor, (VehicleLength)length, facing, isCovered);
            return true;
        }

        private static bool TryClue(string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LotClue? clue)
        {
            clue = null;
            int open = text.IndexOf('(', StringComparison.Ordinal);
            bool isWrapped = open > 0 && text[text.Length - 1] == ')';
            if (!isWrapped)
            {
                return false;
            }

            string family = text.Substring(0, open);
            string[] arguments = text.Substring(open + 1, text.Length - open - 2).Split(',');
            if (arguments.Length < 2 || !TryNumber(arguments[0], out int on) || on > PieceId.MaxValue)
            {
                return false;
            }

            var piece = new PieceId((byte)on);
            switch (family)
            {
                case "Not" when arguments.Length == 2 && TryColour(arguments[1], out ColourId notColour):
                    clue = new LotClue.Not(piece, notColour);
                    return true;
                case "Ahead" when arguments.Length == 2 && (arguments[1] == "Same" || arguments[1] == "Differs"):
                    clue = new LotClue.Ahead(piece, arguments[1] == "Same" ? AheadRelation.Same : AheadRelation.Differs);
                    return true;
                case "Touch" when arguments.Length == 3 && TryColour(arguments[1], out ColourId touchColour)
                                  && TryNumber(arguments[2], out int count) && count <= byte.MaxValue:
                    clue = new LotClue.Touch(piece, touchColour, (byte)count);
                    return true;
                default:
                    return false;
            }
        }

        private static string ClueText(LotClue clue) => clue switch
        {
            LotClue.Not not => string.Concat("Not(", Number(not.On.Value), ",", Letter(not.Colour), ")"),
            LotClue.Ahead ahead => string.Concat("Ahead(", Number(ahead.On.Value), ",", ahead.Relation == AheadRelation.Same ? "Same" : "Differs", ")"),
            LotClue.Touch touch => string.Concat("Touch(", Number(touch.On.Value), ",", Letter(touch.Colour), ",", Number(touch.Count) + ")"),
            _ => throw new ArgumentOutOfRangeException(nameof(clue), "Unknown clue family."),
        };

        /// <summary>Clue order inside one vehicle's tags: family, then its colour or relation, then count.</summary>
        private static int ClueFamilyOrder(LotClue clue) => clue switch
        {
            LotClue.Not not => 0 + not.Colour.Value,
            LotClue.Ahead ahead => 100 + (int)ahead.Relation,
            LotClue.Touch touch => 200 + (touch.Colour.Value * 30) + touch.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(clue), "Unknown clue family."),
        };

        private static IEnumerable<string> Items(string text, char separator) =>
            text.Length == 0 ? Array.Empty<string>() : text.Split(separator);

        private static bool TryColour(string text, out ColourId colour)
        {
            colour = default;
            int index = text.Length == 1 ? ColourLetters.IndexOf(text[0], StringComparison.Ordinal) : -1;
            if (index < 0)
            {
                return false;
            }

            colour = new ColourId((byte)(index + 1));
            return true;
        }

        private static bool TryFacing(string text, out Facing facing)
        {
            switch (text)
            {
                case "N":
                    facing = Facing.North;
                    return true;
                case "E":
                    facing = Facing.East;
                    return true;
                case "S":
                    facing = Facing.South;
                    return true;
                case "W":
                    facing = Facing.West;
                    return true;
                default:
                    facing = default;
                    return false;
            }
        }

        /// <summary>A plain decimal 0-999: digits only, no sign, no leading zero.</summary>
        private static bool TryNumber(string text, out int value)
        {
            value = 0;
            bool isPlain = text.Length > 0 && text.Length <= 3 && (text.Length == 1 || text[0] != '0');
            if (!isPlain)
            {
                return false;
            }

            foreach (char digit in text)
            {
                if (digit < '0' || digit > '9')
                {
                    return false;
                }

                value = (value * 10) + (digit - '0');
            }

            return true;
        }

        private static string Letter(ColourId colour)
        {
            if (colour.Value > ColourLetters.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(colour), colour.Value, "pc-lot r=1 has letters for colours 1-6 only.");
            }

            return ColourLetters[colour.Value - 1].ToString();
        }

        private static string FacingLetter(Facing facing) => facing switch
        {
            Facing.North => "N",
            Facing.East => "E",
            Facing.South => "S",
            Facing.West => "W",
            _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "Unknown facing."),
        };

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static LotTextResult Malformed(int line, LotTextProblem problem) => new LotTextResult.Malformed(line, problem);
    }
}

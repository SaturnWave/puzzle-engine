using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PuzzleCore.Jam;
using PuzzleCore.Rules;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "demo": play the brief's 6×6 illustration board in the terminal. Type a piece number to tap
    /// it, "r" to restart, "q" to quit. A text renderer for rules checks, not the game client.
    /// </summary>
    internal static class DemoCommand
    {
        private static readonly string ColourLetters = "?RBYGPO";

        public static int Run(TextReader input, TextWriter output)
        {
            if (!(LotLoader.Load(DemoBoards.SixBySix()) is LoadResult.Loaded loaded))
            {
                output.Write("The demo board did not load.\n");
                return 1;
            }

            var rules = new JamRules(loaded.Level);
            LotState state = rules.Start;
            int peak = 0;
            output.Write("Covered Lot demo. Tap a vehicle by its number. r = restart, q = quit.\n");
            while (true)
            {
                output.Write(Render(rules, state, peak));
                Outcome outcome = rules.Evaluate(state);
                if (!(outcome is Outcome.InPlay))
                {
                    output.Write(outcome is Outcome.Won
                        ? "Lot cleared. Most bays used: " + Number(peak) + " (par 3).\n"
                        : "All bays are full. Type r to try again.\n");
                }

                output.Write("> ");
                string? line = input.ReadLine();
                if (line is null || line.Trim() == "q")
                {
                    return 0;
                }

                if (line.Trim() == "r")
                {
                    state = rules.Start;
                    peak = 0;
                    continue;
                }

                if (!byte.TryParse(line.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out byte piece) || piece > PieceId.MaxValue)
                {
                    output.Write("Type a piece number, r or q.\n");
                    continue;
                }

                switch (rules.TryApply(state, new Exit(new PieceId(piece))))
                {
                    case MoveResult<LotState, ExitRejection>.Applied applied:
                        peak = Math.Max(peak, state.OccupiedSlots + 1);
                        output.Write(Describe(applied.Events));
                        state = applied.Next;
                        break;
                    case MoveResult<LotState, ExitRejection>.Illegal illegal:
                        output.Write(Explain(illegal.Reason));
                        break;
                }
            }
        }

        private static string Render(JamRules rules, LotState state, int peak)
        {
            LotLevel level = rules.Level;
            var text = new StringBuilder();
            text.Append('\n');
            for (int row = 0; row < level.Size.Height; row++)
            {
                for (int column = 0; column < level.Size.Width; column++)
                {
                    LotCell cell = level.Size.CellAt(new Row((byte)row), new Column((byte)column));
                    text.Append(CellText(rules, state, cell));
                }

                text.Append('\n');
            }

            text.Append("\nBays: ");
            for (int slot = 0; slot < state.Capacity.Value; slot++)
            {
                Option<ColourId> colour = state.ColourIn(new SlotIndex((byte)slot));
                text.Append(colour.TryGet(out ColourId value) ? "[" + ColourLetters[value.Value] + "]" : "[ ]");
            }

            text.Append("   free: ").Append(Number(state.FreeSlots)).Append("   peak so far: ").Append(Number(peak)).Append('\n');
            text.Append(Ledger(rules, state));
            return text.ToString();
        }

        private static string CellText(JamRules rules, LotState state, LotCell cell)
        {
            if ((rules.Level.Pillars & cell.Bit) != 0)
            {
                return "  ## ";
            }

            if (!rules.Level.PieceAt(cell).TryGet(out PieceId piece) || state.HasExited(piece))
            {
                return "  .  ";
            }

            Vehicle vehicle = rules.Level.VehicleOf(piece);
            char colour = rules.IsCovered(state, piece) ? '?' : ColourLetters[vehicle.Colour.Value];
            char arrow = vehicle.Facing switch
            {
                Facing.North => '^',
                Facing.East => '>',
                Facing.South => 'v',
                _ => '<',
            };
            return " " + Number(piece.Value).PadLeft(2) + colour + arrow;
        }

        private static string Ledger(JamRules rules, LotState state)
        {
            var counts = new int[ColourId.MaxValue + 1];
            var tags = new StringBuilder();
            foreach (Vehicle vehicle in rules.Level.Vehicles)
            {
                if (rules.IsCovered(state, vehicle.Id))
                {
                    counts[vehicle.Colour.Value]++;
                }
            }

            foreach (LotClue clue in rules.Level.Clues)
            {
                if (rules.IsCovered(state, clue.On) && clue is LotClue.Not not)
                {
                    tags.Append("   tag on ").Append(Number(clue.On.Value)).Append(": not ").Append(ColourLetters[not.Colour.Value]);
                }
            }

            if (!rules.Level.IsCoveredLevel)
            {
                return string.Empty;
            }

            return "Ledger (still covered): R " + Number(counts[1]) + " · B " + Number(counts[2]) + " · Y " + Number(counts[3]) + tags + "\n";
        }

        private static string Describe(ReadOnlyArray<PuzzleEvent> events)
        {
            var text = new StringBuilder();
            foreach (PuzzleEvent item in events)
            {
                string line = item switch
                {
                    PuzzleEvent.Exited exited => "Vehicle " + Number(exited.Piece.Value) + " parks in bay " + Number(exited.Slot.Value + 1) + ".",
                    PuzzleEvent.CoverLifted lifted => "The cover lifts: it is " + ColourLetters[lifted.Colour.Value] + ".",
                    PuzzleEvent.SetCleared cleared => "Three " + ColourLetters[cleared.Colour.Value] + " drive off together.",
                    _ => string.Empty,
                };
                if (line.Length > 0)
                {
                    text.Append(line).Append('\n');
                }
            }

            return text.ToString();
        }

        private static string Explain(ExitRejection reason) => reason switch
        {
            ExitRejection.Blocked { By: Blocker.Piece blocker } => "Bump. Vehicle " + Number(blocker.Id.Value) + " is in the way.\n",
            ExitRejection.Blocked => "Bump. A pillar is in the way.\n",
            ExitRejection.NotInLot => "That vehicle is not in the lot.\n",
            _ => "The level is over. Type r to restart.\n",
        };

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The brief's 6×6 illustration board (CoveredLot.md, "The game in 30 seconds").</summary>
    internal static class DemoBoards
    {
        public static LotDefinition SixBySix()
        {
            var red = new ColourId(1);
            var blue = new ColourId(2);
            var yellow = new ColourId(3);
            var vehicles = new List<VehicleSpec>
            {
                Car(blue, 1, Facing.North),
                Car(red, 8, Facing.East),
                Car(yellow, 12, Facing.East),
                Car(blue, 14, Facing.South, covered: true),
                Car(blue, 16, Facing.East),
                Car(yellow, 23, Facing.North),
                Car(red, 24, Facing.North),
                Car(red, 27, Facing.West, covered: true),
                Car(yellow, 33, Facing.East),
            };
            return new LotDefinition(6, 6, 4, ReadOnlyArray.Of(4), ReadOnlyArray.CopyOf(vehicles),
                ReadOnlyArray.Of<LotClue>(new LotClue.Not(new PieceId(3), red)));
        }

        private static VehicleSpec Car(ColourId colour, int anchor, Facing facing, bool covered = false) =>
            new VehicleSpec(colour, anchor, VehicleLength.Two, facing, covered);
    }
}

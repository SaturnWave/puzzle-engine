using System;
using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// A Planner's guesses at the covers still hidden (Q71): the arrangements that give each cover one of the colours its
    /// reading leaves it, with the ledger's counts exactly. They are ordered by cover and then by colour, both ascending, so a
    /// drawn index names the same arrangement on every machine. On a certified level the truth is one of them.
    /// </summary>
    public static class CoverGuesses
    {
        /// <summary>How many arrangements fit the reading.</summary>
        public static int Count(CoverReading reading) => CountFrom(reading.Covers, 0, LedgerOf(reading));

        /// <summary>The arrangement at <paramref name="index"/> in the order: one colour per cover, in the reading's order of covers.</summary>
        public static ReadOnlyArray<ColourId> At(CoverReading reading, int index)
        {
            int count = Count(reading);
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "There is no arrangement at that index.");
            }

            int[] left = LedgerOf(reading);
            var colours = new ColourId[reading.Covers.Count];
            int rest = index;
            for (int position = 0; position < colours.Length; position++)
            {
                foreach (ColourId colour in reading.Covers[position].Colours)
                {
                    if (left[colour.Value] == 0)
                    {
                        continue;
                    }

                    left[colour.Value]--;
                    int completions = CountFrom(reading.Covers, position + 1, left);
                    if (rest < completions)
                    {
                        colours[position] = colour;
                        break;
                    }

                    rest -= completions;
                    left[colour.Value]++;
                }
            }

            return ReadOnlyArray.Of(colours);
        }

        /// <summary>
        /// One arrangement, drawn uniformly from the run's Bot stream when two or more fit, and the only one without a draw
        /// otherwise (TC23).
        /// </summary>
        public static ReadOnlyArray<ColourId> Draw(CoverReading reading, Pcg32 random)
        {
            int count = Count(reading);
            if (count == 0)
            {
                throw new InvalidOperationException("No arrangement fits the reading, though the truth always does.");
            }

            return At(reading, count == 1 ? 0 : random.NextIndex(count));
        }

        /// <summary>
        /// Whether the colours, one per cover in the reading's order, still fit it: each in its cover's set, and as many of
        /// each colour as the ledger counts.
        /// </summary>
        public static bool Fits(CoverReading reading, IReadOnlyList<ColourId> colours)
        {
            if (colours.Count != reading.Covers.Count)
            {
                throw new ArgumentException("One colour per cover still hidden.", nameof(colours));
            }

            int[] left = LedgerOf(reading);
            for (int position = 0; position < colours.Count; position++)
            {
                ColourId colour = colours[position];
                if (!Contains(reading.Covers[position].Colours, colour) || left[colour.Value] == 0)
                {
                    return false;
                }

                left[colour.Value]--;
            }

            return true;
        }

        /// <summary>
        /// The arrangements that give the covers from <paramref name="position"/> on a colour each from what is
        /// <paramref name="left"/> in the ledger. The ledger counts exactly the covers still hidden, so every complete
        /// arrangement uses up its counts.
        /// </summary>
        private static int CountFrom(ReadOnlyArray<CoverColours> covers, int position, int[] left)
        {
            if (position == covers.Count)
            {
                return 1;
            }

            int count = 0;
            foreach (ColourId colour in covers[position].Colours)
            {
                if (left[colour.Value] == 0)
                {
                    continue;
                }

                left[colour.Value]--;
                count += CountFrom(covers, position + 1, left);
                left[colour.Value]++;
            }

            return count;
        }

        private static int[] LedgerOf(CoverReading reading)
        {
            var ledger = new int[ColourId.MaxValue + 1];
            for (byte colour = ColourId.MinValue; colour <= ColourId.MaxValue; colour++)
            {
                ledger[colour] = reading.LedgerCount(new ColourId(colour));
            }

            return ledger;
        }

        private static bool Contains(ReadOnlyArray<ColourId> colours, ColourId colour)
        {
            foreach (ColourId candidate in colours)
            {
                if (candidate == colour)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

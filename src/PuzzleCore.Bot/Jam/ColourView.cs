using System;
using PuzzleCore.Jam;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// The colours a Planner believes (TC21): a vehicle never covered shows its own colour, a cover that has lifted the
    /// colour seen when it lifted, and a cover still hidden the bot's guess. It is the only Planner code that reads a
    /// vehicle's own colour, and it reads it only for vehicles never covered.
    /// </summary>
    public sealed class ColourView
    {
        private const byte NoColour = 0;

        // Per vehicle, the colour the bot believes it has; NoColour for a cover still hidden that has no guess yet.
        private readonly byte[] _colours;
        private uint _hidden;

        public ColourView(LotLevel level)
        {
            _colours = new byte[level.VehicleCount];
            foreach (Vehicle vehicle in level.Vehicles)
            {
                if (!vehicle.StartsCovered)
                {
                    _colours[vehicle.Id.Value] = vehicle.Colour.Value;
                }
            }

            _hidden = level.CoveredAtStartMask;
        }

        /// <summary>The colour the bot believes the vehicle has.</summary>
        public ColourId Of(PieceId piece)
        {
            byte colour = _colours[piece.Value];
            return colour != NoColour
                ? new ColourId(colour)
                : throw new InvalidOperationException("A cover still hidden has no guess yet.");
        }

        /// <summary>The cover lifted and showed its colour.</summary>
        public void Seen(PieceId cover, ColourId colour)
        {
            RequireHidden(cover);
            _hidden &= ~(1U << cover.Value);
            _colours[cover.Value] = colour.Value;
        }

        /// <summary>The bot's guess for a cover still hidden.</summary>
        public void Guess(PieceId cover, ColourId colour)
        {
            RequireHidden(cover);
            _colours[cover.Value] = colour.Value;
        }

        private void RequireHidden(PieceId piece)
        {
            if ((_hidden & (1U << piece.Value)) == 0)
            {
                throw new InvalidOperationException("Only a cover still hidden can be guessed or seen.");
            }
        }
    }
}

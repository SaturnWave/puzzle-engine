using System;
using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;

namespace PuzzleCore.Bot.Jam
{
    /// <summary>
    /// Planner1 and Planner3 (TC12, Q71-Q73, Q79). Before a move it reads the covers still hidden up to its ceiling
    /// (<see cref="LotClues.Read"/>), guesses those its reading leaves open (<see cref="CoverGuesses"/>), and looks ahead on
    /// the colours it believes (<see cref="Lookahead"/>), drawing among the best first moves. The guess is drawn at the start
    /// and kept while it fits what the bot reads; when a lifted cover breaks it, every cover still hidden is drawn again,
    /// before the move's other draws (TC23). It reads colours only through its <see cref="ColourView"/>.
    /// </summary>
    public sealed class PlannerPolicy : LotBotPolicy
    {
        private readonly LotLevel _level;
        private readonly LotModel _model;
        private readonly MoveCount _plies;
        private readonly DeductionRung _ceiling;
        private readonly ColourView _colours;
        private readonly List<Exit> _best = new List<Exit>();
        private SeenSet _seen;

        // What the bot read before its last move; None at the start and after a lift, until it reads again.
        private Option<CoverReading> _reading = Option.None<CoverReading>();
        private bool _hasGuessed;
        private int _guessesRight;
        private int _guessesWrong;

        public PlannerPolicy(LotLevel level, MoveCount plies, DeductionRung ceiling)
        {
            if (plies.Value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(plies), plies.Value, "A Planner looks at least one move ahead.");
            }

            if (ceiling is not (DeductionRung.D1 or DeductionRung.D2))
            {
                throw new ArgumentOutOfRangeException(nameof(ceiling), ceiling, "A Planner reads covers at D1 or D2.");
            }

            _level = level;
            _model = new LotModel(level);
            _plies = plies;
            _ceiling = ceiling;
            _colours = new ColourView(level);
        }

        /// <summary>How many moves ahead it looks: Planner1 one, Planner3 three.</summary>
        public MoveCount LookaheadPlies => _plies;

        /// <summary>The highest rung it reads covers at: Planner1 D1, Planner3 D2.</summary>
        public DeductionRung Ceiling => _ceiling;

        public override Option<GuessTally> Guesses => Option.Some(new GuessTally(_guessesRight, _guessesWrong));

        /// <summary>The colour the bot believes the vehicle has; a cover still hidden has a guess only once the bot has moved.</summary>
        public ColourId Believes(PieceId piece) => _colours.Of(piece);

        /// <summary>A lifted cover is tallied against the guess, then counted as seen; the bot reads again before its next move.</summary>
        public override void Observe(ReadOnlyArray<PuzzleEvent> events)
        {
            foreach (PuzzleEvent happened in events)
            {
                if (happened is PuzzleEvent.CoverLifted lifted)
                {
                    TallyGuess(lifted);
                    _seen = _seen.With(new HiddenCell(lifted.Piece.Value));
                    _colours.Seen(lifted.Piece, lifted.Colour);
                    _reading = Option.None<CoverReading>();
                }
            }
        }

        public override Exit Intend(LotState state, IReadOnlyList<Exit> legal, Pcg32 random)
        {
            if (!_reading.HasValue)
            {
                ReadAndGuess(random);
            }

            Lookahead.BestFirstMoves(_model, _colours, LotPosition.Of(state), _plies, legal, _best);
            return _best.Count == 1 ? _best[0] : _best[random.NextIndex(_best.Count)];
        }

        /// <summary>Reads the covers still hidden, and draws a guess when there is none yet or the reading no longer fits it.</summary>
        private void ReadAndGuess(Pcg32 random)
        {
            CoverReading reading = LotClues.Read(_level, _seen, _ceiling);
            if (!_hasGuessed || !CoverGuesses.Fits(reading, Believed(reading)))
            {
                ReadOnlyArray<ColourId> guess = CoverGuesses.Draw(reading, random);
                for (int position = 0; position < guess.Count; position++)
                {
                    _colours.Guess(reading.Covers[position].Cover, guess[position]);
                }

                _hasGuessed = true;
            }

            _reading = Option.Some(reading);
        }

        private List<ColourId> Believed(CoverReading reading)
        {
            var colours = new List<ColourId>(reading.Covers.Count);
            foreach (CoverColours cover in reading.Covers)
            {
                colours.Add(_colours.Of(cover.Cover));
            }

            return colours;
        }

        /// <summary>A cover the last reading left two or more colours was a guess: right when it showed the colour believed.</summary>
        private void TallyGuess(PuzzleEvent.CoverLifted lifted)
        {
            if (!_reading.TryGet(out CoverReading? reading))
            {
                throw new InvalidOperationException("A cover lifted before the bot had read the covers.");
            }

            foreach (CoverColours cover in reading.Covers)
            {
                if (cover.Cover != lifted.Piece)
                {
                    continue;
                }

                if (cover.Colours.Count >= 2)
                {
                    bool isRight = _colours.Of(lifted.Piece) == lifted.Colour;
                    _guessesRight += isRight ? 1 : 0;
                    _guessesWrong += isRight ? 0 : 1;
                }

                return;
            }

            throw new InvalidOperationException("A cover lifted that the bot's reading did not list as hidden.");
        }
    }
}

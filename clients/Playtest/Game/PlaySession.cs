using System;
using System.Collections.Generic;
using System.Globalization;
using PuzzleCore;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Records;
using PuzzleCore.Rules;

namespace CoveredLot.Playtest.Game
{
    /// <summary>What the fail screen may offer, decided by the solver with full information.</summary>
    public sealed record FailOffers(bool SpareCanWin, bool RewindCanWin);

    /// <summary>
    /// One player's attempt at one level, for the playtest UI. Every rule comes from the engine
    /// (<see cref="Attempt{TState, TMove, TRejection}"/> over <see cref="JamRules"/>); this class only
    /// keeps what the screen shows: the peak, the last message, the last bump and the offers.
    /// The +1 bay and rewind are free here (test version), but appear only when the solver says they can still win.
    /// </summary>
    public sealed class PlaySession
    {
        private static readonly NodeBudget OfferBudget = new NodeBudget(500_000);

        private Attempt<LotState, Exit, ExitRejection> _attempt;

        public PlaySession(PlaytestLevel level)
        {
            Level = level;
            Rules = new JamRules(level.Level);
            _attempt = NewAttempt();
        }

        public PlaytestLevel Level { get; }

        public JamRules Rules { get; }

        public LotState State => _attempt.Current;

        public Outcome Outcome => _attempt.Outcome;

        public bool SpareUsed => _attempt.SpareUsed;

        /// <summary>The most bays in use at once in this attempt, counted when a vehicle parks.</summary>
        public int Peak { get; private set; }

        public int Retries { get; private set; }

        public string Message { get; private set; } = "Tap a vehicle. It drives straight out if nothing is in its way.";

        /// <summary>The vehicle that just left, drawn once with a drive-out animation.</summary>
        public Option<PieceId> JustLeft { get; private set; }

        /// <summary>The vehicle whose tap just bumped, and what blocked it.</summary>
        public Option<PieceId> Bumped { get; private set; }

        public Option<Blocker> BumpedBy { get; private set; }

        /// <summary>The covers seen in this attempt: lifted, or shown by Insight. The set only grows within the attempt.</summary>
        public SeenSet Seen => _attempt.Seen;

        public FailOffers Offers { get; private set; } = new FailOffers(false, false);

        /// <summary>The longest rewind the fail screen's picker offers (the engine's per-game limit).</summary>
        public int RewindLimit => _attempt.RewindLimit.Value;

        /// <summary>★★★ at peak par, ★★ one above, ★ for any other win.</summary>
        public int Stars => Peak <= Level.PeakPar.Value ? 3 : Peak == Level.PeakPar.Value + 1 ? 2 : 1;

        public void Tap(PieceId piece)
        {
            ForgetLastTap();
            int occupiedBefore = State.OccupiedSlots;
            switch (_attempt.Move(new Exit(piece)))
            {
                case StepResult<ExitRejection>.Done done:
                    Peak = Math.Max(Peak, occupiedBefore + 1);
                    JustLeft = Option.Some(piece);
                    Message = Describe(done.Events);
                    AfterMove();
                    break;
                case StepResult<ExitRejection>.Illegal { Reason: ExitRejection.Blocked blocked }:
                    Bumped = Option.Some(piece);
                    BumpedBy = Option.Some(blocked.By);
                    Message = blocked.By is Blocker.Piece ? "Bump. Something is in its way." : "Bump. A pillar is in its way.";
                    break;
                case StepResult<ExitRejection>.Illegal:
                    Message = "That vehicle can't move now.";
                    break;
                case StepResult<ExitRejection>.Refused refused:
                    Message = "Not allowed: " + refused.Reason.ToString();
                    break;
            }
        }

        public void TakeSpare()
        {
            if (_attempt.TakeSpare() is StepResult<ExitRejection>.Done)
            {
                Message = "One more bay. It stays for the rest of this attempt.";
                Offers = new FailOffers(false, false);
            }
        }

        public void Rewind(int moves)
        {
            if (_attempt.Rewind(new MoveCount(moves)) is StepResult<ExitRejection>.Done)
            {
                // The last tapped car is back in the lot now; drawn as leaving, it would stay invisible.
                ForgetLastTap();
                Message = "Rewound " + moves.ToString(CultureInfo.InvariantCulture) + (moves == 1 ? " move." : " moves.");
                Offers = new FailOffers(false, false);
            }
        }

        public void Retry()
        {
            _attempt = NewAttempt();
            Peak = 0;
            Retries++;
            ForgetLastTap();
            Offers = new FailOffers(false, false);
            Message = "Fresh start.";
        }

        public bool IsCovered(PieceId piece) => Rules.IsCovered(State, piece) && !Seen.Contains(new HiddenCell(piece.Value));

        /// <summary>For each colour, how many covered vehicles of it are still in the lot (always shown on covered levels).</summary>
        public IReadOnlyList<(ColourId Colour, int Count)> Ledger()
        {
            var counts = new int[ColourId.MaxValue + 1];
            foreach (Vehicle vehicle in Level.Level.Vehicles)
            {
                if (Rules.IsCovered(State, vehicle.Id))
                {
                    counts[vehicle.Colour.Value]++;
                }
            }

            var ledger = new List<(ColourId, int)>();
            foreach (Vehicle vehicle in Level.Level.Vehicles)
            {
                bool isListed = ledger.Exists(entry => entry.Item1 == vehicle.Colour);
                if (!isListed)
                {
                    ledger.Add((vehicle.Colour, counts[vehicle.Colour.Value]));
                }
            }

            ledger.Sort((left, right) => left.Item1.Value.CompareTo(right.Item1.Value));
            return ledger;
        }

        private void AfterMove()
        {
            if (Outcome is Outcome.Won)
            {
                Message = "Lot cleared!";
            }
            else if (Outcome is Outcome.Failed)
            {
                Message = "All bays are full.";
                Offers = ComputeOffers();
            }
        }

        /// <summary>
        /// The engine's fail analysis decides both offers: +1 bay on a near miss (the spare unused and
        /// one more bay wins), rewind when some allowed rewind reaches a winnable position. A budget
        /// overrun is Unknown and offers nothing.
        /// </summary>
        private FailOffers ComputeOffers()
        {
            var solver = new LotPositionSolver(Level.Level);
            NearMiss nearMiss = FailAnalysis.NearMissOf(_attempt, solver, OfferBudget);
            RewindReach reach = FailAnalysis.RewindReachOf(_attempt, solver, OfferBudget);
            return new FailOffers(nearMiss is NearMiss.Yes, reach == RewindReach.Winnable);
        }

        private Attempt<LotState, Exit, ExitRejection> NewAttempt() =>
            new Attempt<LotState, Exit, ExitRejection>(Rules, Rules.Start, PlayMode.Campaign);

        /// <summary>Clears what the last tap showed: the car leaving, and the bumped car with its blocker.</summary>
        private void ForgetLastTap()
        {
            JustLeft = Option.None<PieceId>();
            Bumped = Option.None<PieceId>();
            BumpedBy = Option.None<Blocker>();
        }

        private static string Describe(ReadOnlyArray<PuzzleEvent> events)
        {
            var parts = new List<string>();
            foreach (PuzzleEvent item in events)
            {
                switch (item)
                {
                    case PuzzleEvent.CoverLifted lifted:
                        parts.Add("The cover lifts: it was " + Palette.Name(lifted.Colour) + ".");
                        break;
                    case PuzzleEvent.SetCleared cleared:
                        parts.Add("Three " + Palette.Name(cleared.Colour) + " drive off together.");
                        break;
                }
            }

            return parts.Count == 0 ? "Parked." : string.Join(" ", parts);
        }
    }

    /// <summary>The playtest palette: Okabe-Ito colours (colour-blind safe), each with its letter from pc-lot.</summary>
    public static class Palette
    {
        private static readonly string[] Hex = { "#D55E00", "#0072B2", "#F0E442", "#009E73", "#CC79A7", "#E69F00" };
        private static readonly string[] Names = { "red", "blue", "yellow", "green", "pink", "orange" };

        public static string Css(ColourId colour) => Hex[colour.Value - 1];

        public static string Letter(ColourId colour) => LotText.ColourLetters[colour.Value - 1].ToString();

        public static string Name(ColourId colour) => Names[colour.Value - 1];

        /// <summary>Dark text on the light colours (yellow, orange, pink), white on the rest.</summary>
        public static string Ink(ColourId colour) => colour.Value is 3 or 5 or 6 ? "#1d1d1d" : "#ffffff";
    }
}

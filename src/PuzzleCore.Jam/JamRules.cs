using System;
using System.Collections.Generic;
using PuzzleCore.Rules;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// The Covered Lot rules, r=1. A tapped vehicle leaves only if every cell from its front to the
    /// lot edge is free; it parks in the leftmost free bay. Sets clear by colour count, whatever
    /// the bay order: three of one colour in the bays leave together and the rest close up to the
    /// left. Won when the lot is empty; Failed(SlotsFull) when every bay is full after clears.
    /// </summary>
    public sealed class JamRules : IPuzzleRules<LotState, Exit, ExitRejection>
    {
        public const int SetSize = 3;

        public JamRules(LotLevel level) => Level = level;

        public LotLevel Level { get; }

        public LotState Start => LotState.Start(Level);

        public void AppendLegalMoves(in LotState state, List<Exit> into)
        {
            if (!(Evaluate(state) is Outcome.InPlay))
            {
                return;
            }

            AppendClearExits(state, into);
        }

        public MoveResult<LotState, ExitRejection> TryApply(in LotState state, Exit move)
        {
            if (!(Evaluate(state) is Outcome.InPlay))
            {
                return Illegal(new ExitRejection.NotInPlay());
            }

            bool isInLot = move.Piece.Value < Level.VehicleCount && !state.HasExited(move.Piece);
            if (!isInLot)
            {
                return Illegal(new ExitRejection.NotInLot());
            }

            if (Level.FirstBlocker(move.Piece, state.Exited).TryGet(out Blocker? blocker))
            {
                return Illegal(new ExitRejection.Blocked(blocker));
            }

            return Park(state, move.Piece);
        }

        public Outcome Evaluate(in LotState state)
        {
            if (state.Exited == Level.AllExitedMask)
            {
                return new Outcome.Won();
            }

            if (state.FreeSlots == 0)
            {
                return new Outcome.Failed(FailKind.SlotsFull);
            }

            // Cannot happen on a loaded level (its blocking graph is acyclic); evaluated defensively.
            var moves = new List<Exit>();
            AppendClearExits(state, moves);
            return moves.Count == 0 ? new Outcome.Failed(FailKind.NoLegalMove) : (Outcome)new Outcome.InPlay();
        }

        public Option<LotState> TryWithOneSpare(in LotState state) =>
            state.Capacity.PlusOneSpare().TryGet(out BayCount more) ? Option.Some(state.WithCapacity(more)) : Option.None<LotState>();

        public CapacityCount CapacityOf(in LotState state) => state.Capacity.AsCapacity;

        /// <summary>Whether the vehicle is still under its cover in this state.</summary>
        public bool IsCovered(in LotState state, PieceId piece) => Level.VehicleOf(piece).StartsCovered && !state.HasExited(piece);

        private void AppendClearExits(in LotState state, List<Exit> into)
        {
            for (int index = 0; index < Level.VehicleCount; index++)
            {
                var piece = new PieceId((byte)index);
                if (!state.HasExited(piece) && !Level.FirstBlocker(piece, state.Exited).HasValue)
                {
                    into.Add(new Exit(piece));
                }
            }
        }

        private MoveResult<LotState, ExitRejection> Park(in LotState state, PieceId piece)
        {
            Vehicle vehicle = Level.VehicleOf(piece);
            var events = new List<PuzzleEvent>();

            Span<byte> colours = stackalloc byte[BayCount.PackingLimit];
            int occupied = state.OccupiedSlots;
            for (int slot = 0; slot < occupied; slot++)
            {
                colours[slot] = state.ColourIn(new SlotIndex((byte)slot)).Or(default).Value;
            }

            colours[occupied] = vehicle.Colour.Value;
            occupied++;
            events.Add(new PuzzleEvent.Exited(piece, new SlotIndex((byte)(occupied - 1))));
            if (vehicle.StartsCovered)
            {
                events.Add(new PuzzleEvent.CoverLifted(piece, vehicle.Colour));
            }

            occupied = ClearSetIfComplete(colours, occupied, vehicle.Colour, events);

            var next = new LotState(state.Exited | (1U << piece.Value), LotState.PackSlots(colours.Slice(0, occupied)), state.Capacity);
            Outcome outcome = Evaluate(next);
            if (outcome is Outcome.Won)
            {
                events.Add(new PuzzleEvent.Won());
            }
            else if (outcome is Outcome.Failed failed)
            {
                events.Add(new PuzzleEvent.Failed(failed.Kind));
            }

            return new MoveResult<LotState, ExitRejection>.Applied(next, ReadOnlyArray.CopyOf(events));
        }

        /// <summary>
        /// Count-based clearing: if the bays now hold three of the parked colour, in any positions,
        /// those three leave and the rest close up to the left. Returns the new occupied count.
        /// </summary>
        private static int ClearSetIfComplete(Span<byte> colours, int occupied, ColourId parked, List<PuzzleEvent> events)
        {
            var fromSlots = new List<SlotIndex>();
            for (int slot = 0; slot < occupied; slot++)
            {
                if (colours[slot] == parked.Value)
                {
                    fromSlots.Add(new SlotIndex((byte)slot));
                }
            }

            if (fromSlots.Count < SetSize)
            {
                return occupied;
            }

            int kept = 0;
            for (int slot = 0; slot < occupied; slot++)
            {
                if (colours[slot] != parked.Value)
                {
                    colours[kept] = colours[slot];
                    kept++;
                }
            }

            for (int slot = kept; slot < colours.Length; slot++)
            {
                colours[slot] = 0;
            }

            events.Add(new PuzzleEvent.SetCleared(parked, ReadOnlyArray.CopyOf(fromSlots)));
            return kept;
        }

        private static MoveResult<LotState, ExitRejection> Illegal(ExitRejection reason) =>
            new MoveResult<LotState, ExitRejection>.Illegal(reason);
    }
}

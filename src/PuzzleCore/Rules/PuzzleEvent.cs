namespace PuzzleCore.Rules
{
    /// <summary>
    /// What a move did, for presentation and golden replays. Closed set shared by both games;
    /// new events are added at the end only. The Sort events (Poured, LayerRevealed,
    /// VialCompleted) join with the Sort module.
    /// </summary>
    public abstract record PuzzleEvent
    {
        private PuzzleEvent()
        {
        }

        /// <summary>Jam: a vehicle left the lot and parked in a bay.</summary>
        public sealed record Exited(PieceId Piece, SlotIndex Slot) : PuzzleEvent;

        /// <summary>Jam: a covered vehicle left the lot, so its colour is now shown.</summary>
        public sealed record CoverLifted(PieceId Piece, ColourId Colour) : PuzzleEvent;

        /// <summary>Jam: three vehicles of one colour left the bays from these slots; the rest closed up to the left.</summary>
        public sealed record SetCleared(ColourId Colour, ReadOnlyArray<SlotIndex> FromSlots) : PuzzleEvent;

        /// <summary>One spare was added (Sort: a vial; Jam: a slot); the capacity is now this.</summary>
        public sealed record SpareAdded(CapacityCount Capacity) : PuzzleEvent;

        /// <summary>The state went back this many moves.</summary>
        public sealed record Rewound(MoveCount Moves) : PuzzleEvent;

        public sealed record Won : PuzzleEvent;

        public sealed record Failed(FailKind Kind) : PuzzleEvent;
    }
}

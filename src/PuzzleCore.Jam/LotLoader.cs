using System.Collections.Generic;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// Why the loader refused a level. The first seven are the brief's set; the rest were added
    /// (by addition only) for checks the brief implies but does not name.
    /// </summary>
    public enum LoadProblem
    {
        /// <summary>Some vehicles wait on each other in a cycle, so none of them could ever leave.</summary>
        BlockingCycle,

        /// <summary>A pillar stands on a vehicle's path, so that vehicle could never leave.</summary>
        PillarBlocksPath,

        /// <summary>An Ahead tag sits on a vehicle whose path is clear at the start.</summary>
        AheadWithoutBlocker,

        ColourTotalNotMultipleOfThree,

        /// <summary>A level starts with 4-7 bays.</summary>
        BayCountOutOfRange,

        PieceOutOfLot,

        /// <summary>Two vehicles, or a vehicle and a pillar, share a cell.</summary>
        PiecesOverlap,

        /// <summary>Each side of the lot is 5-8.</summary>
        SizeOutOfRange,

        /// <summary>A level has 1-24 vehicles (24 is the exited-mask width).</summary>
        VehicleCountOutOfRange,

        /// <summary>PieceIds are ascending anchor order, so the anchors must be listed strictly ascending.</summary>
        PiecesNotInAnchorOrder,

        PillarOutOfLot,

        /// <summary>A clue or cover names a PieceId the level does not have.</summary>
        UnknownPiece,

        /// <summary>A clue tag sits on a vehicle that is not covered.</summary>
        ClueOnOpenVehicle,
    }

    /// <summary>A vehicle as written in level input, before validation. Its list position is its PieceId.</summary>
    public sealed record VehicleSpec(ColourId Colour, int Anchor, VehicleLength Length, Facing Facing, bool StartsCovered);

    /// <summary>A level as written (pc-lot text or a hand-built test board), before validation.</summary>
    public sealed record LotDefinition(
        int Width,
        int Height,
        int Bays,
        ReadOnlyArray<int> Pillars,
        ReadOnlyArray<VehicleSpec> Vehicles,
        ReadOnlyArray<LotClue> Clues);

    /// <summary>Loaded(level) or Rejected(problem, the piece it concerns when there is one).</summary>
    public abstract record LoadResult
    {
        private LoadResult()
        {
        }

        public sealed record Loaded(LotLevel Level) : LoadResult;

        public sealed record Rejected(LoadProblem Problem, Option<PieceId> Piece) : LoadResult;
    }

    /// <summary>
    /// Validates a level definition into a <see cref="LotLevel"/>. A valid level always has a
    /// legal move until it is won or its bays fill: its start blocking graph is acyclic and no
    /// path crosses a pillar, and exits only ever free space.
    /// </summary>
    public static class LotLoader
    {
        public const int MaxVehicles = 24;

        public static LoadResult Load(LotDefinition definition)
        {
            if (!LotSize.Of(definition.Width, definition.Height).TryGet(out LotSize size))
            {
                return Reject(LoadProblem.SizeOutOfRange);
            }

            if (!BayCount.AtLoad(definition.Bays).TryGet(out BayCount bays))
            {
                return Reject(LoadProblem.BayCountOutOfRange);
            }

            int count = definition.Vehicles.Count;
            if (count < 1 || count > MaxVehicles)
            {
                return Reject(LoadProblem.VehicleCountOutOfRange);
            }

            ulong pillars = 0;
            foreach (int pillar in definition.Pillars)
            {
                if (!size.Contains(pillar))
                {
                    return Reject(LoadProblem.PillarOutOfLot);
                }

                pillars |= 1UL << pillar;
            }

            var footprints = new ulong[count];
            var vehicles = new Vehicle[count];
            ulong occupied = pillars;
            for (int index = 0; index < count; index++)
            {
                VehicleSpec spec = definition.Vehicles[index];
                var piece = new PieceId((byte)index);
                if (index > 0 && spec.Anchor <= definition.Vehicles[index - 1].Anchor)
                {
                    return Reject(LoadProblem.PiecesNotInAnchorOrder, piece);
                }

                if (!Footprint(size, spec).TryGet(out ulong footprint))
                {
                    return Reject(LoadProblem.PieceOutOfLot, piece);
                }

                if ((occupied & footprint) != 0)
                {
                    return Reject(LoadProblem.PiecesOverlap, piece);
                }

                occupied |= footprint;
                footprints[index] = footprint;
                vehicles[index] = new Vehicle(piece, new LotCell((byte)spec.Anchor), spec.Length, spec.Facing, spec.Colour, spec.StartsCovered);
            }

            var paths = new LotCell[count][];
            for (int index = 0; index < count; index++)
            {
                paths[index] = Path(size, vehicles[index]);
                foreach (LotCell cell in paths[index])
                {
                    if ((pillars & cell.Bit) != 0)
                    {
                        return Reject(LoadProblem.PillarBlocksPath, vehicles[index].Id);
                    }
                }
            }

            if (HasBlockingCycle(footprints, paths))
            {
                return Reject(LoadProblem.BlockingCycle);
            }

            if (!ColourTotalsAreThrees(vehicles))
            {
                return Reject(LoadProblem.ColourTotalNotMultipleOfThree);
            }

            var level = new LotLevel(size, bays, pillars, ReadOnlyArray.Of(vehicles), definition.Clues, footprints, paths);
            foreach (LotClue clue in definition.Clues)
            {
                if (clue.On.Value >= count)
                {
                    return Reject(LoadProblem.UnknownPiece, clue.On);
                }

                if (!vehicles[clue.On.Value].StartsCovered)
                {
                    return Reject(LoadProblem.ClueOnOpenVehicle, clue.On);
                }

                bool isAheadOnClearPath = clue is LotClue.Ahead && !level.FirstOnPathAtStart(clue.On).HasValue;
                if (isAheadOnClearPath)
                {
                    return Reject(LoadProblem.AheadWithoutBlocker, clue.On);
                }
            }

            return new LoadResult.Loaded(level);
        }

        private static LoadResult Reject(LoadProblem problem) => new LoadResult.Rejected(problem, Option.None<PieceId>());

        private static LoadResult Reject(LoadProblem problem, PieceId piece) => new LoadResult.Rejected(problem, Option.Some(piece));

        /// <summary>The footprint mask: along the row for East/West, down the column for North/South.</summary>
        private static Option<ulong> Footprint(LotSize size, VehicleSpec spec)
        {
            if (!size.Contains(spec.Anchor))
            {
                return Option.None<ulong>();
            }

            int length = (int)spec.Length;
            int row = spec.Anchor / size.Width;
            int column = spec.Anchor % size.Width;
            bool isHorizontal = spec.Facing == Facing.East || spec.Facing == Facing.West;
            bool fits = isHorizontal ? column + length <= size.Width : row + length <= size.Height;
            if (!fits)
            {
                return Option.None<ulong>();
            }

            int stride = isHorizontal ? 1 : size.Width;
            ulong mask = 0;
            for (int step = 0; step < length; step++)
            {
                mask |= 1UL << (spec.Anchor + (step * stride));
            }

            return Option.Some(mask);
        }

        /// <summary>The cells in front of the vehicle up to the lot edge, nearest first.</summary>
        private static LotCell[] Path(LotSize size, Vehicle vehicle)
        {
            int length = (int)vehicle.Length;
            int stride = vehicle.IsHorizontal ? 1 : size.Width;
            bool frontIsAnchor = vehicle.Facing == Facing.North || vehicle.Facing == Facing.West;
            var front = new LotCell((byte)(vehicle.Anchor.Index + (frontIsAnchor ? 0 : (length - 1) * stride)));

            var cells = new List<LotCell>();
            LotCell current = front;
            while (size.Step(current, vehicle.Facing).TryGet(out LotCell next))
            {
                cells.Add(next);
                current = next;
            }

            return cells.ToArray();
        }

        /// <summary>
        /// Kahn's algorithm on "u stands on v's path": repeatedly let out a vehicle whose path holds
        /// only vehicles already out. If some never get out, they wait on each other in a cycle.
        /// </summary>
        private static bool HasBlockingCycle(ulong[] footprints, LotCell[][] paths)
        {
            int count = footprints.Length;
            ulong[] pathMasks = new ulong[count];
            for (int piece = 0; piece < count; piece++)
            {
                foreach (LotCell cell in paths[piece])
                {
                    pathMasks[piece] |= cell.Bit;
                }
            }

            var isOut = new bool[count];
            int outCount = 0;
            bool progressed = true;
            while (progressed && outCount < count)
            {
                progressed = false;
                ulong stillIn = 0;
                for (int piece = 0; piece < count; piece++)
                {
                    if (!isOut[piece])
                    {
                        stillIn |= footprints[piece];
                    }
                }

                for (int piece = 0; piece < count; piece++)
                {
                    if (!isOut[piece] && (pathMasks[piece] & stillIn) == 0)
                    {
                        isOut[piece] = true;
                        outCount++;
                        progressed = true;
                    }
                }
            }

            return outCount < count;
        }

        private static bool ColourTotalsAreThrees(Vehicle[] vehicles)
        {
            var totals = new int[ColourId.MaxValue + 1];
            foreach (Vehicle vehicle in vehicles)
            {
                totals[vehicle.Colour.Value]++;
            }

            foreach (int total in totals)
            {
                if (total % 3 != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

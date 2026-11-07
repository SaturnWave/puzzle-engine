using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Randomness;

namespace PuzzleCore.Jam
{
    /// <summary>What one level should look like (a content-plan entry). All counts are integers.</summary>
    public sealed record LotPlan(
        int Width,
        int Height,
        int Colours,
        int Sets,
        int Bays,
        int MinSlack,
        int MaxSlack,
        PerMille VanShare,
        int Pillars,
        PerMille MinBlocked);

    /// <summary>A generated level with its par: the fewest bays it can be won with, and an order that does it.</summary>
    public sealed record GeneratedLot(LotLevel Level, CapacityCount PeakPar, ReadOnlyArray<Exit> ParPath, int Tries)
    {
        public int Slack => Level.Bays.Value - PeakPar.Value;
    }

    /// <summary>
    /// Why one try was thrown away. Closed set. LoaderRejected and WitnessUnsolvable are generator
    /// bugs (the construction guarantees a valid, winnable level) and tests assert they never occur.
    /// </summary>
    public enum TryFailure
    {
        NoExitOrder,
        NoPlacement,
        LoaderRejected,
        TooFewBlocked,
        SolverBudget,
        SlackOutOfBand,
        WitnessUnsolvable,
    }

    public abstract record GenerateResult
    {
        private GenerateResult()
        {
        }

        public sealed record Generated(GeneratedLot Lot) : GenerateResult;

        /// <summary>Every try failed; one reason per try, in try order.</summary>
        public sealed record Rejected(ReadOnlyArray<TryFailure> Tries) : GenerateResult;
    }

    /// <summary>
    /// Plain levels by reverse-exit scramble (CoveredLot.md, "Generator"):
    /// 1. draw a colour exit order whose bay load never fills the row without a clear (the win witness);
    /// 2. place vehicles in reverse exit order, each with a path to the edge clear of every vehicle
    ///    already placed, so the blocking graph is acyclic by construction; favour spots that block a
    ///    later-leaving vehicle, for interlock;
    /// 3. put pillars only on cells no path crosses;
    /// 4. gate on the blocked share at the start and on the slack (bays − PeakPar).
    /// Try t uses colour sample ⌊t/4⌋ and layout t. Deterministic: same pack, level and plan, same level.
    /// </summary>
    public static class LotGenerator
    {
        public const int TriesPerSlot = 20;
        private static readonly NodeBudget SolveBudget = new NodeBudget(200_000);

        /// <summary>How strongly placement favours spots that block a vehicle leaving later (per mille of draws).</summary>
        private const int BlockingPreference = 800;

        public static GenerateResult Generate(PackId pack, LevelIndex index, LotPlan plan)
        {
            Seed seed = Seed.ForPack(pack);
            var failures = new List<TryFailure>();
            for (int attempt = 0; attempt < TriesPerSlot; attempt++)
            {
                Pcg32 palette = SeedDerivation.Stream(seed, StreamKey.ForGenerator(RandomPurpose.Palette, pack, index, attempt / 4));
                Pcg32 layout = SeedDerivation.Stream(seed, StreamKey.ForGenerator(RandomPurpose.Layout, pack, index, attempt));
                switch (TryOnce(plan, palette, layout))
                {
                    case TryOutcome.Built built:
                        return new GenerateResult.Generated(new GeneratedLot(built.Level, built.Par.Peak, built.Par.Path, attempt + 1));
                    case TryOutcome.Failed failed:
                        failures.Add(failed.Reason);
                        break;
                }
            }

            return new GenerateResult.Rejected(ReadOnlyArray.CopyOf(failures));
        }

        private static TryOutcome TryOnce(LotPlan plan, Pcg32 palette, Pcg32 layout)
        {
            if (!ExitOrder(plan, palette).TryGet(out int[]? colours))
            {
                return Fail(TryFailure.NoExitOrder);
            }

            if (!LotSize.Of(plan.Width, plan.Height).TryGet(out LotSize size))
            {
                return Fail(TryFailure.NoPlacement);
            }

            var placed = new List<Placement>();
            for (int order = colours.Length - 1; order >= 0; order--)
            {
                if (!Place(size, plan, layout, placed, colours[order]).TryGet(out Placement? placement))
                {
                    return Fail(TryFailure.NoPlacement);
                }

                placed.Add(placement);
            }

            ReadOnlyArray<int> pillars = Pillars(size, plan.Pillars, layout, placed);
            ReadOnlyArray<VehicleSpec> vehicles = ReadOnlyArray.CopyOf(placed
                .OrderBy(placement => placement.Anchor)
                .Select(placement => new VehicleSpec(new ColourId((byte)placement.Colour), placement.Anchor, placement.Length, placement.Facing, StartsCovered: false)));

            var definition = new LotDefinition(plan.Width, plan.Height, plan.Bays, pillars, vehicles, ReadOnlyArray.Empty<LotClue>());
            if (!(LotLoader.Load(definition) is LoadResult.Loaded loaded))
            {
                return Fail(TryFailure.LoaderRejected);
            }

            return Grade(loaded.Level, plan);
        }

        /// <summary>Interlock and slack gates; the par search runs with the level's own bays as the ceiling.</summary>
        private static TryOutcome Grade(LotLevel level, LotPlan plan)
        {
            int blocked = 0;
            for (int index = 0; index < level.VehicleCount; index++)
            {
                if (level.FirstOnPathAtStart(new PieceId((byte)index)).HasValue)
                {
                    blocked++;
                }
            }

            if (PerMille.Of(blocked, level.VehicleCount).Value < plan.MinBlocked.Value)
            {
                return Fail(TryFailure.TooFewBlocked);
            }

            switch (LotSolver.PeakPar(level, level.Bays.AsCapacity, SolveBudget))
            {
                case PeakParResult.Found par:
                    int slack = level.Bays.Value - par.Peak.Value;
                    bool isInBand = slack >= plan.MinSlack && slack <= plan.MaxSlack;
                    return isInBand ? new TryOutcome.Built(level, par) : Fail(TryFailure.SlackOutOfBand);
                case PeakParResult.BudgetExceeded:
                    return Fail(TryFailure.SolverBudget);
                default:
                    // The exit-order witness wins with the plan's bays, so this would be a generator bug.
                    return Fail(TryFailure.WitnessUnsolvable);
            }
        }

        private static TryOutcome Fail(TryFailure reason) => new TryOutcome.Failed(reason);

        /// <summary>
        /// A random exit order of colours (3 per set) that the plan's bays can hold: a park that does not
        /// clear must leave a bay free. Restarts on a dead end, a bounded number of times.
        /// </summary>
        private static Option<int[]> ExitOrder(LotPlan plan, Pcg32 random)
        {
            int[] setsPerColour = new int[plan.Colours];
            for (int colour = 0; colour < plan.Colours; colour++)
            {
                setsPerColour[colour] = 1;
            }

            for (int extra = plan.Colours; extra < plan.Sets; extra++)
            {
                setsPerColour[random.NextIndex(plan.Colours)]++;
            }

            for (int restart = 0; restart < 50; restart++)
            {
                int[] remaining = setsPerColour.Select(sets => sets * LotSolver.SetSize).ToArray();
                int[] waiting = new int[plan.Colours];
                int occupied = 0;
                var order = new List<int>();
                var choices = new List<int>();
                while (order.Count < plan.Sets * LotSolver.SetSize)
                {
                    choices.Clear();
                    for (int colour = 0; colour < plan.Colours; colour++)
                    {
                        bool clears = waiting[colour] == LotSolver.SetSize - 1;
                        bool fits = clears ? occupied + 1 <= plan.Bays : occupied + 1 < plan.Bays;
                        if (remaining[colour] > 0 && fits)
                        {
                            choices.Add(colour);
                        }
                    }

                    if (choices.Count == 0)
                    {
                        break;
                    }

                    int chosen = choices[random.NextIndex(choices.Count)];
                    remaining[chosen]--;
                    waiting[chosen] = (waiting[chosen] + 1) % LotSolver.SetSize;
                    occupied = waiting.Sum();
                    order.Add(chosen + 1);
                }

                if (order.Count == plan.Sets * LotSolver.SetSize)
                {
                    return Option.Some(order.ToArray());
                }
            }

            return Option.None<int[]>();
        }

        /// <summary>
        /// One vehicle placed so that its path is clear of every vehicle already placed (they all leave
        /// after it). Spots that land on a placed vehicle's path are favoured: those make interlock.
        /// </summary>
        private static Option<Placement> Place(LotSize size, LotPlan plan, Pcg32 random, List<Placement> placed, int colour)
        {
            VehicleLength length = random.NextBelow(PerMille.Whole) < plan.VanShare.Value ? VehicleLength.Three : VehicleLength.Two;
            ulong occupiedCells = 0;
            ulong placedPaths = 0;
            foreach (Placement other in placed)
            {
                occupiedCells |= other.Footprint;
                placedPaths |= other.Path;
            }

            var blocking = new List<Placement>();
            var free = new List<Placement>();
            foreach (VehicleLength candidateLength in length == VehicleLength.Three ? new[] { VehicleLength.Three, VehicleLength.Two } : new[] { VehicleLength.Two })
            {
                for (int anchor = 0; anchor < size.CellCount; anchor++)
                {
                    foreach (Facing facing in new[] { Facing.North, Facing.East, Facing.South, Facing.West })
                    {
                        if (!Placement.Try(size, anchor, candidateLength, facing, colour).TryGet(out Placement? candidate))
                        {
                            continue;
                        }

                        bool isValid = (candidate.Footprint & occupiedCells) == 0 && (candidate.Path & occupiedCells) == 0;
                        if (!isValid)
                        {
                            continue;
                        }

                        ((candidate.Footprint & placedPaths) != 0 ? blocking : free).Add(candidate);
                    }
                }

                if (blocking.Count + free.Count > 0)
                {
                    break;
                }
            }

            bool preferBlocking = blocking.Count > 0 && (free.Count == 0 || random.NextBelow(PerMille.Whole) < BlockingPreference);
            List<Placement> pool = preferBlocking ? blocking : free;
            return pool.Count == 0 ? Option.None<Placement>() : Option.Some(pool[random.NextIndex(pool.Count)]);
        }

        /// <summary>Pillars on random empty cells that no vehicle's path crosses.</summary>
        private static ReadOnlyArray<int> Pillars(LotSize size, int count, Pcg32 random, List<Placement> placed)
        {
            ulong blockedCells = 0;
            foreach (Placement placement in placed)
            {
                blockedCells |= placement.Footprint | placement.Path;
            }

            var candidates = new List<int>();
            for (int cell = 0; cell < size.CellCount; cell++)
            {
                if ((blockedCells & (1UL << cell)) == 0)
                {
                    candidates.Add(cell);
                }
            }

            var chosen = new List<int>();
            while (chosen.Count < count && candidates.Count > 0)
            {
                int pick = random.NextIndex(candidates.Count);
                chosen.Add(candidates[pick]);
                candidates.RemoveAt(pick);
            }

            return ReadOnlyArray.CopyOf(chosen.OrderBy(cell => cell));
        }

        private abstract record TryOutcome
        {
            private TryOutcome()
            {
            }

            public sealed record Built(LotLevel Level, PeakParResult.Found Par) : TryOutcome;

            public sealed record Failed(TryFailure Reason) : TryOutcome;
        }

        private sealed class Placement
        {
            private Placement(int anchor, VehicleLength length, Facing facing, int colour, ulong footprint, ulong path)
            {
                Anchor = anchor;
                Length = length;
                Facing = facing;
                Colour = colour;
                Footprint = footprint;
                Path = path;
            }

            public int Anchor { get; }

            public VehicleLength Length { get; }

            public Facing Facing { get; }

            public int Colour { get; }

            public ulong Footprint { get; }

            public ulong Path { get; }

            public static Option<Placement> Try(LotSize size, int anchor, VehicleLength length, Facing facing, int colour)
            {
                int cells = (int)length;
                int row = anchor / size.Width;
                int column = anchor % size.Width;
                bool isHorizontal = facing == Facing.East || facing == Facing.West;
                bool fits = isHorizontal ? column + cells <= size.Width : row + cells <= size.Height;
                if (!fits)
                {
                    return Option.None<Placement>();
                }

                int stride = isHorizontal ? 1 : size.Width;
                ulong footprint = 0;
                for (int step = 0; step < cells; step++)
                {
                    footprint |= 1UL << (anchor + (step * stride));
                }

                bool frontIsAnchor = facing == Facing.North || facing == Facing.West;
                var current = new LotCell((byte)(anchor + (frontIsAnchor ? 0 : (cells - 1) * stride)));
                ulong path = 0;
                while (size.Step(current, facing).TryGet(out LotCell next))
                {
                    path |= next.Bit;
                    current = next;
                }

                return Option.Some(new Placement(anchor, length, facing, colour, footprint, path));
            }
        }
    }
}

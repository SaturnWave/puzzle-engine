using System;
using System.Collections.Generic;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Records;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Jam
{
    public sealed class LotSolverTests
    {
        private static readonly NodeBudget Budget = new(1_000_000);

        [Fact]
        public void The_six_by_six_board_has_peak_par_three_and_its_path_replays_to_a_win()
        {
            LotLevel level = Load(SixBySix());

            var found = Assert.IsType<PeakParResult.Found>(LotSolver.PeakPar(level, level.Bays.AsCapacity, Budget));

            Assert.Equal(new CapacityCount(3), found.Peak);
            Assert.Equal(level.VehicleCount, found.Path.Count);
            Assert.Equal(3, LotSolver.PeakOf(level, found.Path));
            AssertWinsWithItsOwnBays(level, found.Path);
        }

        [Fact]
        public void Free_cars_of_three_colours_always_need_only_three_bays()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));

            var found = Assert.IsType<PeakParResult.Found>(LotSolver.PeakPar(level, level.Bays.AsCapacity, Budget));

            Assert.Equal(new CapacityCount(3), found.Peak);
        }

        [Fact]
        public void A_tiny_budget_is_reported_as_budget_exceeded_not_as_unsolvable()
        {
            LotLevel level = Load(SixBySix());

            Assert.IsType<SolveResult<Exit>.BudgetExceeded>(LotSolver.Solve(level, new CapacityCount(3), new NodeBudget(2)));
        }

        [Fact]
        public void Peak_par_equals_an_independent_min_peak_search_and_its_path_wins_on_generated_levels()
        {
            // The brief's property: the smallest feasible bay count equals the lowest peak over all
            // winning orders. The reference below knows nothing about bay limits; it only minimises the peak.
            int checkedLevels = 0;
            foreach (string packText in new[] { "solver-property-a", "solver-property-b" })
            {
                PackId pack = PackId.Parse(packText).Or(default);
                for (int index = 0; index < 30; index++)
                {
                    if (!(LotGenerator.Generate(pack, new LevelIndex(index), LotContentPlan.For(new LevelIndex(index))) is GenerateResult.Generated generated))
                    {
                        continue;
                    }

                    LotLevel level = generated.Lot.Level;
                    Assert.Equal(MinPeakReference(level), generated.Lot.PeakPar.Value);
                    Assert.Equal(generated.Lot.PeakPar.Value, LotSolver.PeakOf(level, generated.Lot.ParPath));
                    AssertWinsWithItsOwnBays(level, generated.Lot.ParPath);
                    checkedLevels++;
                }
            }

            Assert.True(checkedLevels >= 55, "Only " + checkedLevels.ToString(System.Globalization.CultureInfo.InvariantCulture) + " levels were generated.");
        }

        [Fact]
        public void With_one_bay_fewer_than_peak_par_no_order_wins()
        {
            PackId pack = PackId.Parse("solver-tight").Or(default);
            for (int index = 0; index < 20; index++)
            {
                var generated = Assert.IsType<GenerateResult.Generated>(LotGenerator.Generate(pack, new LevelIndex(index), LotContentPlan.For(new LevelIndex(index))));
                int below = generated.Lot.PeakPar.Value - 1;

                Assert.IsType<SolveResult<Exit>.Unsolvable>(LotSolver.Solve(generated.Lot.Level, new CapacityCount(below), Budget));
            }
        }

        private static void AssertWinsWithItsOwnBays(LotLevel level, ReadOnlyArray<Exit> path)
        {
            var actions = new List<RecordAction<Exit>>();
            foreach (Exit exit in path)
            {
                actions.Add(new RecordAction<Exit>.Move(exit, new ElapsedMs(0)));
            }

            var rules = new JamRules(level);
            ReplayReport report = Replay.Run(rules, rules.Start, PlayMode.Campaign, ReadOnlyArray.CopyOf(actions));

            Assert.Equal(new ReplayVerdict.Valid(new Outcome.Won(), new MoveCount(level.VehicleCount), SpareUsed: false), report.Verdict);
        }

        /// <summary>Exhaustive memoised search: the lowest achievable peak over all exit orders, with unlimited bays.</summary>
        private static int MinPeakReference(LotLevel level)
        {
            var memo = new Dictionary<uint, int>();
            return MinPeakFrom(level, 0U, memo);
        }

        private static int MinPeakFrom(LotLevel level, uint exited, Dictionary<uint, int> memo)
        {
            if (exited == level.AllExitedMask)
            {
                return 0;
            }

            if (memo.TryGetValue(exited, out int known))
            {
                return known;
            }

            var counts = new int[ColourId.MaxValue + 1];
            for (int piece = 0; piece < level.VehicleCount; piece++)
            {
                if ((exited & (1U << piece)) != 0)
                {
                    counts[level.Vehicles[piece].Colour.Value]++;
                }
            }

            int occupied = 0;
            foreach (int count in counts)
            {
                occupied += count % 3;
            }

            int best = int.MaxValue;
            for (int piece = 0; piece < level.VehicleCount; piece++)
            {
                bool isFree = (exited & (1U << piece)) == 0 && !level.FirstBlocker(new PieceId((byte)piece), exited).HasValue;
                if (isFree)
                {
                    best = Math.Min(best, Math.Max(occupied + 1, MinPeakFrom(level, exited | (1U << piece), memo)));
                }
            }

            memo[exited] = best;
            return best;
        }
    }
}

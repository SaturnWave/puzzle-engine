using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>
    /// The Planners' look ahead (Q79), checked against an independent search: every line of up to k moves played with
    /// JamRules on the true colours, ranked by its ending as Q79 words it.
    /// </summary>
    public sealed class LookaheadTests
    {
        [Fact]
        public void A_win_ranks_first_then_lines_in_play_then_lines_that_fill_the_bays()
        {
            // The free-cars board, B R Y three times (pieces 0 B, 1 R, 2 Y, 3 B, 4 R, ...), every car free.
            LotPosition blueRed = Parked(4, 0, 1);
            LotPosition blueBlue = Parked(4, 0, 3);
            LotPosition blueBlueRed = Parked(4, 0, 3, 1);
            LotPosition fullOfTwoColours = Parked(4, 0, 3, 1, 4);
            LotPosition fullOfThreeColours = Parked(4, 0, 3, 1, 2);
            LotPosition fiveBaysFull = Parked(5, 0, 3, 1, 4, 2);

            Assert.True(LineScore.Won > LineScore.InPlay(Parked(4)));
            Assert.True(LineScore.InPlay(blueBlueRed) > LineScore.BaysFull(fullOfTwoColours));
            Assert.True(LineScore.InPlay(blueRed) > LineScore.InPlay(blueBlueRed));
            Assert.True(LineScore.InPlay(blueBlue) > LineScore.InPlay(blueRed));
            Assert.True(LineScore.BaysFull(fullOfTwoColours) > LineScore.BaysFull(fullOfThreeColours));
            Assert.True(LineScore.BaysFull(fullOfThreeColours) > LineScore.BaysFull(fiveBaysFull));
            Assert.Equal(LineScore.InPlay(blueRed), LineScore.InPlay(Parked(4, 1, 0)));
            Assert.NotEqual(LineScore.InPlay(fullOfTwoColours), LineScore.BaysFull(fullOfTwoColours));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void The_best_first_moves_are_those_whose_best_line_ends_best(int plies)
        {
            int positions = 0;
            var legal = new List<Exit>();
            var best = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.All.Where(level => level.Slot % 4 == plies))
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                var model = new LotModel(level);
                ColourView truth = TrueColours(level);
                Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, 7);
                LotState state = rules.Start;
                while (rules.Evaluate(state) is Outcome.InPlay)
                {
                    legal.Clear();
                    rules.AppendLegalMoves(state, legal);
                    Lookahead.BestFirstMoves(model, truth, LotPosition.Of(state), new MoveCount(plies), legal, best);

                    Assert.Equal(OracleBestFirstMoves(rules, state, legal, plies), best);
                    positions++;
                    state = Applied(rules, state, legal[random.NextIndex(legal.Count)]);
                }
            }

            Assert.True(positions > 100, "The check must see many positions.");
        }

        [Fact]
        public void One_move_ahead_the_planners_rank_moves_as_greedys_habit_does()
        {
            var legal = new List<Exit>();
            var ahead = new List<Exit>();
            var habit = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.All)
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                var model = new LotModel(level);
                var greedy = new GreedyPolicy(level);
                ColourView truth = TrueColours(level);
                Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, 9);
                LotState state = rules.Start;
                while (rules.Evaluate(state) is Outcome.InPlay)
                {
                    legal.Clear();
                    rules.AppendLegalMoves(state, legal);
                    Lookahead.BestFirstMoves(model, truth, LotPosition.Of(state), new MoveCount(1), legal, ahead);
                    greedy.BestMoves(state, legal, habit);

                    Assert.Equal(habit, ahead);
                    state = Applied(rules, state, habit[random.NextIndex(habit.Count)]);
                }
            }
        }

        [Fact]
        public void Looking_three_moves_ahead_changes_the_choice_somewhere_in_the_pack()
        {
            var legal = new List<Exit>();
            var oneAhead = new List<Exit>();
            var threeAhead = new List<Exit>();
            int differ = 0;
            foreach (CampaignLevel campaign in CampaignLevels.All.Take(20))
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                var model = new LotModel(level);
                ColourView truth = TrueColours(level);
                Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, 11);
                LotState state = rules.Start;
                while (rules.Evaluate(state) is Outcome.InPlay)
                {
                    legal.Clear();
                    rules.AppendLegalMoves(state, legal);
                    Lookahead.BestFirstMoves(model, truth, LotPosition.Of(state), new MoveCount(1), legal, oneAhead);
                    Lookahead.BestFirstMoves(model, truth, LotPosition.Of(state), new MoveCount(3), legal, threeAhead);
                    differ += oneAhead.SequenceEqual(threeAhead) ? 0 : 1;
                    state = Applied(rules, state, threeAhead[random.NextIndex(threeAhead.Count)]);
                }
            }

            Assert.True(differ > 0);
        }

        [Fact]
        public void A_planner_looks_at_least_one_move_ahead()
        {
            LotLevel level = Load(SixBySix());
            var legal = new List<Exit> { new(new PieceId(0)) };

            Assert.Throws<ArgumentOutOfRangeException>(() => Lookahead.BestFirstMoves(
                new LotModel(level), TrueColours(level), LotPosition.Of(LotState.Start(level)), new MoveCount(0), legal, new List<Exit>()));
        }

        /// <summary>The free-cars board with these bays, after the given cars parked in order.</summary>
        private static LotPosition Parked(int bays, params int[] pieces)
        {
            LotLevel level = Load(FreeCars(bays, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            LotPosition position = LotPosition.Of(LotState.Start(level));
            foreach (int piece in pieces)
            {
                var id = new PieceId((byte)piece);
                position = position.After(id, level.VehicleOf(id).Colour);
            }

            return position;
        }

        /// <summary>A view that believes every cover's true colour.</summary>
        internal static ColourView TrueColours(LotLevel level)
        {
            var view = new ColourView(level);
            foreach (Vehicle vehicle in level.Vehicles.Where(vehicle => vehicle.StartsCovered))
            {
                view.Guess(vehicle.Id, vehicle.Colour);
            }

            return view;
        }

        internal static LotState Applied(JamRules rules, LotState state, Exit move) =>
            ((MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, move)).Next;

        /// <summary>The first moves whose best line, played with JamRules, ranks highest; in the rules' order.</summary>
        private static List<Exit> OracleBestFirstMoves(JamRules rules, LotState state, List<Exit> legal, int plies)
        {
            var ranked = legal.Select(move => (Move: move, Rank: OracleBestLine(rules, Applied(rules, state, move), plies - 1))).ToList();
            (int, int, int) top = ranked.Max(entry => entry.Rank);
            return ranked.Where(entry => entry.Rank.Equals(top)).Select(entry => entry.Move).ToList();
        }

        /// <summary>
        /// Q79's order as a tuple compared field by field, higher first: how the line ends (won 2, in play 1, bays full 0),
        /// then fewer bays in use, then fewer colours waiting.
        /// </summary>
        private static (int End, int FewerBays, int FewerColours) OracleBestLine(JamRules rules, LotState state, int movesLeft)
        {
            Outcome outcome = rules.Evaluate(state);
            int bays = state.OccupiedSlots;
            int colours = Enumerable.Range(0, bays).Select(slot => state.ColourIn(new SlotIndex((byte)slot)).Or(default)).Distinct().Count();
            if (outcome is Outcome.Won)
            {
                return (2, 0, 0);
            }

            if (outcome is Outcome.Failed || movesLeft == 0)
            {
                return (outcome is Outcome.Failed ? 0 : 1, -bays, -colours);
            }

            var legal = new List<Exit>();
            rules.AppendLegalMoves(state, legal);
            return legal.Max(move => OracleBestLine(rules, Applied(rules, state, move), movesLeft - 1));
        }
    }
}

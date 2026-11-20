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
    /// <summary>Greedy (Q76, Q80): the colour with the most waiting, any free vehicle when none waits, ties drawn.</summary>
    public sealed class GreedyPolicyTests
    {
        [Fact]
        public void Greedy_taps_the_colour_with_the_most_waiting_and_draws_only_among_two_or_more()
        {
            // The free-cars board: B R Y three times, all free. With nothing waiting every car ties; with one blue waiting
            // the two other blues tie; with two blues waiting only the third blue is best.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            var rules = new JamRules(level);
            var greedy = new GreedyPolicy(level);
            LotState oneBlue = LookaheadTests.Applied(rules, rules.Start, new Exit(new PieceId(0)));
            LotState twoBlues = LookaheadTests.Applied(rules, oneBlue, new Exit(new PieceId(3)));
            Pcg32 random = Pcg32.Seeded(8, 8);
            Pcg32State before = random.Snapshot();

            Exit only = greedy.Intend(twoBlues, Legal(rules, twoBlues), random);

            Assert.Equal(Enumerable.Range(0, 9).Select(Tap), Best(greedy, rules, rules.Start));
            Assert.Equal(new[] { Tap(3), Tap(6) }, Best(greedy, rules, oneBlue));
            Assert.Equal(Tap(6), only);
            Assert.Equal(before, random.Snapshot());
            Assert.Equal(Tap(3 * (1 + Pcg32.Seeded(8, 8).NextIndex(2))), greedy.Intend(oneBlue, Legal(rules, oneBlue), random));
        }

        [Fact]
        public void Greedy_picks_exactly_the_best_moves_of_the_habit_gen_measured_greedy_win_with()
        {
            // Q80: the habit as LotPlayouts.GreedyWins played it before the Greedy bot replaced it, written out here as the
            // oracle: count the colours in the bays slot by slot, and keep the free vehicles whose colour has the highest count.
            int positions = 0;
            var habit = new List<Exit>();
            var best = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.All)
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                var greedy = new GreedyPolicy(level);
                for (int playout = 0; playout < 3; playout++)
                {
                    Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, (ulong)playout);
                    LotState state = rules.Start;
                    while (rules.Evaluate(state) is Outcome.InPlay)
                    {
                        List<Exit> legal = Legal(rules, state);
                        OldHabit(level, state, legal, habit);
                        greedy.BestMoves(state, legal, best);

                        Assert.Equal(habit, best);
                        positions++;
                        state = LookaheadTests.Applied(rules, state, best[random.NextIndex(best.Count)]);
                    }
                }
            }

            Assert.True(positions > 1000);
        }

        private static void OldHabit(LotLevel level, LotState state, List<Exit> legal, List<Exit> into)
        {
            var waiting = new int[ColourId.MaxValue + 1];
            for (int slot = 0; slot < state.OccupiedSlots; slot++)
            {
                if (state.ColourIn(new SlotIndex((byte)slot)).TryGet(out ColourId colour))
                {
                    waiting[colour.Value]++;
                }
            }

            int bestScore = -1;
            into.Clear();
            foreach (Exit move in legal)
            {
                int score = waiting[level.VehicleOf(move.Piece).Colour.Value];
                if (score > bestScore)
                {
                    bestScore = score;
                    into.Clear();
                }

                if (score == bestScore)
                {
                    into.Add(move);
                }
            }
        }

        private static List<Exit> Legal(JamRules rules, LotState state)
        {
            var legal = new List<Exit>();
            rules.AppendLegalMoves(state, legal);
            return legal;
        }

        private static List<Exit> Best(GreedyPolicy greedy, JamRules rules, LotState state)
        {
            var best = new List<Exit>();
            greedy.BestMoves(state, Legal(rules, state), best);
            return best;
        }

        private static Exit Tap(int piece) => new(new PieceId((byte)piece));
    }
}

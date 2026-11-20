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
    /// <summary>The bots' own lot model (TC21): on the true colours it moves and ends exactly as JamRules does.</summary>
    public sealed class LotModelTests
    {
        [Fact]
        public void On_the_true_colours_the_model_frees_moves_and_ends_the_lot_as_the_rules_do()
        {
            int wins = 0;
            int fills = 0;
            var legal = new List<Exit>();
            var best = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.All)
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                var model = new LotModel(level);
                var greedy = new GreedyPolicy(level);
                for (int playout = 0; playout < 6; playout++)
                {
                    // Random playouts mostly fill the bays; the greedy ones (odd playouts) reach wins too.
                    Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, (ulong)playout);
                    LotState state = rules.Start;
                    LotPosition position = LotPosition.Of(state);
                    while (true)
                    {
                        Assert.Equal(LotPosition.Of(state), position);
                        Outcome outcome = rules.Evaluate(state);
                        Assert.Equal(outcome is Outcome.Won, model.IsWon(position));
                        Assert.Equal(outcome is Outcome.Failed, !model.IsWon(position) && position.BaysFull);
                        if (outcome is not Outcome.InPlay)
                        {
                            wins += outcome is Outcome.Won ? 1 : 0;
                            fills += outcome is Outcome.Failed ? 1 : 0;
                            break;
                        }

                        legal.Clear();
                        rules.AppendLegalMoves(state, legal);
                        Assert.Equal(legal.Select(move => move.Piece), Pieces(level).Where(piece => model.CanLeave(position, piece)));
                        if (playout % 2 == 1)
                        {
                            greedy.BestMoves(state, legal, best);
                        }

                        List<Exit> choices = playout % 2 == 1 ? best : legal;
                        Exit played = choices[random.NextIndex(choices.Count)];
                        state = ((MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, played)).Next;
                        position = position.After(played.Piece, level.VehicleOf(played.Piece).Colour);
                    }
                }
            }

            Assert.True(wins > 0 && fills > 0, "The playouts must reach both endings.");
        }

        [Fact]
        public void A_vehicle_parks_and_three_of_a_colour_leave_the_bays_together()
        {
            // The free-cars board: nine cars B R Y three times, every one free, four bays.
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            LotPosition start = LotPosition.Of(LotState.Start(level));

            LotPosition twoBluesAndARed = start.After(new PieceId(0), Blue).After(new PieceId(3), Blue).After(new PieceId(1), Red);
            LotPosition afterThirdBlue = twoBluesAndARed.After(new PieceId(6), Blue);

            Assert.Equal((3, 2, 2, 1), (twoBluesAndARed.BaysInUse, twoBluesAndARed.ColoursWaiting, twoBluesAndARed.Waiting(Blue), twoBluesAndARed.Waiting(Red)));
            Assert.Equal((1, 1, 0, 1), (afterThirdBlue.BaysInUse, afterThirdBlue.ColoursWaiting, afterThirdBlue.Waiting(Blue), afterThirdBlue.Waiting(Red)));
            Assert.Equal(0b100_1011U, afterThirdBlue.Exited);
            Assert.Equal((0, 0, 4), (start.BaysInUse, start.ColoursWaiting, start.Bays));
        }

        [Fact]
        public void The_bays_are_full_when_every_bay_holds_a_vehicle_and_a_vehicle_leaves_once()
        {
            LotLevel level = Load(FreeCars(4, Blue, Red, Yellow, Blue, Red, Yellow, Blue, Red, Yellow));
            LotPosition threeColours = LotPosition.Of(LotState.Start(level)).After(new PieceId(0), Blue).After(new PieceId(1), Red).After(new PieceId(2), Yellow);

            LotPosition full = threeColours.After(new PieceId(3), Blue);

            Assert.False(threeColours.BaysFull);
            Assert.True(full.BaysFull);
            Assert.Throws<InvalidOperationException>(() => threeColours.After(new PieceId(1), Red));
        }

        [Fact]
        public void A_vehicle_can_leave_once_every_vehicle_on_its_path_has_left()
        {
            // The buried board: piece 0's path holds pieces 1 and 2, and piece 1's holds piece 3.
            LotLevel level = Load(BuriedBehindTwo());
            var model = new LotModel(level);
            LotPosition start = LotPosition.Of(LotState.Start(level));

            LotPosition afterThreeAndTwo = start.After(new PieceId(3), Blue).After(new PieceId(2), Red);
            LotPosition afterOne = afterThreeAndTwo.After(new PieceId(1), Blue);

            Assert.False(model.CanLeave(start, new PieceId(0)));
            Assert.False(model.CanLeave(start, new PieceId(1)));
            Assert.True(model.CanLeave(start, new PieceId(3)));
            Assert.False(model.CanLeave(afterThreeAndTwo, new PieceId(0)));
            Assert.True(model.CanLeave(afterOne, new PieceId(0)));
            Assert.False(model.CanLeave(afterOne, new PieceId(1)));
        }

        private static IEnumerable<PieceId> Pieces(LotLevel level) =>
            Enumerable.Range(0, level.VehicleCount).Select(index => new PieceId((byte)index));
    }
}

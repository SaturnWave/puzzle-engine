using System;
using System.Collections.Generic;
using System.Linq;
using PuzzleCore.Bot.Jam;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Randomness;
using PuzzleCore.Rules;
using PuzzleCore.Search;
using Xunit;
using static PuzzleCore.Tests.Jam.Boards;

namespace PuzzleCore.Tests.Bot
{
    /// <summary>
    /// Planner1 and Planner3 (Q71, Q72, Q79, TC21, TC23): what they read and guess, the order of their draws, and that a
    /// cover they have not seen lift is to them only what the public information says.
    /// </summary>
    public sealed class PlannerPolicyTests
    {
        private static readonly (int Plies, DeductionRung Ceiling)[] Planners = { (1, DeductionRung.D1), (3, DeductionRung.D2) };

        [Fact]
        public void A_planner_draws_its_guess_before_its_tie_keeps_it_while_it_fits_and_redraws_every_cover_a_lift_breaks()
        {
            int redraws = 0;
            int keeps = 0;
            int guesses = 0;
            var legal = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.Covered)
            {
                LotLevel level = campaign.Level;
                var rules = new JamRules(level);
                foreach ((int plies, DeductionRung ceiling) in Planners)
                {
                    for (int seed = 0; seed < 4; seed++)
                    {
                        var policy = new PlannerPolicy(level, new MoveCount(plies), ceiling);
                        var replica = new ReplicaPlanner(level, plies, ceiling);
                        Pcg32 random = Pcg32.Seeded((ulong)campaign.Slot, (ulong)seed);
                        Pcg32 replay = Pcg32.Seeded((ulong)campaign.Slot, (ulong)seed);
                        LotState state = rules.Start;
                        while (rules.Evaluate(state) is Outcome.InPlay)
                        {
                            legal.Clear();
                            rules.AppendLegalMoves(state, legal);

                            Exit intended = policy.Intend(state, legal, random);

                            Assert.Equal(replica.Intend(state, legal, replay), intended);
                            Assert.Equal(replay.Snapshot(), random.Snapshot());
                            Assert.All(level.Vehicles, vehicle => Assert.Equal(replica.Believes(vehicle.Id), policy.Believes(vehicle.Id)));
                            var applied = (MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, intended);
                            policy.Observe(applied.Events);
                            replica.Observe(applied.Events);
                            state = applied.Next;
                        }

                        Assert.Equal(Option.Some(replica.Tally), policy.Guesses);
                        redraws += replica.Redraws;
                        keeps += replica.Keeps;
                        guesses += replica.Tally.Right + replica.Tally.Wrong;
                    }
                }
            }

            Assert.True(redraws > 0 && keeps > 0 && guesses > 0, "Redraws " + redraws + ", keeps " + keeps + ", guesses " + guesses + ".");
        }

        [Fact]
        public void Twins_with_their_hidden_colours_rotated_get_the_same_moves_until_a_cover_lifts()
        {
            // The twin keeps every public fact (geometry, open colours, ledger, tags) and moves the hidden colours one
            // cover along, so a planner that reads only public facts plays both alike until a cover shows its colour.
            int twins = 0;
            int movesCompared = 0;
            var legal = new List<Exit>();
            foreach (CampaignLevel campaign in CampaignLevels.Covered)
            {
                LotLevel level = campaign.Level;
                LotLevel twin = WithHiddenColoursRotated(level);
                if (level.Vehicles.SequenceEqual(twin.Vehicles))
                {
                    continue;
                }

                twins++;
                var rules = new JamRules(level);
                foreach ((int plies, DeductionRung ceiling) in Planners)
                {
                    for (int seed = 0; seed < 3; seed++)
                    {
                        var original = new PlannerPolicy(level, new MoveCount(plies), ceiling);
                        var rotated = new PlannerPolicy(twin, new MoveCount(plies), ceiling);
                        Pcg32 first = Pcg32.Seeded((ulong)campaign.Slot, (ulong)seed);
                        Pcg32 second = Pcg32.Seeded((ulong)campaign.Slot, (ulong)seed);
                        LotState state = rules.Start;
                        while (rules.Evaluate(state) is Outcome.InPlay)
                        {
                            legal.Clear();
                            rules.AppendLegalMoves(state, legal);
                            Exit move = original.Intend(state, legal, first);

                            Assert.Equal(move, rotated.Intend(state, legal, second));
                            movesCompared++;
                            if (level.VehicleOf(move.Piece).StartsCovered)
                            {
                                break;
                            }

                            // Only open vehicles have left so far, and they have the same colours in both lots.
                            var applied = (MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, move);
                            original.Observe(applied.Events);
                            rotated.Observe(applied.Events);
                            state = applied.Next;
                        }
                    }
                }
            }

            Assert.True(twins >= 8, "Only " + twins + " covered levels have hidden colours to rotate.");
            Assert.True(movesCompared > twins * 6);
        }

        [Fact]
        public void A_cover_the_reading_fixes_is_not_a_guess()
        {
            // The brief's 6x6: "3 not R" and the ledger fix both covers at D1, so their lifts are no guesses. The test
            // picks the moves (a cover when one is free, else Greedy's first best move); the bot reads before each one.
            LotLevel level = Load(SixBySix());
            var rules = new JamRules(level);
            var policy = new PlannerPolicy(level, new MoveCount(1), DeductionRung.D1);
            var greedy = new GreedyPolicy(level);
            var legal = new List<Exit>();
            var best = new List<Exit>();
            int lifts = 0;
            LotState state = rules.Start;
            while (rules.Evaluate(state) is Outcome.InPlay)
            {
                legal.Clear();
                rules.AppendLegalMoves(state, legal);
                policy.Intend(state, legal, Pcg32.Seeded(2, 5));
                greedy.BestMoves(state, legal, best);
                Exit played = legal.Where(move => level.VehicleOf(move.Piece).StartsCovered).DefaultIfEmpty(best[0]).First();
                var applied = (MoveResult<LotState, ExitRejection>.Applied)rules.TryApply(state, played);
                policy.Observe(applied.Events);
                lifts += applied.Events.OfType<PuzzleEvent.CoverLifted>().Count();
                state = applied.Next;
            }

            Assert.Equal(2, lifts);
            Assert.Equal(Option.Some(GuessTally.None), policy.Guesses);
        }

        [Fact]
        public void A_tally_counts_each_guess_as_right_or_wrong()
        {
            GuessTally tally = GuessTally.None.After(wasRight: true).After(wasRight: false).After(wasRight: false);

            Assert.Equal((0, 0), (GuessTally.None.Right, GuessTally.None.Wrong));
            Assert.Equal((1, 2), (tally.Right, tally.Wrong));
            Assert.Equal(GuessTally.None.After(wasRight: false).After(wasRight: true), GuessTally.None.After(wasRight: true).After(wasRight: false));
        }

        [Fact]
        public void A_planner_believes_what_it_sees_and_has_no_belief_about_a_cover_before_its_first_move()
        {
            LotLevel level = Load(SixBySix());
            var policy = new PlannerPolicy(level, new MoveCount(1), DeductionRung.D1);

            Assert.Equal(Red, policy.Believes(new PieceId(1)));
            Assert.Throws<InvalidOperationException>(() => policy.Believes(new PieceId(3)));
        }

        [Fact]
        public void A_planner_looks_at_least_one_move_ahead_and_reads_covers_at_d1_or_d2()
        {
            LotLevel level = Load(SixBySix());

            Assert.Throws<ArgumentOutOfRangeException>(() => new PlannerPolicy(level, new MoveCount(0), DeductionRung.D1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlannerPolicy(level, new MoveCount(1), DeductionRung.D3));
        }

        [Fact]
        public void A_lift_before_the_first_move_or_of_a_vehicle_never_covered_is_an_engine_fault()
        {
            LotLevel level = Load(SixBySix());
            var rules = new JamRules(level);
            var legal = new List<Exit>();
            rules.AppendLegalMoves(rules.Start, legal);
            var unread = new PlannerPolicy(level, new MoveCount(1), DeductionRung.D1);
            var started = new PlannerPolicy(level, new MoveCount(1), DeductionRung.D1);
            started.Intend(rules.Start, legal, Pcg32.Seeded(1, 1));

            Assert.Throws<InvalidOperationException>(() => unread.Observe(ReadOnlyArray.Of<PuzzleEvent>(new PuzzleEvent.CoverLifted(new PieceId(3), Blue))));
            Assert.Throws<InvalidOperationException>(() => started.Observe(ReadOnlyArray.Of<PuzzleEvent>(new PuzzleEvent.CoverLifted(new PieceId(0), Blue))));
        }

        /// <summary>The level with each cover's colour moved to the cover before it (by PieceId, the last to the first).</summary>
        private static LotLevel WithHiddenColoursRotated(LotLevel level)
        {
            LotDefinition definition = Assert.IsType<LotTextResult.Read>(LotText.Read(LotText.Write(level))).Definition;
            int[] covers = Enumerable.Range(0, definition.Vehicles.Count).Where(index => definition.Vehicles[index].StartsCovered).ToArray();
            var vehicles = definition.Vehicles.ToArray();
            for (int position = 0; position < covers.Length; position++)
            {
                vehicles[covers[position]] = vehicles[covers[position]] with
                {
                    Colour = definition.Vehicles[covers[(position + 1) % covers.Length]].Colour,
                };
            }

            return Load(definition with { Vehicles = ReadOnlyArray.CopyOf(vehicles) });
        }

        /// <summary>
        /// The Planner's play written out from Q71 and TC23 on the public parts: read before the first move and after each
        /// lift; draw a guess when there is none or the reading no longer fits it; then draw among the best first moves.
        /// </summary>
        private sealed class ReplicaPlanner
        {
            private readonly LotLevel _level;
            private readonly LotModel _model;
            private readonly MoveCount _plies;
            private readonly DeductionRung _ceiling;
            private readonly ColourView _view;
            private SeenSet _seen;
            private CoverReading? _reading;
            private bool _hasGuessed;

            public ReplicaPlanner(LotLevel level, int plies, DeductionRung ceiling)
            {
                _level = level;
                _model = new LotModel(level);
                _plies = new MoveCount(plies);
                _ceiling = ceiling;
                _view = new ColourView(level);
            }

            public GuessTally Tally { get; private set; } = GuessTally.None;

            public int Redraws { get; private set; }

            public int Keeps { get; private set; }

            public ColourId Believes(PieceId piece) => _view.Of(piece);

            public Exit Intend(LotState state, List<Exit> legal, Pcg32 replay)
            {
                if (_reading is null)
                {
                    CoverReading reading = LotClues.Read(_level, _seen, _ceiling);
                    bool keepsGuess = _hasGuessed && CoverGuesses.Fits(reading, reading.Covers.Select(cover => _view.Of(cover.Cover)).ToList());
                    if (keepsGuess)
                    {
                        Keeps++;
                    }
                    else
                    {
                        Redraws += _hasGuessed ? 1 : 0;
                        int count = CoverGuesses.Count(reading);
                        ReadOnlyArray<ColourId> guess = CoverGuesses.At(reading, count >= 2 ? replay.NextIndex(count) : 0);
                        for (int position = 0; position < guess.Count; position++)
                        {
                            _view.Guess(reading.Covers[position].Cover, guess[position]);
                        }

                        _hasGuessed = true;
                    }

                    _reading = reading;
                }

                var best = new List<Exit>();
                Lookahead.BestFirstMoves(_model, _view, LotPosition.Of(state), _plies, legal, best);
                return best.Count >= 2 ? best[replay.NextIndex(best.Count)] : best[0];
            }

            public void Observe(ReadOnlyArray<PuzzleEvent> events)
            {
                foreach (PuzzleEvent.CoverLifted lifted in events.OfType<PuzzleEvent.CoverLifted>())
                {
                    CoverColours read = _reading!.Covers.Single(cover => cover.Cover == lifted.Piece);
                    if (read.Colours.Count >= 2)
                    {
                        Tally = Tally.After(wasRight: _view.Of(lifted.Piece) == lifted.Colour);
                    }

                    _seen = _seen.With(new HiddenCell(lifted.Piece.Value));
                    _view.Seen(lifted.Piece, lifted.Colour);
                    _reading = null;
                }
            }
        }
    }
}

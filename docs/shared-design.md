# Shared design: Covered Lot and Sealed Vials

This file is the one copy of the design that Covered Lot and Sealed Vials share: the engine, its rules and records, the analytics schema, the money and ad rules, the daily, the pointers to each game's plan, and the rules for the two Claude Code sessions that build the games. The briefs (`CoveredLot.md` and `SealedVials.md`) point here and do not repeat it. In a brief, "shared §1" to "shared §8" mean the numbered sections below.

- **Changes:** small and additive, merged as soon as their checks pass (Q53). Some changes need the user's approval first; "Contracts" under "Coordination between the two sessions" lists them.
- **Sketches:** a phase that changes a type sketched here updates the sketch in the same branch.
- **Ids:** decision ids (Q1-Q56, TC1-TC19, U1-U10) are those in Appendix A of Covered Lot's plan, `CoveredLot/docs/development-plan.md`. Phase ids (P0-P19, W1, W2) are that plan's phases, unless a line names the Sealed Vials plan.
- **Sessions:** a *game session* is a play session (§7). In names such as `ThisSession` and `session_start`, "session" means a game session. "The two sessions" are the Claude Code sessions that build the games: the Covered Lot session and the Sealed Vials session.
- **Sources:** `GAME_DESIGN.md` (Rule of Rows), the report (`Puzzle-Type-Octalysis.md`) and the other source documents are in `CoveredLot/docs/sources` (Q12, Q38).

## Shared engine (used by both games)

Both games run on one deterministic C# engine. The client never decides the rules: the game, the generator, the bots and the server validator all call the same code. The engineering conventions follow the Rule of Rows design doc (`GAME_DESIGN.md` §15-16): netstandard2.1 / C# 9 libraries delivered to Unity as DLLs, the Block engine's build guards, golden packs and golden replays.

### 1. Layout

Each game has its own Unity project in its own repo. The engine and one shared client package live in the engine repo (Q55). Both Unity projects use the same Unity LTS: the newest LTS when the first session starts its Unity work (Q36). That session records the version under "Coordination between the two sessions".

```
puzzle-engine/                         (engine repo, shared by both games)
  Directory.Build.props / .targets     TreatWarningsAsErrors, overflow checks, layering rules
  src/Shared/IsExternalInit.cs         one linked file: C# 9 records on netstandard2.1 (Rule of Rows §15)
  src/PuzzleCore            netstandard2.1  ids, Option/results, ReadOnlyArray, Pcg32, rules interfaces,
                                            solver, grader, near-miss, offer eligibility, clue checker,
                                            records, PuzzleEvent
  src/PuzzleCore.Sort       netstandard2.1  SortState, Pour, SortRules, SortClues, SortGenerator
  src/PuzzleCore.Jam        netstandard2.1  LotState, Exit, JamRules, LotLoader, LotClues, LotGenerator
  src/PuzzleCore.Bot        netstandard2.1  skill-profile bots (shipped to Unity only if hints need them)
  src/PuzzleCore.Validation netstandard2.1  IReplayValidator, embedded-pack level source (no UGS refs)
  src/PuzzleMeta            netstandard2.1  Gold, GoldPack, Money, CurrencyCode, real-money labels (no rules)
  src/PuzzleClient          netstandard2.1  client rules: Interstitials.After (pure; integer inputs, no clock)
  src/PuzzleCore.Gen        net10.0         offline pack writer: JSON packs, presentation files,
                                            build reports, grading reports (CSV)
  src/PuzzleCore.Cli        net10.0         gen / validate / sim / bench / replay / demo (text renderer)
  src/Server.CloudCode      net (UGS)       thin adapter: request -> IReplayValidator -> response
  tests/PuzzleCore.Tests    net10.0, xUnit  golden, property and guard tests (tr-TR / sv-SE culture)
  clients/Playtest          net10.0         Blazor playtest harness (not shipped; not in the sln)
  packs/sort/  packs/jam/                   packs + presentation files + golden fingerprints
  (shared client package)                   Shared.Client: offer-screen logic, analytics adapter, game-session
                                            and day counters, input, UI kit -> PuzzleCore, PuzzleMeta, PuzzleClient
covered-lot/                           (Covered Lot's game repo, with its own Unity project)
  Assets/Generated/PuzzleCore/         DLLs + golden replays staged by sync-engine.sh (precompiled refs)
  Assets/CoveredLot/   CoveredLot.Client.asmdef     -> Shared.Client, PuzzleCore, PuzzleCore.Jam
  Assets/Tests/        Client.Tests.asmdef          EngineSmokeTest: replays the golden records
(Sealed Vials' game repo)              (its own Unity project, laid out the same way)
  Assets/SealedVials/  SealedVials.Client.asmdef    -> Shared.Client, PuzzleCore, PuzzleCore.Sort
```

- **Layering:** `Sort` and `Jam` depend only on `PuzzleCore` and never on each other (nor would a later hybrid module). `Bot` references Core and Jam (P5); Sort is added when Sort's bots are built. `Validation` references Core and Jam (P6); Sort is added when the Sort module is built. `PuzzleMeta` depends on nothing in the engine, holds no rules and is never referenced by Core, Sort, Jam, Bot or Validation. `PuzzleClient` references PuzzleCore (P8). Nothing under `src/` (except the Cloud Code adapter) references UnityEngine or UGS. Enforce this with the `UnityBoundApiPolicy` and the architecture guard tests.
- **Shared client package:** logic and plain building blocks only: the offer-screen logic, the analytics adapter, the game-session and day counters, input and the UI kit. Each game styles its own screens. The package has its own tests, which both Unity projects run. The first session to reach its Unity work creates it in the engine repo and names its folder; the other extends it additively (Covered Lot: P11).
- **No extra runtime DLLs:** Core uses its own `ReadOnlyArray<T>` instead of `ImmutableArray<T>`, so `sync-engine.sh` does not have to stage System.Collections.Immutable for Unity.
- **Portability:** the Cloud Code adapter is the only piece that depends on UGS. To move the server later, write a new adapter (for example an ASP.NET endpoint) around the same `IReplayValidator`.

### 2. Core abstractions (sketches; C# 9, closed sets as closed types)

The sketches follow the code in `src/`. Types that the code does not have yet are marked with the phase that builds them.

```csharp
// Identity/unit values: each gets its own type, so two of them cannot be swapped at a call site.
public readonly struct ColourId      : IEquatable<ColourId>      { public byte Value { get; } }  // 1-15 (0 = empty in packing)
public readonly struct VialId        : IEquatable<VialId>        { public byte Value { get; } }  // Sort
public readonly struct LayerIndex    : IEquatable<LayerIndex>    { public byte Value { get; } }  // Sort: 0 = bottom
public readonly struct PieceId       : IEquatable<PieceId>       { public byte Value { get; } }  // Jam: ascending anchor order
public readonly struct SlotIndex     : IEquatable<SlotIndex>     { public byte Value { get; } }  // Jam holding row
public readonly struct HiddenCell    : IEquatable<HiddenCell>    { public byte Value { get; } }  // Sort: vial×4+layer; Jam: PieceId
public readonly struct LevelIndex    : IEquatable<LevelIndex>    { public int Value { get; } }
public readonly struct ActionIndex   : IEquatable<ActionIndex>   { public int Value { get; } }   // into ActionRecord.actions
public readonly struct MoveCount     : IEquatable<MoveCount>     { public int Value { get; } }
public readonly struct StepCount     : IEquatable<StepCount>     { public int Value { get; } }   // deduction steps
public readonly struct PerMille      : IEquatable<PerMille>      { public int Value { get; } }   // validated 0..1000
public readonly struct CapacityCount : IEquatable<CapacityCount> { public int Value { get; } }   // vials (Sort) or slots (Jam)
public readonly struct OfferCount    : IEquatable<OfferCount>    { public int Value { get; } }   // fail screens shown
public readonly struct AdCount       : IEquatable<AdCount>       { public int Value { get; } }   // rewarded ads watched
public readonly struct NodeBudget    : IEquatable<NodeBudget>    { public int Nodes { get; } }   // nodes allowed (at least 1)
public readonly struct NodeCount     : IEquatable<NodeCount>     { public int Value { get; } }   // nodes used
public readonly struct Option<T> { public bool TryGet(out T value); public T Or(T fallback); }  // Option.Some(x), Option.None<T>()
public readonly struct ReadOnlyArray<T> { /* wraps a private T[]; no System.Collections.Immutable */ }  // ReadOnlyArray.Of/CopyOf/Empty

// State: readonly structs packed into integers. Apply returns a new value, so state is never shared.
public interface IPuzzleState<TSelf> : IEquatable<TSelf> where TSelf : struct, IPuzzleState<TSelf>
{
    ExactKey Exact { get; }          // 128-bit FNV-1a over all packed words in id order: replays, rewind, golden tests
    CanonicalKey Canonical { get; }  // reduced: the solver's transposition table only
}
// SortState (not in code yet; the Sealed Vials plan): ≤14 vials (13 + one spare) × 4 layers × 4-bit ColourId = 4 ulongs, plus one ulong for the 56-bit
//            HiddenMask and the 4-bit vial count. Canonical: vial words sorted by (word, hidden-mask nibble).
// LotState:  exited mask (≤24 bits) + slot order (≤8 slots × 4 bits, i.e. 7 + one spare) + slot count. Geometry
//            lives in the level. Canonical: exited mask + slot count (slot order matters only to ExactKey).

public abstract record Outcome { private Outcome() { }
    public sealed record InPlay : Outcome;  public sealed record Won : Outcome;
    public sealed record Failed(FailKind Kind) : Outcome; }
public enum FailKind { NoLegalMove, SlotsFull }

public abstract record PuzzleEvent { private PuzzleEvent() { }   // closed set, as Rule of Rows' BoardEvent; add at the end only
    /* sealed, in code: Exited, CoverLifted, SetCleared, SpareAdded, Rewound, Won, Failed.
       Sort's Poured, LayerRevealed and VialCompleted are added at the end with the Sort module. */ }

public interface IPuzzleRules<TState, TMove, TRejection>    // TRejection: the game's closed set (Jam: ExitRejection)
    where TState : struct, IPuzzleState<TState> where TMove : struct, IEquatable<TMove> where TRejection : class
{
    void AppendLegalMoves(in TState state, List<TMove> into);              // stable order: ascending ids
    MoveResult<TState, TRejection> TryApply(in TState state, TMove move);  // Applied(next, events) | Illegal(reason)
    Outcome Evaluate(in TState state);
    Option<TState> TryWithOneSpare(in TState state);            // Sort: +1 empty vial; Jam: +1 slot.
                                                                // None at the packing limit.
    CapacityCount CapacityOf(in TState state);                  // capacity in play, spare included (for SpareAdded)
    RewindFence RewindFence { get; }                            // how far back a rewind may reach
}
// Moves: Sort `Pour(VialId From, VialId To)` (not in code yet); Jam `Exit(PieceId Piece)`.
public enum RewindFence { None, AtReveal }   // AtReveal: never back past the last move that revealed a hidden cell.
                                             // Both games use AtReveal (Q3, Q19): Jam since Covered Lot's P1, and Sort
                                             // when its rules are built. None stays in the contract; no game uses it.
// P6 (not in code yet): the "which cells are hidden now" query becomes a new optional interface next to
// IPuzzleRules, so existing implementers keep compiling (TC6). Jam implements it; Insight's refusal reads it.

public enum RandomPurpose { Layout, Palette, Scramble, Cover, Clues, Bot, Playout }  // names FIXED FOREVER
// Pcg32, Seed and SeedDerivation are ported from Block.Core.Randomness (FNV-1a-64 over UTF-8 -> SplitMix64).
// Seed.ForPack(pack) = SplitMix64 of FNV-1a-64("pack|" + id); SeedDerivation.Stream(seed, key) seeds PCG32 from
// SplitMix64(seed XOR FNV-1a-64(key)). StreamKey.ForGenerator / ForBot / ForPlayout build keys with string.Concat
// and explicit ToString(CultureInfo.InvariantCulture), never $"" interpolation (the IL guard bans it):
//   generator: "{purpose name}|{packId}:{levelIndex}:{try}"
//   bots:      "Bot|{packId}:{levelIndex}:{profile name}:{run}"
//   playouts:  "Playout|{packId}:{levelIndex}:{playout}"
// Use the name, never the enum ordinal. Add new purposes at the end only.

public interface ILevelGenerator<TLevel>   // not in code yet: a shared shape for the games' generators
{
    GenerateResult<TLevel> Generate(GeneratorVersion version, PackSeed seed, LevelIndex levelIndex,
                                    NodeBudget perTry);
}   // Generated(level, grade, tries) | Rejected(GenerateFailure reason). Reverse-scramble from solved.
    // At most 20 tries per slot, on the schedule pinned by the golden packs.
// In code today, Jam's generator is static: LotGenerator.Generate(PackId, LevelIndex, LotPlan), seeded from
// Seed.ForPack, with fixed 200,000-node budgets (LotCoverer too). It returns Generated(GeneratedLot)
// | Rejected(one TryFailure per try).

public abstract record SolveResult<TMove> { private SolveResult() { }
    public sealed record Solved(ReadOnlyArray<TMove> Path) : SolveResult<TMove>;  // Path.Length = Par
    public sealed record Unsolvable : SolveResult<TMove>;
    public sealed record BudgetExceeded(NodeCount NodesUsed) : SolveResult<TMove>; }
// Solver (not in code yet; Sort's, in the Sealed Vials plan): BFS for small boards, IDA* with an integer admissible
// heuristic otherwise; its transposition table is a fixed-size array keyed by CanonicalKey (bounded), where a
// collision replaces the old entry. Jam has its own feasibility DFS (LotSolver) behind the same SolveResult, with a
// fixed-size dead-mask table.
public interface IPositionSolver<TState, TMove>   // each game supplies one (Jam: LotPositionSolver)
{
    SolveResult<TMove> SolveFrom(in TState state, NodeBudget budget);  // full information, from any position in play
}

// P4 (not in code yet): LevelGrade, PlanGrade and LevelKind. DeductionGrade and DeductionRung exist.
public sealed record LevelGrade(PlanGrade Plan, Option<DeductionGrade> Deduction, LevelKind Kind);
public sealed record PlanGrade(MoveCount Par, PerMille DeadEnd, CapacityCount SpareNeed,
                               CapacityCount MinFreeCapacity, Option<CapacityCount> PeakPar);  // build with named arguments
public abstract record DeductionGrade { private DeductionGrade() { }
    public sealed record Graded(DeductionRung Rung, StepCount Steps) : DeductionGrade;
    public sealed record NeedsGuess : DeductionGrade;
    public sealed record BudgetExceeded(NodeCount NodesUsed) : DeductionGrade; }
public enum DeductionRung { D1, D2, D3 }                                       // the one ladder: see the notes
public enum LevelKind { Plan, Think }                                          // the sawtooth reads this

public abstract record NearMiss { private NearMiss() { }         // THE single definition; computed on Failed only
    public sealed record Yes(MoveCount MovesFromWin) : NearMiss;   // solver wins from TryWithOneSpare(final); Jam: vehicles left
    public sealed record No : NearMiss;
    public sealed record SpareUnavailable : NearMiss;              // spare already used, or TryWithOneSpare is None
    public sealed record Unknown(NodeCount NodesUsed) : NearMiss; } // budget hit: log it, never offer on it
public enum RewindReach { Winnable, NotWinnable, Unknown }       // does some rewind the attempt allows (1 to RewindLimit) win?
                                                                 // NotWinnable at limit 0; never offer a rewind on Unknown
public static class FailAnalysis   // generic over <TState, TMove, TRejection>; Failed only, otherwise InvalidOperationException
{
    public static NearMiss NearMissOf(Attempt<…> attempt, IPositionSolver<…> solver, NodeBudget budget);
    public static RewindReach RewindReachOf(Attempt<…> attempt, IPositionSolver<…> solver, NodeBudget budget);
}   // NearMissOf solves PeekSpare(). RewindReachOf solves PeekRewind(RewindLimit) once, because winning rewinds are
    // upward-closed. The caller passes the budget (no default until bench).
public enum FailCause { Capacity, Deduction, Unknown }           // P2, not in code yet; logs and debug lines only, see the notes

public sealed class Attempt<TState, TMove, TRejection>   // one attempt; the client and the replay validator both drive it
{
    public const int MaxRewind = 5;
    public Attempt(IPuzzleRules<TState, TMove, TRejection> rules, TState start, PlayMode mode);
    public StepResult<TRejection> Move(TMove move);
    public StepResult<TRejection> TakeSpare();               // once per attempt, on a fail only (Campaign only)
    public StepResult<TRejection> Rewind(MoveCount moves);   // 1 to RewindLimit moves, on a fail only (Campaign only)
    public StepResult<TRejection> UseInsight(HiddenCell cell);
    public StepResult<TRejection> Concede();
    public MoveCount RewindLimit { get; }    // min(MaxRewind, moves on the current line, and under AtReveal the moves
                                             // since the last move that revealed a hidden cell); ignores mode and outcome
    public Option<TState> PeekRewind(MoveCount moves);   // the state a rewind would restore; None outside 1 to RewindLimit
    public Option<TState> PeekSpare();                   // the state TakeSpare would give; None when used or at the limit
    public SeenSet Seen { get; }                         // the attempt's seen hidden cells
}
// StepResult = Done(events) | Illegal(the game's rejection: a free bump, never recorded) | Refused(InvalidReason).
// The mode is checked first (K7), then the end of the attempt, then the action's own rule.

public enum PlayMode { Campaign, DailyProof, Competitive, Casebook, WeeklyTwist, Siege }  // add at the end only
public enum FirstGame { InProgress, Completed }   // K24: no rewarded ad until the player's first game is completed
public sealed record RewardedAdCapState(FirstGame FirstGame, AdCount ThisAttempt, AdCount ThisSession, AdCount ThisUtcDay);
    // AllowsOneMore: FirstGame is Completed and every cap has room: 1 per attempt (K8), 2 per game session, 5 per UTC day (Q4)
public enum FailScreen { NotYetShown, AlreadyShown }   // a fail never shows a second screen
public enum SpareUse { Unused, Used }                  // one spare per attempt
public sealed record OfferContext(PlayMode Mode, NearMiss NearMiss, RewindReach RewindReach,
                                  OfferCount OffersShownThisSession, FailScreen ThisFail,
                                  SpareUse SpareThisAttempt, RewardedAdCapState AdCap);  // build both with named arguments
public enum PaidOption { SpareForGold, RewindForGold, AdForSpare, AdForRewind }  // listed in this order
public static class FailOffer { public static OfferSet Eligible(OfferContext context); }  // called on Failed only
// OfferSet = RetryOnly (no screen, not counted) | Screen(ReadOnlyArray<PaidOption> Paid) (free retry + paid; counted).
// RetryOnly unless Mode == Campaign && ThisFail == NotYetShown && OffersShownThisSession < 4. Otherwise Screen,
// shown and counted even when it holds no paid option:
//   SpareForGold   when NearMiss is Yes && SpareThisAttempt == Unused
//   RewindForGold  (1 to RewindLimit moves, length chosen by the player) when RewindReach == Winnable
//   AdForSpare if SpareForGold is offered, else AdForRewind if RewindForGold is; only when AdCap.AllowsOneMore (§7)
// Each check compares with the one value that allows an option, so a value outside its enum offers nothing.
// Pure function: no prices, no clock. The client passes the game-session and day counts in.

public abstract record Uniqueness { private Uniqueness() { }   // brute force over the few hidden cells
    public sealed record Unique(HiddenAssignment Answer) : Uniqueness;
    public sealed record Multiple(HiddenAssignment First, HiddenAssignment Second) : Uniqueness;
    public sealed record Contradiction : Uniqueness;
    public sealed record BudgetExceeded(NodeCount NodesUsed) : Uniqueness; }
public interface IClueModel   // implemented by SortClues (tags + shelf ledger) and LotClues (tags + ledger). Built from
{                             // the level, it models the level start; only public information enters it.
    ReadOnlyArray<HiddenCell> HiddenCells { get; }                   // ascending order
    void AppendCandidates(int position, List<ColourId> into);        // by position in HiddenCells; ascending ColourId
    bool IsConsistent(PartialAssignment assignment);                 // prunes early
}
// UniquenessCheck.Run(IClueModel model, NodeBudget budget) stops at the second solution.

// P5 (not in code yet): src/PuzzleCore.Bot.
public enum BotProfileId { Novice, Average, Expert, Greedy, Planner1, Planner3, Strong }
// Each game uses its own subset. Covered Lot (Q40): Greedy (prefers a free vehicle whose colour already waits in the
// bays; it reads every cover), Planner1 (1 ply, ceiling D1), Planner3 (3 plies, ceiling D2; the reference bot) and
// Expert (the solver). Sealed Vials: Novice, Average, Strong and Expert in its brief; its session settles them.
public interface IBotProfile { BotProfileId Id { get; } BotSearch Search { get; } PerMille Mistakes { get; } }
public abstract record BotSearch { private BotSearch() { }      // draws from RandomPurpose.Bot
    public sealed record Limited(MoveCount LookaheadPlies, DeductionRung Ceiling) : BotSearch;  // sees what players see
    public sealed record FullSolver : BotSearch; }              // Expert only: full information, within its NodeBudget
// Covered Lot's Greedy fits neither case yet: it reads every cover (Q40) and looks no move ahead. P5 adds its shape.
public enum BotEndReason { Won, NoLegalMove, SlotsFull, Cycle, StepCap }
// Every bot run terminates: on Failed, on a repeated ExactKey within the run (Cycle), or after Par × 3 moves
// (StepCap; the factor is an estimate). Covered Lot runs end in Won or SlotsFull.
// How Covered Lot's bots play (TC12; Sealed Vials settles its own in its session, Q39): it deduces up to its Ceiling and guesses the rest consistently with the ledger, the tags and
// the cells it knows; after each reveal it deduces again. A mistake is a random other legal move, at the profile's
// Mistakes rate (Covered Lot starts Planner1 at 150‰ and Planner3 at 50‰). Every draw comes from the run's Bot stream.
// Bots play through the shared Attempt, so the near miss keeps one definition, and they take no rescues.
// Bot results only describe levels until the difficulty pass (Covered Lot: P10); from then on, bot gates may reject.

public interface IReplayValidator { ReplayVerdict Validate(string recordJson); }   // in code; implemented in P6
// Valid(Outcome, MoveCount moves, bool spareUsed) | Invalid(ActionIndex at, InvalidReason reason)
// | UnknownLevel | UnsupportedVersion. (A Jam Failed(NoLegalMove) is Valid but logged as an engine fault.)
// P6 looks the level up by pack id and level id, then compares the fingerprint (TC13): UnknownLevel for a level it
// cannot find, UnsupportedVersion for a rulesVersion the engine does not implement.
// Replay.Run(rules, start, mode, actions) gives ReplayReport(Verdict, Events, Final ExactKey) for golden replays.
public enum InvalidReason { IllegalMove, SpareOutsideFail, SecondSpare, RewindOutsideFail, RewindOutOfRange,
                            BoosterInCompetitiveMode, InsightLimit, ActionAfterEnd }  // mode checked first; see §4
// P6 adds InsightUnavailable at the end (Q27, TC13).
```

**Notes on the abstractions**
- **No-guess certificate, two-solver shortcut.** A level ships only if all three hold: `Uniqueness` returns `Unique`, its answer equals the true colours of the hidden cells, and every tag is needed (the clue check: Q6, Q21); the technique grader returns `Graded` rather than `NeedsGuess`, so the clues pin every hidden cell before the first move; and the planner, given full information, returns `Solved` (Jam: with the level's own N bays). This avoids building a partial-information certifier. The exact rung is a generator rule, not part of this certificate (TC10).
- **Cap the board to keep the budgets small:** before the spare, start at ≤ 13 vials of capacity 4, and at ≤ 8×8 lots with ≤ 24 vehicles and ≤ 7 holding slots. Keep the hidden-cell count small. Measure solver, grader and uniqueness times with `bench` as each core is built, and set the default `NodeBudget`s from them (Covered Lot: P9, aiming at a fail screen that answers within 250 ms for 95% of fails on a mid-range Android phone, Q45). Until then, every caller passes its own budget.
- **One spare per attempt, in both games.** `FailOffer.Eligible` enforces it through `SpareThisAttempt`, and the validator through `Invalid(SecondSpare)`. The start caps (13 vials, 7 bays) mean the spare always fits. `TryWithOneSpare` returns `None` at the 14-vial / 8-slot packing limit, and a guard test checks it.
- **Grade fields, defined once (game-neutral):**

  | Field | Meaning |
  |---|---|
  | `Par` | Length of the shortest winning path, in moves (pours or exits). In the jam it equals the vehicle count, so it is constant |
  | `DeadEnd` | Per-mille of 1,000 seeded random legal playouts (`RandomPurpose.Playout`) that end in `Failed`, `Cycle` or `StepCap` |
  | `SpareNeed` | Spares needed beyond the level's own capacity to win. 0 for every shipped level |
  | `MinFreeCapacity` | Fewest free capacity units on the best line. Sort: empty vials on the par line. Jam: free bays at the peak, i.e. the slack N − `PeakPar`. Lower is tighter |
  | `PeakPar` | Optional, in capacity units. Jam: `Some(lowest peak bay use over winning orders)`; Sort: `None`. `Par` always means path length |
  | `DeductionRung` | The level's hardest deduction step, on the deduction ladder below. A needed what-if case split = `NeedsGuess`, rejected |

  Jam-only grader features (`GreedyWinPerMille`, branching, buried third, late-fail share) stay outside `PlanGrade`, in the CSV.
- **The deduction ladder (one text for both games; Q5, Q16, Q41, TC10).** Each brief gives examples in its own terms; the rungs are defined only here.
  - **D1:** one tag, with the ledger, fixes a hidden cell. Ledger completion is D1 too: once a colour's ledger count is used up by cells already fixed, that colour comes off every other hidden cell (Q41).
  - **D2:** two tags are needed together.
  - **D3:** a tag that works only through a hidden cell deduced earlier, or one tag or the ledger counting over several unknown cells. Forcing a colour by count over unknown cells is D3 (Q41).
  - A level's rung is its hardest step. A case split is `NeedsGuess`, and such levels are rejected.
  - **Exact rung (Q17):** every clue slot in a campaign grades exactly at its slot's rung, or is rejected. This is a generator rule, not part of the ship certificate (TC10). If a rung's build rate falls below 90%, the user gets the numbers first.
  - **In code:** Covered Lot's P3 switches `LotClues` to this ladder: covered levels are pinned first, then re-pinned once after a per-slot diff, with the user's approval (TC10). P3 also removes the third definition: the `DeductionRung` doc comment and the unused `ForUnknownCount` (Q5). Sort's grader follows this text; once P3 is on `master`, the code's ladder matches it.
- **Fail offer (identical in both games; confirmed, Q44):** every Campaign fail shows the offer screen until 4 have been shown in the game session (each one counts), then a free retry only; a concede never shows it. Each game's onboarding may hold the offer back on its first levels (Covered Lot: P10 sets the level where it first appears). Copy: "This offer only appears when one more vial (bay) can still win" and "A rewind of up to 5 moves can still win", never which length. Rewind is sold only there, and only when it can still win: no paid rewind on either HUD, no free undo. The rewarded ad grants +1 spare on `Yes`, otherwise a free rewind. A game session ends after 30 minutes in the background (§7).
- **Fail cause for analytics** (`FailCause`, in PuzzleCore next to `FailAnalysis`; built in Covered Lot's P2; Q2, Q20, Q23-Q26, TC1, TC5, TC9): a cause is given for every fail, and only for fails: never for a concede, a retry or a quit (Q23, Q24). The losing move is the move that left the last winnable position.
  - **The scan:** it goes back from the fail to the first winnable position, with one budgeted solve per position, using each game's position solver. A binary search is allowed, because a lost line never becomes winnable again. It never scans behind the spare. The caller passes a node budget for each position, with no default until the bench; the first overrun gives `Unknown` (TC5, TC9).
  - **Deduction:** the losing move involved a hidden cell, judged with the attempt's `SeenSet` from just before that move, because the losing move reveals its own cell (TC5). Jam: the losing move sent out a vehicle that started covered and was not in that `SeenSet` (Q2, Q20, Q25); the label looks only at the losing move's own vehicle. Sort: the losing move poured from or into a vial that held a layer that started hidden and was not in that `SeenSet` (Q20). This Sort label is a *proposal*: the Sealed Vials session settles it in its own grilling (Q39).
  - **Capacity:** every other losing move, including (Jam) every open vehicle.
  - **Unknown:** a solve hit its budget.
  - **Position:** the result carries `losing_move_back` (1 = the last move), absent when the cause is `Unknown` (Q23, TC9).

  It is logged for every fail and never shown to players or sold, because an exact "rewind to the last winnable position" sells the answer. The playtest's fail screen and Covered Lot's Unity debug overlay show it on a debug line (Q26, TC19); Sealed Vials' screens are its session's (Q47). The bench decides from measured cost whether the cause is worked out on the phone or offline from records (TC9; Covered Lot P9).
- **Rewind, the fence and the `SeenSet`:** in both games `Rewind(n)` restores the exact earlier state, where n counts `Move` actions, from 1 to the attempt's `RewindLimit`. A rewind never goes back past the last move that revealed a hidden cell (`RewindFence.AtReveal`, a shared rule: Q3, Q19), so a paid rewind sells planning, never a second look. It never removes a spare already taken. `UseInsight` never moves the fence; a move that reveals a cell already shown by Insight does.
  - The `SeenSet` (hidden cells seen revealed, or shown by `UseInsight`, which changes nothing else) belongs to the attempt (Q22). It lives outside the rules state, and the rules never read it. Replaying the record rebuilds it. It grows within the attempt.
  - In both games, "hidden" means not in the attempt's `SeenSet` (Q20).
  - A retry starts fresh (Q22, Q42): a new attempt, whose `SeenSet` holds only the level's Insight cell. Every other revealed cell is drawn hidden again (Jam: covered again), and pencil marks clear.
  - The client shows the `SeenSet` as lifted covers (Jam). Sealed Vials proposes automatic pencil marks for seen layers; its session settles that (Q39).
  - `Attempt` sees reveals through its events: today only Jam's `CoverLifted`. Sort's `LayerRevealed` joins there with the Sort module, or `AtReveal` does nothing for Sort.
- **Insight (Q27, Q28):** one Insight shows one hidden cell (`UseInsight(cell)`), in Campaign only.
  - Covered Lot: it works only on a covered vehicle that is still in the lot and not yet seen, and only while the attempt is in play. A refused Insight is not spent; the refusal is a new value, `InsightUnavailable`, at the end of `InvalidReason` (TC13; P6).
  - Covered Lot: the player picks the cover, once per level. The engine checks the limit per attempt (`InsightLimit`), the client per level (Q28).
  - Both refusals take a per-game input. Whether they bind Sealed Vials is decided in the Sealed Vials session's grilling (Q39).
- **Records:** `ActionRecord` contains:
  - format `pc-record`, `formatVersion`, `game` (`Sort`/`Jam`), `mode` (the `PlayMode` name), level `{packId, levelId, fingerprint}` and `rulesVersion`. From P4 on, level ids have the form `{packId}-{slot:0000}` (TC14);
  - the actions `Move`, `Rewind(n)`, `TakeSpare`, `UseInsight(HiddenCell)` and `Concede`, each with `sincePreviousMs` stamped by the client. Illegal moves cost nothing and are never recorded.

  `Undo` is reserved: neither game emits it, and it can be added later by addition. The validator returns `Invalid(at, BoosterInCompetitiveMode)` for `TakeSpare`, `Rewind` or `UseInsight` outside Campaign. The reader and writer are PuzzleCore's `RecordJson` (`Records/RecordJson.cs`), modelled on the Block engine's Classic `RecordJson`. The format is a public contract: it changes only by addition, with a version bump.
- **Packs (Q29, Q30, TC14; built in Covered Lot's P7):** one JSON container after Rule of Rows §16, for both games.
  - **Header:** format, formatVersion, packId, packVersion, `rulesVersion` (in place of Rule of Rows' `grammarVersion`: Covered Lot plan, Appendix C change 4), `generatorVersion` and `game`.
  - **Levels:** id `{packId}-{slot:0000}`, slot, fingerprint, and the canonical level text embedded as is.
  - **Retired:** a list of retired levels.
  - A rejected slot is left out of the pack and listed in the pack's build report. Players see level numbers without gaps.
  - **`generatorVersion`:** version 1 is the generator as it stands at P7, after the ladder switch and with the novelty gate (Covered Lot plan, Appendix C change 3). From version 1 on, any change that alters generated content bumps it, and old versions are not kept runnable (Q29).
  - Grades go in a separate `{packId}.presentation.json` with each level's grade fields. Players see no tiers or labels until the difficulty pass (TC11).
  - **Pins:** the covered-levels pin (P3, re-pinned once) and the golden pack pin: the SHA-256 of the canonical pack for `golden-gen` slots 0-99, plain and covered, with the presentation file pinned separately. Today's plain golden hash stays pinned (Q30, TC14).
- **Events:** golden replays compare the `PuzzleEvent` sequence, the outcome and the final `ExactKey`.
- **Server cost:** the validator replays the moves, which costs time linear in the move count. It never solves the board again.

### 3. Determinism rules

- [ ] Integer maths only in Core, Sort, Jam, Bot, Validation, PuzzleMeta and PuzzleClient. No `float`/`double`/`decimal` (IL guard). Ratios are `PerMille` integers.
- [ ] No `System.Random`, `DateTime`, `Stopwatch`, `Environment.TickCount` or `Guid.NewGuid` in the core. Time enters only as recorded `sincePreviousMs`. Game-session and day counts enter only through `OfferContext`, `RewardedAdCapState` and `LevelEndContext`.
- [ ] Never iterate `Dictionary`/`HashSet` where the order affects output. Use arrays indexed by id, or sort by key first.
- [ ] Stable sorting only: an explicit `(key, id)` tiebreak or insertion sort on small arrays. Never rely on `Array.Sort` stability.
- [ ] Hashes use FNV, never `string.GetHashCode` or `System.HashCode`. Hash code goes in `unchecked { }` blocks, each with a comment.
- [ ] Every string the engine emits, stream keys included, is built with explicit `ToString(CultureInfo.InvariantCulture)`, never `$""` interpolation. The `HostileCulture` tests run under tr-TR and sv-SE.
- [ ] Budgets count nodes, never wall-clock time, so the same seed gives the same pack on any machine. The CLI may add a wall-clock safety cap that **fails the run loudly** instead of emitting a different pack.
- [ ] A level is always judged under the `rulesVersion` / `generatorVersion` its pack names. A new version never re-judges old levels.

### 4. Testing

| Test | Checks |
|---|---|
| Golden replays | Each committed `pc-record` pins its expected verdict. A valid record reproduces the same `PuzzleEvent` sequence, outcome and final `ExactKey`; a record kept to be refused gives the same `Invalid` action index and reason (Jam's `six-by-six-rewind-across-reveal`: action 4, `RewindOutOfRange`). They also run in Unity's `EngineSmokeTest` |
| Golden generator packs | The golden pack pin: the SHA-256 of the canonical pack for `golden-gen` slots 0-99, plain and covered, per `generatorVersion`, including the try schedule; the presentation file is pinned separately, and today's plain golden hash stays. The covered-levels pin. Re-pinned only with the user's approval (Q30) |
| Apply/rewind property | For random legal move sequences, `Rewind(n)` returns the exact earlier `ExactKey`, no accepted rewind crosses the last reveal, and the `SeenSet` only grows within the attempt. `TryApply` never mutates its input |
| Key split | Sort: permuting identical vials changes `ExactKey` but not `CanonicalKey`. Jam: the same exited set with a different slot order shares the `CanonicalKey` and differs in `ExactKey` |
| Spare limit | `TryWithOneSpare` returns `None` at the packing limit (14 vials, 8 slots). A second `TakeSpare` in one attempt gives `Invalid(SecondSpare)` |
| Solver ⇔ validator | Every `Solved.Path` validates as `Valid(Won)`. Every illegal move injected into it gives `Invalid` at that index |
| Near-miss soundness | `NearMiss.Yes` ⇒ the validator accepts `TakeSpare` plus the solver path as a win. After a spare it is `SpareUnavailable`. No offer is ever built from `Unknown` |
| Offer eligibility | Table tests: a counted screen on every Campaign fail until 4 per game session, then `RetryOnly`; no paid option in any other mode; never a second screen per fail; no spare without `Yes` or after a spare; no rewind (gold or ad) unless `Winnable`; no ad that grants nothing, breaks a cap or comes before the first game is completed |
| Fail cause | Every fail gets a cause, and no other ending does. An overrun gives `Unknown`. `losing_move_back` is present for every cause except `Unknown` (P2) |
| Validator modes | Outside Campaign, `TakeSpare`, `Rewind` or `UseInsight` → `BoosterInCompetitiveMode`. In Campaign: `TakeSpare` / `Rewind` while not `Failed` → `SpareOutsideFail` / `RewindOutsideFail`; n outside 1 to the attempt's `RewindLimit` → `RewindOutOfRange`; a second `UseInsight` in one attempt → `InsightLimit`; any action after `Won` or `Concede` → `ActionAfterEnd`. From P6, a Covered Lot `UseInsight` outside play, or on a cell that is not a covered vehicle still in the lot and unseen → `InsightUnavailable`, and the Insight is not spent |
| Uniqueness | Each shipped clue level passes the clue check (Q6, Q21): `Unique`, with the answer equal to the true colours of the hidden cells and every tag needed, and `Graded` |
| Bot bands | Each profile's win and near-miss rates, per `BotEndReason`, on the calibration pack stay within the bands recorded at calibration with the user's approval. Re-recording them also needs it. Every run terminates |
| Money | `RealMoney.For(150 gold)` with $1.99 = 600 as the smallest pack gives 50 USD cents. JPY (0 minor units) rounds up to a whole yen. No gold pack gives `GoldNotSold` |
| Interstitials | A table test covers every rule of `Interstitials.After` (§7): `Skip` after every level that is not won, after a `DailyProof` or `Competitive` result, during onboarding, with `AdsRemoved`, at each K8 cap, within the first 72 hours, within 7 days of a purchase and after the day's rewarded-ad limit; `Show` only after a won level that breaks none of them |
| Guards | IL/determinism guards, layering guards, a consumer compile test with Block.Core and PuzzleCore together |

### 5. Shared analytics schema

Both games log the same events from the greybox onward, with `game` = `sort` or `jam`. The client stamps the time. The engine supplies the grades, near-miss and fail cause.

| Event | Fields |
|---|---|
| `level_start` | game, mode, level_id, seed, attempt_index (1-based, per level), set (`clue` = sealed/covered, or `plain`), kind (`think`/`plan`), plan_grade, deduction_grade (absent on plain) |
| `level_end` | result (win/fail/abandoned), ended_by (`won`/`no_legal_move`/`slots_full`/`retry`/`concede`/`quit`; quit = left the level or the app), moves, time_ms, par_stars, spare_used, gold_earned |
| `fail_detail` | near_miss (`yes`/`no`/`spare_unavailable`/`unknown`, by the single definition), moves_from_win, rewind_reach (`winnable`/`not_winnable`/`unknown`), fail_kind (`no_legal_move`/`slots_full`), fail_cause (`capacity`/`deduction`/`unknown`), losing_move_back (1 = the last move; absent when fail_cause is `unknown`), cleared_permille (Sort: share of layers in completed vials; Jam: share of vehicles exited). If the bench puts the fail cause offline, `validate` adds fail_cause and losing_move_back from the records, and the phone's event may lack them |
| `stall` | idle > 20 s with legal moves left |
| `offer_shown` / `offer_result` | offer_type (`spare_vial`/`spare_slot`, `rewind`, `ad_spare`/`ad_rewind`), rewind_moves (1-5, the length chosen; absent otherwise), price, real_money_label (currency + minor units, or `gold_not_sold`), gold_balance, offers_shown_this_session, accepted |
| `self_blame` | answer to the fail survey: `my_mistake` / `unfair` / `didnt_know`, plus survey_rate |
| `pencil_marks`, `insight_used` | count per level. Insight is earned only, ≤ 1 per level |
| `proof_result`, `share_card_tap` | solved, attempts, counted (true for the first attempt only: N = 1, §8), time_ms |
| `gold_change` | source (`win`/`daily`/`spare`/`rewind`/`purchase`/`test_grant`), amount (signed), balance |
| `iap_purchase`, `ad_rewarded`, `session_start`, `session_end` | The ad event carries the cap state and the grant (`spare`/`rewind`). `session_end` carries gold earned and spent |
| `interstitial_shown`, `remove_ads_purchased` | The interstitial event carries mode, level_id and the K8 and K24 cap state. `remove_ads_purchased` carries the price (currency + minor units) |

**Metric definitions (shared)**
- **Stall rate** = the share of clue-level attempts with at least one `stall` event.
- **Self-blame survey:** shown after every fail in the greybox, so there are enough answers to judge a 70% band. After release the starting value is 1 in 10 fails; each game sets its release rate in its plan (Covered Lot: P15). `survey_rate` is logged with each answer.
- **Player near-miss rate** is reported per `ended_by`. **Bot near-miss rate** is reported per `BotEndReason` (offline CSV column `bot_end_reason`).
- **Free-retry win rate** = the win rate of attempt k + 1 after a fail on attempt k, with no paid aid.
- **Gold per day** = the sum of positive `gold_change` amounts per tester per UTC day.

### 6. Game-specific vs shared

| Concern | Shared (PuzzleCore / PuzzleMeta / PuzzleClient / Shared.Client) | Sealed Vials (Sort) | Covered Lot (Jam) |
|---|---|---|---|
| State packing | `IPuzzleState`, `ExactKey`, `CanonicalKey` | vial words + hidden mask | exited mask + slot order; geometry in the level |
| Move | `IPuzzleRules` contract, typed rejection | `Pour(from, to)` | `Exit(piece)`: leaves only if the path is clear |
| Win/fail | `Outcome`, `FailKind` | all vials single-colour / no legal pour | lot cleared / all bays full after clears |
| Spare (rescue) | `TryWithOneSpare`, near-miss check, one per attempt | +1 empty vial (≤ 14) | +1 holding slot (≤ 8) |
| Rewind / reveals | exact `Rewind(n)` up to the fence (`AtReveal`), the attempt's `SeenSet` | seen layers as automatic pencil marks (*proposal*) | seen covers drawn as lifted |
| Clues | `IClueModel`, `Uniqueness`, the D1-D3 ladder, the clue check | tags + shelf ledger | tags on covered pieces + ledger (original rule set) |
| Generator | Pcg32, purposes, try schedule, the JSON pack container | reverse-pour scramble; `pc-sort` text | reverse-exit scramble, `LotLoader`; `pc-lot` text |
| Solver/grader/bots | `SolveResult`, BFS/IDA*, `LevelGrade`, `IBotProfile`, bot termination | heuristic: Σ (vials holding a colour − 1) | feasibility DFS; "blocked exits + slot pressure" bound only if `bench` needs it |
| Fail offer / records / validator / analytics / money | all shared | labels only | labels only |
| Presentation | the shared client package: offer-screen logic, analytics adapter, UI kit; each game styles its own screens | pour VFX, ledger UI | vehicle motion, slot row |

### 7. Money types, caps, game session and day

Money types live in `PuzzleMeta`, which references nothing in the engine. `Interstitials.After` lives in the client-rules project, `src/PuzzleClient`, which references PuzzleCore (TC7, TC15). The game-session and day counters live in the shared client package (`Shared.Client`), never in `PuzzleCore`. The engine sees only the counts. Covered Lot's P8 builds PuzzleMeta and PuzzleClient: every amount is an integer, and prices are inputs (Q43).

```csharp
public readonly struct Gold         : IEquatable<Gold>         { public int Value { get; } }
public readonly struct CurrencyCode : IEquatable<CurrencyCode> { public string Iso4217 { get; } }  // "USD", "TRY", "JPY"
public readonly struct ProductId    : IEquatable<ProductId>    { public string Value { get; } }    // store product id
public readonly struct Money        : IEquatable<Money>
{   // from the store product's localised price; minor units follow the currency (JPY has 0)
    public CurrencyCode Currency { get; } public long MinorUnits { get; }
}
public sealed record GoldPack(ProductId Product, Gold Contents, Money Price);
public static class RealMoney { public static RealMoneyLabel For(Gold price, IReadOnlyList<GoldPack> storefront); }
// RealMoneyLabel = Shown(Money equivalent) | GoldNotSold (no gold pack in this storefront). Equivalent =
// ceil(price.Value × pack.Price.MinorUnits / pack.Contents.Value) for the smallest pack (fewest Contents, ties by
// ProductId ordinal), per currency, in long maths as (a × b + c − 1) / c.
// Test: 150 gold with $1.99 = 600 gold → 50 USD cents. Formatting for display happens in the client.
```

- **Starting gold (both games, the report's values, unconfirmed):** 10 plain / 20 clue spike, +5 per star (1-3 stars, every win earns at least 1).
- **Daily gold note (estimate):** the report's ~150-200 gold a day assumes almost no stars. With at least 1 star per win, the floor is about 230 a day. With 2 stars per level it is about 290 a day, about 2 free rescues a day at the report's 150-gold spare. Re-derive this in the economy simulator (Covered Lot: P14). The greybox measures gold earned per UTC day per tester (`gold_change`).
- **Caps (Q4):** at most 4 fail screens per game session. Rewarded ads: at most 1 per attempt (K8), none in the player's first game (K24), 2 per game session and 5 per UTC day. When the first game ends, at the first won level or at the first level that ends, is decided in Covered Lot's P12 (its plan's Appendix C, change 2).
- **Game session (Q44):** runs from app foreground until the app has been in the background for 30 minutes. **Day:** the UTC day, with the same 00:00 UTC rollover as the daily (E1). The shared client package counts both and passes them into `OfferContext` / `RewardedAdCapState`, and into `LevelEndContext` for interstitials. The engine never reads a clock.

**Confirmed by the user on 2026-11-07 (both games):**
- **Gold is sold for real money.** The gold packs and the starter pack are real IAP from soft launch on. Every gold price shows its real-money equivalent on the same screen (`RealMoney.For`). `GoldNotSold` is left only for a storefront that has no gold pack.
- **Pastel Rows' ad rules carry over:** interstitials under K8's caps, rewarded ads under K8, and a Remove Ads purchase. Since then, K8 and K24 apply in full, narrowed by Q43 (Q33): see the ad rules below.

**Ad rules from the Block plan.** K8, K24 and T33 are recorded in the Block project's `docs/plan-ve-kararlar.md` (read only). Their ad rules are translated from Turkish below. **Here:** marks each place where these games differ.

*K8, ads:*
- **Rewarded ads**, in four places in the Block game: a continue after a loss (1 ad continue per attempt); doubling a level's reward; a daily chest (1 extra chest a day); a life (+1 life, at most 3 a day).
  - **Here:** until the meta design (P14), the fail screen is the only rewarded-ad placement (Q43). Its caps are under "Caps" above (Q4).
- **Interstitial limits:**
  - none in the first 3 days. **Here:** the 72 hours count as T33 and Q34 say below;
  - only after a game or a level has ended (K24 sets the exact placement);
  - between two interstitials, at least 4 minutes and at least 3 completed games. **Here:** only won levels count as completed games (Q33);
  - at most 6 a day;
  - none within 5 minutes after a purchase or a rewarded ad;
  - none before or during a competitive attempt. **Here:** also none after a `DailyProof` or `Competitive` result (Q35);
  - Remove Ads turns interstitials off completely; the rewarded ad stays optional.

*K24, the ad rules:*
- **Network:** AdMob through the official Unity plugin, with its version pinned. No other ad network at launch; more networks come later through AdMob mediation, once there is soft-launch data. No banner, app-open or rewarded interstitial ads.
- **Placement:** the interstitial shows once the attempt is final, after a transition of about 1 second with taps locked, only if the ad has loaded, and **before** the result screen opens, because of a Google Play rule (K8's note). K8's numbers do not change. **Here:** after wins only (Q32). `Interstitials.After` decides only once the attempt is final; the 1 s lock and the "only if loaded" check are client work (Q32; Covered Lot P15).
- **After a purchase:** no interstitial for 7 days after each purchase.
- **Remove Ads by spend:** Remove Ads switches on by itself once the player's total spend reaches its price. A refund of Remove Ads brings the interstitials back.
- **Rewarded-ad day limit:** once a player has watched N rewarded ads in a day, no interstitial shows for the rest of that day. N comes from the economy simulator. **Here:** the day is the UTC day, N starts at 3 (Q33), and the simulator re-derives it (P14).
- **The first game:** everyone is asked once, after the first finished game, a neutral birth-year question, and only the age band is stored. So no ad continue is offered in the player's first game (a small exception to K8's first placement); the gold continue stays open. **Here:** when the first game ends is decided in Covered Lot's P12 (see "Caps").
- **Consent and content:** Google's consent form in the EEA, the UK and Switzerland. No personalised ads under 18. Ad content is capped at G under 13 and at PG for everyone else, and the gambling, alcohol, social casino, dating, "get rich quick", sexual reference and sensational categories are blocked. **Here:** K24's own consent card for Turkey (KVKK) and its seller clause are not carried over, because the Block decision K37 voided them for the same accounts (Covered Lot plan, Appendix C change 9).
- **iOS tracking (ATT):** asked only of players 18 and over, after the first finished game and the regional consent; refusing changes nothing. On Android, the advertising-id permission stays and is managed with the consent.
- **Here (Q43):** K24's purchase side (the products and prices, its rule of no random products and no subscriptions, the 28-day pass, the under-18 purchase gate, the IAP plugin and its version, refunds of gold) is decided in production (Covered Lot P14 and P15). There, its no-random-products rule meets the 2026-11-07 "Paid random rewards: yes" below. Prices are inputs.

*T33, days and anchors:* daily counters reset at 00:00 UTC (E1), and weeks start on Monday at 00:00 UTC (K9). K8's "first 3 days" are 72 hours from the moment the server created the account, and a device with no account sees no interstitials. **Here:** with no account, the 72 hours count from the app's first launch (Q34).

**Interstitials and Remove Ads (both games):**
- **Rules:** `Interstitials.After` applies K8's caps and K24's ad rules above, with the differences marked (Q33, Q43). An interstitial comes only after a won level (Q32, Q33), decided once the attempt is final. Never on the fail path (between a fail and its offer, or right after a rescue), never after a `DailyProof` or `Competitive` result (Q35), and never during onboarding: onboarding is an input, and the difficulty pass sets its range (TC7; Covered Lot P10). The fail screen keeps the rewarded ad only, so the one Black Hat moment stays a single, opted-in offer.
- **Remove Ads:** a non-consumable, restorable purchase that removes interstitials only (K8). It also switches on by itself once total spend reaches its price, and a refund brings the interstitials back (K24). The rewarded ad stays an opt-in choice on the fail screen, with the same caps. It is sold in the shop beside the gold packs, never on the fail screen and never chained after another purchase.
- **Greybox:** no interstitials and no ads SDK. `Interstitials.After` and its tests are built in Covered Lot's P8; the ads SDK comes in P15.
- **Types:** `Interstitials.After` decides the ad slot; the engine never does.

```csharp
// src/PuzzleClient (P8; not in code yet): pure, with integer inputs, and it never reads a clock (TC7, TC15).
public enum AdEntitlement : byte { Standard, AdsRemoved }   // AdsRemoved: bought or restored from the store receipt, or
                                                             // unlocked by total spend (K24); a refund takes it back
public readonly struct InterstitialCapState { /* the counts K8, K24 and T33 need, as integers: time since the first
    launch, since the last interstitial, since the last purchase and since the last rewarded ad; won levels since the
    last interstitial; interstitials and rewarded ads this UTC day. P8 names the fields. */ }
public sealed record LevelEndContext(PlayMode Mode, Outcome Outcome, LevelIndex Level,
                                     AdEntitlement Entitlement, InterstitialCapState Caps);  // plus the onboarding input (P8)
public enum InterstitialSkipReason : byte { AdsRemoved, NotAWin, CompetitiveMode, Onboarding, CapReached }
// Each rule gets its own skip reason, so P8 adds reasons (TC15). CompetitiveMode replaces DailyMode and covers DailyProof
// and Competitive results (Q35). The enum is not built yet, so this is not a contract change.
public abstract record InterstitialSlot
{
    private InterstitialSlot() { }
    public sealed record Show : InterstitialSlot;
    public sealed record Skip(InterstitialSkipReason Reason) : InterstitialSlot;
}
public static class Interstitials { public static InterstitialSlot After(LevelEndContext context); }  // pure
```

**Decided by the user on 2026-11-07, second round (both games):**
- **Lives: yes**, with a refill timer.
- **One currency for now:** gold only (K12 holds, for now).
- **No boosters in competitive modes:** K7 holds.
- **Streaks: yes.** E9 does not carry over to these games.
- **Paid random rewards: yes** (for example chests or packs).

**Settled since then:**
- **The daily's counted attempts:** N = 1, in both games (Q9, §8).
- **K24:** it applies in full, narrowed by Q43 (Q33). Its ad rules are above, and its purchase side is decided in production. Under it the games are 13+ (K24's audience), with a minimum of iOS 15 (K24's floor; Covered Lot re-checks it against the chosen Unity LTS in P11). K24 allows no subscriptions, only a 28-day non-renewing pass; that is part of the purchase side. Real-money equivalents on gold prices were confirmed on 2026-11-07 (above).

**To design in the meta design, after the greybox, before any of it is built (Q11; Covered Lot P14).** None of it is in the greybox, and the money step (P8) builds gold, money and interstitials only. Sealed Vials' questions 2 (the streak) and 6 (paid random rewards) are decided with it. The Money sections and the mode tables still describe the game without these systems until this design is done.
- **Lives:** max lives, minutes per life, and what a fail, a quit and a restart cost (a deduction game needs free restarts for thinking). Also the out-of-lives screen, with the timer as the free route; gold and ad refills; and which modes use lives (Campaign only is the obvious default).
- **Streak:** the type (a Campaign win streak or a daily-play streak), its rewards, whether a paid rescue keeps it, streak protection, and what the fail screen says about it. The teen checklist bans loss-framed copy.
- **Paid random rewards:**
  - Contents: tokens for the existing gated actions (a spare token still needs `NearMiss.Yes`, a rewind token still needs `Winnable`) and cosmetics. Never Insight, never the answer, never gold for gold.
  - The price and a published odds table.
  - Where they are sold: the shop only, never on the fail screen, and never chained after a continue.
- **Engine placement:** lives, streaks and chests are meta (`PuzzleMeta`, `Shared.Client`, server), never `PuzzleCore`. The engine never reads a clock. Chest draws happen on the server, with a versioned odds table the client cannot predict.
- **Then update both briefs:** analytics events, tests, the Rules-by-mode columns, the Octalysis ratings (CD6, CD7 and CD8 go up) and the red lines. Meta rules and economy numbers kept in this file change only through small shared changes, agreed by both sessions and approved by the user (P14).

**Check before shipping paid random rewards in a 13+ game.** This comes from general knowledge, has not been re-verified here, and is not legal advice.
- Both stores require the odds of each item to be shown before purchase (App Store guideline 3.1.1; Google Play's payments policy).
- The age-rating questionnaires add "In-game purchases (includes random items)".
- Some markets restrict paid random rewards. Belgium treats paid loot boxes as gambling. Brazil's ECA Digital law bans them for minors. Australia rates games with paid loot boxes M or higher. South Korea requires published probabilities.
- Plan a per-storefront switch, and verify each market before launch.

### 8. Mode rules and the daily

- Only `Campaign` allows `TakeSpare`, `Rewind` and `UseInsight`. The validator enforces this for every other `PlayMode` (K7).
- **Daily (both games; the Proof and the Daily Lot are both `DailyProof`):**
  - It rolls over at **00:00 UTC** (E1).
  - **Seed (one format for both games, *proposal*):** the one-level pack `daily-{game}-{yyyy-MM-dd}` (UTC date, built like a stream key), whose seed is `Seed.ForPack` of that pack id (§2).
  - Only the **first attempt** counts toward "X% solved it unaided": N = 1 counted attempt, in both games (Q9). Later retries are practice: they are logged with `proof_result.counted = false` and not counted.
  - Rule of Rows chose 1 because a rule board's answer is revealed after the first attempt; K9's block daily uses 3.
  - No interstitial follows a `DailyProof` result (Q35, §7).

## Shared plan

The dated shared plan and the cross-game decision that stood here are retired, and the hybrid options shrink to the idea below. Each game has its own plan, with phases and gates and no dates (Q50):
- **Covered Lot:** `CoveredLot/docs/development-plan.md`.
- **Sealed Vials:** its own plan, written in the Sealed Vials session (Q50, Q56).

What changed:
- **In parallel:** the two games are built in parallel, each by its own Claude Code session (Q46). The old rule of one queue, Covered Lot first and Sealed Vials after it, no longer holds.
- **No cross-game decision:** the cross-game table and the joint-playtest gate are retired. Each game passes its own greybox bands and goes to its own release (Q52).
- **The hybrid stays an idea for later** (Q52): both games in one app, with sort and jam levels in one sawtooth map, or a jam-to-sort rule set as a new rules module that keeps `Sort` and `Jam` independent. Nothing is built for it.
- **The old "Done when" lines are gates now** (Covered Lot plan, Appendix C change 5). This is where each one stands:

| Old work | Done when | Gate now |
|---|---|---|
| Engine scaffold: guards, ids and counts, `Pcg32` and seeds | The guards fail on a planted `float` and a planted `System.Random`; golden values pass; the tests pass under tr-TR and sv-SE | Met before the plans. Each new Unity-bound project repeats the planted-type check (Covered Lot P5, P6 and P8), and every phase runs the tests in both cultures |
| Jam Stage 0 and the rules core | Stage 0 log and table written; the Jam golden rule tests, the apply/rewind property and the key-split test pass | Rules core and tests: met before the plans, with r=1 frozen (Q7). Stage 0's desk log: `CoveredLot/docs/stage0-originality.md`. The "Differs?" table: Covered Lot W1 |
| Records, `PuzzleEvent` and replay | A hand-written record replays to `Valid(Won)`; golden replays pass | Met before the plans. The validator project: Covered Lot P6 |
| Solver and `bench` | Solver ⇔ validator test passes; p50/p99 solve times recorded at 8×8 | "The solver and the validator agree": Covered Lot P6. Solve times at 8×8: P9 |
| Jam plain generator and pack writer | `gen` makes 1,000 plain levels, all `Solved` under `validate`, with zero loader rejects; generation rate recorded | Pack writer: Covered Lot P7. The 1,000-level gate run and the generation rate: P9 |
| Near miss, spare limit, rewind reach, fail offer, grades, playouts, fail cause | Offer-eligibility, near-miss-soundness and spare-limit tests pass | Met before the plans. `FailCause`: Covered Lot P2. Grades: P4 |
| Testers and default budgets | Tester list; budgets set | Default budgets: Covered Lot P9. Testers: P12 (greybox) and P15 (closed test) |
| Jam clue layer, bots, engineering gate and greybox pack | Covered levels `Unique` and `Graded`; engineering gate passes; first calibration table | Clue layer: met before the plans; each covered slot at its exact rung: Covered Lot P3. First calibration table: P5. Engineering gate: P9. Greybox pack: P12 |
| Sort Stage 0, plain core and generator | Sort golden rule, solver ⇔ validator and golden pack tests pass; 1,000 plain levels `Solved` | The Sealed Vials plan |
| Covered Lot `Play` scene and greybox | The Covered Lot greybox | Covered Lot P12's gate; the test itself is P13 |
| Sort clue layer, bots, `Play` scene and greybox | The Sealed Vials greybox | The Sealed Vials plan |
| Joint playtest | The cross-game decision | Retired (Q52) |
| Paid creative test (Stage 2) | | Covered Lot: decided in P16 with the rest of Q49, or brought forward at P13's gate |

## Coordination between the two sessions

Covered Lot and Sealed Vials are built in parallel, each by its own Claude Code session with its own plan (Q46, Q50). The Sealed Vials session starts after Covered Lot's P0, with its own `HANDOFF.md`, its worktree and a session card. It opens with its own grilling (Q39) and Sort Stage 0 (Q56), and its `HANDOFF.md` points here instead of copying these rules (Q56). Both sessions follow them; they come from the standing rules of Covered Lot's plan.

- **Worktrees (Q53):** the Covered Lot session works in `CoveredLot/puzzle-engine`. The Sealed Vials session works in a git worktree of the same repo at `SealedVials/puzzle-engine`, cut from `master` after P0.
- **Branches (Q1):** each phase that changes a repo gets its own branch, cut from `master` and merged once its checks pass. Neither worktree rests on `master`: each stays on its own branch between phases, because git won't update a branch that another worktree has checked out.
- **Merging:**
  1. Merge `master` into the phase branch and re-run the checks. Each session takes in the latest `master` before merging (Q53).
  2. Fast-forward `master` with `git fetch . <branch>:master`. If git refuses, `master` has moved: take it in again and repeat.
  3. Push `master` to GitHub.
- **Checks before a merge:** `dotnet test` under tr-TR and under sv-SE, no build warnings, and `dotnet build clients/Playtest`. Before an engine phase is merged, a read-only review checks the change, and hand-made mutants of the new code each make the tests fail (Covered Lot plan, Appendix C new item 2).
- **Shared parts:** PuzzleCore; the build files and the test setup; the golden pins; this file; the shared client package; the Unity editor version.
- **Contracts (Q53):** every change to a shared part is small and additive, PuzzleCore included, and it is merged as soon as its checks pass. The exceptions:
  - P3's removal of `ForUnknownCount` (Q5), the one exception approved with the plan. The Covered Lot session tells the Sealed Vials session before merging it;
  - re-pins, and edits to this file, that the user approves.

  Any other contract change is raised with the user first.
- **Pins and bands:** a golden hash or another shared pin is never re-pinned without the user's approval, and bot bands are never re-recorded without it (Q53; Covered Lot plan, Appendix C change 7). Stop and ask first.
- **Meta and economy:** meta rules and economy numbers kept in this file change only through small shared changes, agreed by both sessions and approved by the user (Covered Lot P14).
- **Records:** at each gate, each session adds a line to its own `HANDOFF.md`: the phase, the merge commit, the test counts in both cultures, and any approvals given. A phase that changes a type sketched here updates the sketch in the same branch.
- **Playtest:** only one session runs the playtest on port 5019 at a time.
- **Machine rules:** the rules for the machine both sessions run on (its other projects, its drives and its network mappings) live in each session's `HANDOFF.md`, not in this public file (U5, U6, U8).
- **Public repos (U8):** the engine repo and the game repos are public on GitHub until near the end of the project. No commit may hold a secret or personal data: no keys, keystores, passwords, tokens, or store or service credentials, and no personal email or machine-specific path. A scan of the commits runs before each push to `master`.
- **Workflows:** multi-agent workflows use 2 agents unless the user says otherwise (U9).

### Who delivers each shared piece

| Shared piece | Delivered by | In | Until then, the other session may assume |
|---|---|---|---|
| The rewind fence for Covered Lot (`JamRules.RewindFence` = `AtReveal`) | Covered Lot session | P1 (delivered) | `Attempt` already applies `AtReveal` for a game that names it. Sort names it, and adds `LayerRevealed` to the reveals `Attempt` sees, with its events |
| `FailCause`, next to `FailAnalysis`, and `losing_move_back` in `fail_detail` | Covered Lot session | P2 | No cause exists in code; §2's definition is the target. Each game supplies its own position solver and hidden check (TC1) |
| The deduction ladder in code: `LotClues` switched; `ForUnknownCount` and the third definition removed | Covered Lot session | P3 | The ladder text in §2 is the target; the code still grades with the old mapping. Until then, a Sort grader follows the §2 text, not the code's old mapping. The Covered Lot session tells the Sealed Vials session before merging the removal |
| Grades: `PlanGrade`, `LevelGrade`, `LevelKind`, the presentation file, level ids `{packId}-{slot:0000}` | Covered Lot session | P4 | Only `DeductionGrade` and `DeductionRung` exist |
| `PuzzleCore.Bot`, `IBotProfile` and `sim` | Covered Lot session | P5 | No bot code exists. Sort's profiles and bots are the Sealed Vials session's, added to the project additively |
| `PuzzleCore.Validation` (`IReplayValidator`), `InsightUnavailable`, the hidden-cell interface and `validate` | Covered Lot session | P6 | The interface is optional, so Sort's rules keep compiling without it (TC6). Whether the Insight refusals bind Sealed Vials is decided in its grilling |
| The JSON pack container, `generatorVersion`, the pack writer in `PuzzleCore.Gen` and the golden pack pin | Covered Lot session | P7 | Jam levels come from `LotPackBuilder` and `pc-lot` files. Sort uses the container once it is on `master` (Q29) |
| `PuzzleMeta` and the client-rules project `src/PuzzleClient` (`Interstitials.After`) | Covered Lot session | P8 | The §7 sketches are the target; prices are inputs (Q43) |
| `bench`, the default `NodeBudget`s and the PuzzleCore-family consumer compile test | Covered Lot session | P9 | No default budgets: every caller passes its own. Sort measures its own budgets as its core is built |
| Meta rules and economy numbers in §7 | Covered Lot session, agreed with the Sealed Vials session and approved by the user | P14 | §7's "To design" list; none of it is built |
| The shared client package (`Shared.Client`) | Whichever session reaches its Unity work first; the other extends it additively | Covered Lot: P11 | It does not exist yet. It goes in the engine repo |
| The Unity editor version | Whichever session reaches its Unity work first | Covered Lot: P11 | None is chosen yet; it is recorded below |
| Sort's modules: `PuzzleCore.Sort`, Sort's events, its position solver, `SortClues`, its generator and its bots | Sealed Vials session | The Sealed Vials plan | Covered Lot assumes nothing from them. Any PuzzleCore change they need is small and additive (Q53) |

**Unity editor version:** not chosen yet. The first session to start its Unity work installs the newest Unity LTS (Q36), records the exact version here and creates the shared client package. The other session uses the same version (Q55).

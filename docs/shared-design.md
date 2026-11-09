## Shared engine (used by both games)

Both prototypes run on one deterministic C# engine. The client never decides the rules: the game, the generator, the bots and the server validator all call the same code. The engineering conventions follow the Rule of Rows design doc (`GAME_DESIGN.md` §15-16): netstandard2.1 / C# 9 libraries delivered to Unity as DLLs, the Block engine's build guards, golden packs and golden replays.

### 1. Layout

The engine goes in a new repository and the Unity 6 LTS project in a second one. Use one Unity version for the whole project.

```
puzzle-engine/                         (engine repo)
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
  src/PuzzleCore.Gen        net10.0         offline pack builder, grading reports (CSV)
  src/PuzzleCore.Cli        net10.0         gen / validate / sim / bench / replay / demo (text renderer)
  src/Server.CloudCode      net (UGS)       thin adapter: request -> IReplayValidator -> response
  tests/PuzzleCore.Tests    net10.0, xUnit  golden, property and guard tests (tr-TR / sv-SE culture)
  packs/sort/  packs/jam/                   packs + presentation files + golden fingerprints
unity-client/                          (Unity 6 LTS project)
  Assets/Generated/PuzzleCore/         DLLs + golden replays staged by sync-engine.sh (precompiled refs)
  Assets/Shared/       Shared.Client.asmdef         offer screen, analytics adapter, session/day counters,
                                                    input, UI kit -> PuzzleCore, PuzzleMeta
  Assets/SealedVials/  SealedVials.Client.asmdef    -> Shared.Client, PuzzleCore, PuzzleCore.Sort
  Assets/CoveredLot/   CoveredLot.Client.asmdef     -> Shared.Client, PuzzleCore, PuzzleCore.Jam
  Assets/Tests/        Client.Tests.asmdef          EngineSmokeTest: replays the golden records
```

- **Layering:** `Sort` and `Jam` depend only on `PuzzleCore` and never on each other (nor would a later hybrid module). `Validation` depends on Core, Sort and Jam. `PuzzleMeta` depends on nothing in the engine, holds no rules and is never referenced by Core, Sort, Jam, Bot or Validation. Nothing under `src/` (except the Cloud Code adapter) references UnityEngine or UGS. Enforce this with the `UnityBoundApiPolicy` and the architecture guard tests.
- **No extra runtime DLLs:** Core uses its own `ReadOnlyArray<T>` instead of `ImmutableArray<T>`, so `sync-engine.sh` does not have to stage System.Collections.Immutable for Unity.
- **Portability:** the Cloud Code adapter is the only piece that depends on UGS. To move the server later, write a new adapter (for example an ASP.NET endpoint) around the same `IReplayValidator`.

### 2. Core abstractions (sketches; C# 9, closed sets as closed types)

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
public readonly struct NodeBudget    { public int Nodes { get; } }                               // nodes allowed
public readonly struct NodeCount     : IEquatable<NodeCount>     { public int Value { get; } }   // nodes used
public readonly struct Option<T> { public bool TryGet(out T value); public static Option<T> None { get; } }
public readonly struct ReadOnlyArray<T> { /* wraps a private T[]; no System.Collections.Immutable */ }

// State: readonly structs packed into integers. Apply returns a new value, so state is never shared.
public interface IPuzzleState<TSelf> : IEquatable<TSelf> where TSelf : struct, IPuzzleState<TSelf>
{
    ExactKey Exact { get; }          // 128-bit FNV-1a over all packed words in id order: replays, rewind, golden tests
    CanonicalKey Canonical { get; }  // reduced: the solver's transposition table only
}
// SortState: ≤14 vials (13 + one spare) × 4 layers × 4-bit ColourId = 4 ulongs, plus one ulong for the 56-bit
//            HiddenMask and the 4-bit vial count. Canonical: vial words sorted by (word, hidden-mask nibble).
// LotState:  exited mask (≤24 bits) + slot order (≤8 slots × 4 bits, i.e. 7 + one spare) + slot count. Geometry
//            lives in the level. Canonical: exited mask + slot count (slot order matters only to ExactKey).

public abstract record Outcome { private Outcome() { }
    public sealed record InPlay : Outcome;  public sealed record Won : Outcome;
    public sealed record Failed(FailKind Kind) : Outcome; }
public enum FailKind { NoLegalMove, SlotsFull }

public abstract record PuzzleEvent { private PuzzleEvent() { }   // closed set, as Rule of Rows' BoardEvent
    /* sealed: Poured, LayerRevealed, VialCompleted, Exited, CoverLifted, SetCleared,
               SpareAdded, Rewound, Won, Failed */ }

public interface IPuzzleRules<TState, TMove, TRejection>    // TRejection: the game's closed set (Jam: ExitRejection)
    where TState : struct, IPuzzleState<TState> where TMove : struct, IEquatable<TMove>
{
    void AppendLegalMoves(in TState state, List<TMove> into);              // stable order: ascending ids
    MoveResult<TState, TRejection> TryApply(in TState state, TMove move);  // Applied(next, events) | Illegal(reason)
    Outcome Evaluate(in TState state);
    Option<TState> TryWithOneSpare(in TState state);            // Sort: +1 empty vial; Jam: +1 slot.
}                                                               // None at the packing limit.
// Moves: Sort `Pour(VialId From, VialId To)`; Jam `Exit(PieceId Piece)`.

public enum RandomPurpose { Layout, Palette, Scramble, Cover, Clues, Bot, Playout }  // names FIXED FOREVER
// Port Pcg32, Seed, SeedDerivation from Block.Core.Randomness (FNV-1a-64 over UTF-8 -> SplitMix64).
// StreamKey.For(...) builds keys with string.Concat and explicit ToString(CultureInfo.InvariantCulture),
// never $"" interpolation (the IL guard bans it):
//   generator: "{purpose name}|{packId}:{levelIndex}:{try}"
//   bots:      "Bot|{packId}:{levelIndex}:{profile name}:{run}"
//   playouts:  "Playout|{packId}:{levelIndex}:{playout}"
// Use the name, never the enum ordinal. Add new purposes at the end only.

public interface ILevelGenerator<TLevel>
{
    GenerateResult<TLevel> Generate(GeneratorVersion version, PackSeed seed, LevelIndex levelIndex,
                                    NodeBudget perTry);
}   // Generated(level, grade, tries) | Rejected(GenerateFailure reason). Reverse-scramble from solved.
    // At most 20 tries per slot, on the schedule pinned by the golden packs.

public abstract record SolveResult<TMove> { private SolveResult() { }
    public sealed record Solved(ReadOnlyArray<TMove> Path) : SolveResult<TMove>;  // Path.Length = Par
    public sealed record Unsolvable : SolveResult<TMove>;
    public sealed record BudgetExceeded(NodeCount NodesUsed) : SolveResult<TMove>; }
// Solver: BFS for small boards, IDA* with an integer admissible heuristic otherwise (Jam: its own feasibility DFS
// behind the same SolveResult). The transposition table is a fixed-size array keyed by CanonicalKey (bounded);
// a collision replaces the old entry (eviction policy).

public sealed record LevelGrade(PlanGrade Plan, Option<DeductionGrade> Deduction, LevelKind Kind);
public sealed record PlanGrade(MoveCount Par, PerMille DeadEnd, CapacityCount SpareNeed,
                               CapacityCount MinFreeCapacity, Option<CapacityCount> PeakPar);  // build with named arguments
public abstract record DeductionGrade { private DeductionGrade() { }
    public sealed record Graded(DeductionRung Rung, StepCount Steps) : DeductionGrade;
    public sealed record NeedsGuess : DeductionGrade;
    public sealed record BudgetExceeded(NodeCount NodesUsed) : DeductionGrade; }
public enum DeductionRung { D1, D2, D3 }                                       // game-neutral, see notes
public enum LevelKind { Plan, Think }                                          // the sawtooth reads this

public abstract record NearMiss { private NearMiss() { }         // THE single definition; computed on Failed only
    public sealed record Yes(MoveCount MovesFromWin) : NearMiss;   // solver wins from TryWithOneSpare(final); Jam: vehicles left
    public sealed record No : NearMiss;
    public sealed record SpareUnavailable : NearMiss;              // spare already used, or TryWithOneSpare is None
    public sealed record Unknown(NodeCount NodesUsed) : NearMiss; } // budget hit: log it, never offer on it
public enum RewindReach { Winnable, NotWinnable, Unknown }       // does some rewind of 1-5 moves reach a winnable position?
public enum FailCause { Capacity, Deduction, Unknown }           // logs only, never shown or sold

public enum PlayMode { Campaign, DailyProof, Competitive, Casebook, WeeklyTwist, Siege }  // add at the end only
public sealed record RewardedAdCapState(AdCount ThisFail, AdCount ThisSession, AdCount ThisUtcDay);
public sealed record OfferContext(PlayMode Mode, NearMiss NearMiss, RewindReach RewindReach,
                                  OfferCount OffersShownThisSession, bool AlreadyOfferedThisFail,
                                  bool SpareUsedThisAttempt, RewardedAdCapState AdCap);  // build both with named arguments
public enum PaidOption { SpareForGold, RewindForGold, AdForSpare, AdForRewind }  // listed in this order
public static class FailOffer { public static OfferSet Eligible(OfferContext context); }  // called on Failed only
// OfferSet = RetryOnly (no screen, not counted) | Screen(ReadOnlyArray<PaidOption>) (free retry + paid; counted).
// RetryOnly unless Mode == Campaign && !AlreadyOfferedThisFail && OffersShownThisSession < 4. Otherwise Screen,
// shown and counted even when it holds no paid option:
//   SpareForGold   when NearMiss is Yes && !SpareUsedThisAttempt
//   RewindForGold  (1-5 moves, length chosen by the player) when RewindReach == Winnable
//   AdForSpare if SpareForGold is offered, else AdForRewind if RewindForGold is; only within the ad caps (§7)
// Pure function: no prices, no clock. The client passes the session and day counts in.

public abstract record Uniqueness { private Uniqueness() { }   // brute force over the few hidden cells
    public sealed record Unique(HiddenAssignment Answer) : Uniqueness;
    public sealed record Multiple(HiddenAssignment First, HiddenAssignment Second) : Uniqueness;
    public sealed record Contradiction : Uniqueness;
    public sealed record BudgetExceeded(NodeCount NodesUsed) : Uniqueness; }
public interface IClueModel<TState>   // implemented by SortClues (tags + shelf ledger) and LotClues (tags + ledger)
{
    ReadOnlyArray<HiddenCell> HiddenCells(in TState state);                        // ascending order
    void AppendCandidates(in TState state, HiddenCell cell, List<ColourId> into);  // ascending ColourId
    bool IsConsistent(in TState state, PartialAssignment assignment);              // prunes early
}

public enum BotProfileId { Novice, Average, Expert, Greedy, Planner1, Planner3, Strong }
public interface IBotProfile { BotProfileId Id { get; } BotSearch Search { get; } PerMille Mistakes { get; } }
public abstract record BotSearch { private BotSearch() { }      // draws from RandomPurpose.Bot
    public sealed record Limited(MoveCount LookaheadPlies, DeductionRung Ceiling) : BotSearch;  // sees what players see
    public sealed record FullSolver : BotSearch; }              // Expert only: full information, within its NodeBudget
public enum BotEndReason { Won, NoLegalMove, SlotsFull, Cycle, StepCap }
// Every bot run terminates: on Failed, on a repeated ExactKey within the run (Cycle), or after Par × 3 moves
// (StepCap; the factor is an estimate). Above its Ceiling a Limited bot plans on a random ledger-consistent assignment.

public interface IReplayValidator { ReplayVerdict Validate(ActionRecord record); }
// Valid(Outcome, MoveCount moves, bool spareUsed) | Invalid(ActionIndex at, InvalidReason reason)
// | UnknownLevel | UnsupportedVersion. (A Jam Failed(NoLegalMove) is Valid but logged as an engine fault.)
public enum InvalidReason { IllegalMove, SpareOutsideFail, SecondSpare, RewindOutsideFail, RewindOutOfRange,
                            BoosterInCompetitiveMode, InsightLimit, ActionAfterEnd }  // mode checked first; see §4
```

**Notes on the abstractions**
- **No-guess certificate, two-solver shortcut.** A level ships only if all three hold: `Uniqueness` returns `Unique`; the technique grader returns `Graded` rather than `NeedsGuess`, so the clues pin every hidden cell before the first move; and the planner, given full information, returns `Solved` (Jam: with the level's own N bays). This avoids building a partial-information certifier.
- **Cap the board to keep the budgets small:** before the spare, start at ≤ 13 vials of capacity 4, and at ≤ 8×8 lots with ≤ 24 vehicles and ≤ 7 holding slots. Keep the hidden-cell count small. Measure solver, grader and uniqueness times with `bench` as each core is built (Jam: weeks 1-2; Sort: weeks 3 and 5), and set the default `NodeBudget`s from them.
- **One spare per attempt, in both games.** `FailOffer.Eligible` enforces it through `SpareUsedThisAttempt`, and the validator through `Invalid(SecondSpare)`. The start caps (13 vials, 7 bays) mean the spare always fits. `TryWithOneSpare` returns `None` at the 14-vial / 8-slot packing limit, and a guard test checks it.
- **Grade fields, defined once (game-neutral):**

  | Field | Meaning |
  |---|---|
  | `Par` | Length of the shortest winning path, in moves (pours or exits). In the jam it equals the vehicle count, so it is constant |
  | `DeadEnd` | Per-mille of 1,000 seeded random legal playouts (`RandomPurpose.Playout`) that end in `Failed`, `Cycle` or `StepCap` |
  | `SpareNeed` | Spares needed beyond the level's own capacity to win. 0 for every shipped level |
  | `MinFreeCapacity` | Fewest free capacity units on the best line. Sort: empty vials on the par line. Jam: free bays at the peak, i.e. the slack N − `PeakPar`. Lower is tighter |
  | `PeakPar` | Optional, in capacity units. Jam: `Some(lowest peak bay use over winning orders)`; Sort: `None`. `Par` always means path length |
  | `DeductionRung` | D1 = one constraint plus the ledger/count forces a cell. D2 = two constraints combined force a cell. D3 = a chain or count across several cells. A needed what-if case split = `NeedsGuess`, rejected in v1. Each brief maps its own clue techniques onto these rungs |

  Jam-only grader features (`GreedyWinPerMille`, branching, buried third, late-fail share) stay outside `PlanGrade`, in the CSV.
- **Fail offer (identical in both games):** every Campaign fail shows the offer screen until 4 have been shown in the session (each one counts), then a free retry only; a concede never shows it (*proposal*). Copy: "This offer only appears when one more vial (bay) can still win" and "A rewind of up to 5 moves can still win", never which length. Rewind is sold only there: no paid rewind on either HUD, no free undo. *Proposals* to confirm: the ad reward (+1 spare on `Yes`, otherwise a free rewind), selling rewind only when it can still win, rewind on the fail screen only, and the session definition (§7).
- **Fail cause for analytics** (`FailCause`, a *proposal*): the losing move is the move that left the last winnable position, found by one budgeted solve per prefix of the current line, scanning back from the fail.
  - **Deduction:** the losing move poured from or into a vial that had hidden layers at that time (Sort), or exited a still-covered vehicle (Jam).
  - **Capacity:** any other losing move.
  - **Unknown:** a prefix solve hit its budget.

  This is computed for logs only, never shown or sold, because an exact "rewind to the last winnable position" sells the answer.
- **Rewind and `SeenSet`:** in both games `Rewind(n)` restores the exact earlier state, where n counts `Move` actions; it never removes a spare already taken. The shared `SeenSet` (hidden cells seen revealed, or shown by `UseInsight`, which changes nothing else) lives in the session/record layer, never in the rules state, and the rules never read it. Replaying the record rebuilds it. It only grows. The client shows it as automatic pencil marks (Sort) or lifted covers (Jam).
- **Records:** `ActionRecord` contains:
  - format `pc-record`, `formatVersion`, `game` (`Sort`/`Jam`), `mode` (the `PlayMode` name), level `{packId, levelId, fingerprint}` and `rulesVersion`;
  - the actions `Move`, `Rewind(n)`, `TakeSpare`, `UseInsight(HiddenCell)` and `Concede`, each with `sincePreviousMs` stamped by the client. Illegal moves cost nothing and are never recorded.

  `Undo` is reserved: neither game emits it, and it can be added later by addition. The validator returns `Invalid(at, BoosterInCompetitiveMode)` for `TakeSpare`, `Rewind` or `UseInsight` outside Campaign. The reader and writer follow Classic's `RecordJson`. The format is a public contract: it changes only by addition, with a version bump.
- **Events:** golden replays compare the `PuzzleEvent` sequence, the outcome and the final `ExactKey`.
- **Server cost:** the validator replays the moves, which costs time linear in the move count. It never solves the board again.

### 3. Determinism rules

- [ ] Integer maths only in Core, Sort, Jam, Bot, Validation and Meta. No `float`/`double`/`decimal` (IL guard). Ratios are `PerMille` integers.
- [ ] No `System.Random`, `DateTime`, `Stopwatch`, `Environment.TickCount` or `Guid.NewGuid` in the core. Time enters only as recorded `sincePreviousMs`. Session and day counts enter only through `OfferContext` and `RewardedAdCapState`.
- [ ] Never iterate `Dictionary`/`HashSet` where the order affects output. Use arrays indexed by id, or sort by key first.
- [ ] Stable sorting only: an explicit `(key, id)` tiebreak or insertion sort on small arrays. Never rely on `Array.Sort` stability.
- [ ] Hashes use FNV, never `string.GetHashCode` or `System.HashCode`. Hash code goes in `unchecked { }` blocks, each with a comment.
- [ ] Every string the engine emits, stream keys included, is built with explicit `ToString(CultureInfo.InvariantCulture)`, never `$""` interpolation. The `HostileCulture` tests run under tr-TR and sv-SE.
- [ ] Budgets count nodes, never wall-clock time, so the same seed gives the same pack on any machine. The CLI may add a wall-clock safety cap that **fails the run loudly** instead of emitting a different pack.
- [ ] A level is always judged under the `rulesVersion` / `generatorVersion` its pack names. A new version never re-judges old levels.

### 4. Testing

| Test | Checks |
|---|---|
| Golden replays | Each committed `pc-record` reproduces the same `PuzzleEvent` sequence, outcome and final `ExactKey`. They also run in Unity's `EngineSmokeTest` |
| Golden generator packs | Seed → pack fingerprint (SHA-256 of canonical text) per `generatorVersion`, including the try schedule |
| Apply/rewind property | For random legal move sequences, `Rewind(n)` returns the exact earlier `ExactKey`, and `SeenSet` only grows. `TryApply` never mutates its input |
| Key split | Sort: permuting identical vials changes `ExactKey` but not `CanonicalKey`. Jam: the same exited set with a different slot order shares the `CanonicalKey` and differs in `ExactKey` |
| Spare limit | `TryWithOneSpare` returns `None` at the packing limit (14 vials, 8 slots). A second `TakeSpare` in one attempt gives `Invalid(SecondSpare)` |
| Solver ⇔ validator | Every `Solved.Path` validates as `Valid(Won)`. Every illegal move injected into it gives `Invalid` at that index |
| Near-miss soundness | `NearMiss.Yes` ⇒ the validator accepts `TakeSpare` plus the solver path as a win. After a spare it is `SpareUnavailable`. No offer is ever built from `Unknown` |
| Offer eligibility | Table tests: a counted screen on every Campaign fail until 4 per session, then `RetryOnly`; no paid option in any other mode; never a second screen per fail; no spare without `Yes` or after a spare; no rewind (gold or ad) unless `Winnable`; no ad that grants nothing or breaks a cap |
| Validator modes | Outside Campaign, `TakeSpare`, `Rewind` or `UseInsight` → `BoosterInCompetitiveMode`. In Campaign: `TakeSpare` / `Rewind` while not `Failed` → `SpareOutsideFail` / `RewindOutsideFail`; n outside 1-5 or beyond the line → `RewindOutOfRange`; a second `UseInsight` → `InsightLimit`; any action after `Won` or `Concede` → `ActionAfterEnd` |
| Uniqueness | Each shipped clue level is `Unique` and `Graded`. Mutating one clue gives `Multiple` or `Contradiction` (this catches weak checkers) |
| Bot bands | Each profile's win and near-miss rates, per `BotEndReason`, on a fixed pack stay within the bands recorded at calibration. Every run terminates |
| Money | `RealMoney.For(150 gold)` with $1.99 = 600 as the smallest pack gives 50 USD cents. JPY (0 minor units) rounds up to a whole yen. No gold pack gives `GoldNotSold` |
| Interstitials | Table tests of `Interstitials.After`: `Skip` after every fail, in `DailyProof`, during onboarding, with `AdsRemoved` and at each K8 cap; `Show` only after a win outside `DailyProof`, within the caps |
| Guards | IL/determinism guards, layering guards, a consumer compile test with Block.Core and PuzzleCore together |

### 5. Shared analytics schema

Both games log the same events from the greybox onward, with `game` = `sort` or `jam`. The client stamps the time. The engine supplies the grades, near-miss and fail cause.

| Event | Fields |
|---|---|
| `level_start` | game, mode, level_id, seed, attempt_index (1-based, per level), set (`clue` = sealed/covered, or `plain`), kind (`think`/`plan`), plan_grade, deduction_grade (absent on plain) |
| `level_end` | result (win/fail/abandoned), ended_by (`won`/`no_legal_move`/`slots_full`/`retry`/`concede`/`quit`; quit = left the level or the app), moves, time_ms, par_stars, spare_used, gold_earned |
| `fail_detail` | near_miss (`yes`/`no`/`spare_unavailable`/`unknown`, by the single definition), moves_from_win, rewind_reach (`winnable`/`not_winnable`/`unknown`), fail_kind (`no_legal_move`/`slots_full`), fail_cause (`capacity`/`deduction`/`unknown`), cleared_permille (Sort: share of layers in completed vials; Jam: share of vehicles exited) |
| `stall` | idle > 20 s with legal moves left |
| `offer_shown` / `offer_result` | offer_type (`spare_vial`/`spare_slot`, `rewind`, `ad_spare`/`ad_rewind`), rewind_moves (1-5, the length chosen; absent otherwise), price, real_money_label (currency + minor units, or `gold_not_sold`), gold_balance, offers_shown_this_session, accepted |
| `self_blame` | answer to the fail survey: `my_mistake` / `unfair` / `didnt_know`, plus survey_rate |
| `pencil_marks`, `insight_used` | count per level. Insight is earned only, ≤ 1 per level |
| `proof_result`, `share_card_tap` | solved, attempts, counted (true for the first N attempts, §8), time_ms |
| `gold_change` | source (`win`/`daily`/`spare`/`rewind`/`purchase`/`test_grant`), amount (signed), balance |
| `iap_purchase`, `ad_rewarded`, `session_start`, `session_end` | The ad event carries the cap state and the grant (`spare`/`rewind`). `session_end` carries gold earned and spent |
| `interstitial_shown`, `remove_ads_purchased` | The interstitial event carries mode, level_id and the K8 cap state. `remove_ads_purchased` carries the price (currency + minor units) |

**Metric definitions (shared)**
- **Stall rate** = the share of clue-level attempts with at least one `stall` event.
- **Self-blame survey:** shown after every fail in the greybox, so there are enough answers to judge a 70% band, and after 1 in 10 fails at soft launch. `survey_rate` is logged with each answer.
- **Player near-miss rate** is reported per `ended_by`. **Bot near-miss rate** is reported per `BotEndReason` (offline CSV column `bot_end_reason`).
- **Free-retry win rate** = the win rate of attempt k + 1 after a fail on attempt k, with no paid aid.
- **Gold per day** = the sum of positive `gold_change` amounts per tester per UTC day.

### 6. Game-specific vs shared

| Concern | Shared (PuzzleCore / Shared.Client / PuzzleMeta) | Sealed Vials (Sort) | Covered Lot (Jam) |
|---|---|---|---|
| State packing | `IPuzzleState`, `ExactKey`, `CanonicalKey` | vial words + hidden mask | exited mask + slot order; geometry in the level |
| Move | `IPuzzleRules` contract, typed rejection | `Pour(from, to)` | `Exit(piece)`: leaves only if the path is clear |
| Win/fail | `Outcome`, `FailKind` | all vials single-colour / no legal pour | lot cleared / all bays full after clears |
| Spare (rescue) | `TryWithOneSpare`, near-miss check, one per attempt | +1 empty vial (≤ 14) | +1 holding slot (≤ 8) |
| Rewind / reveals | exact `Rewind(n)`, session `SeenSet` | seen layers as automatic pencil marks | seen covers drawn as lifted |
| Clues | `IClueModel`, `Uniqueness`, `DeductionRung` D1-D3 | tags + shelf ledger | tags on covered pieces + ledger (original rule set) |
| Generator | Pcg32, purposes, try schedule, pack format | reverse-pour scramble; `pc-sort` text | reverse-exit scramble, `LotLoader`; `pc-lot` text |
| Solver/grader/bots | `SolveResult`, BFS/IDA*, `LevelGrade`, `IBotProfile`, bot termination | heuristic: Σ (vials holding a colour − 1) | feasibility DFS; "blocked exits + slot pressure" bound only if `bench` needs it |
| Fail offer / records / validator / analytics / money | all shared | labels only | labels only |
| Presentation | offer screen, analytics adapter, UI kit | pour VFX, ledger UI | vehicle motion, slot row |

### 7. Money types, caps, session and day

Money types live in `PuzzleMeta` and the session and day counters in `Shared.Client`, never in `PuzzleCore`. The engine sees only the counts.

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
- **Daily gold note (estimate):** the report's ~150-200 gold a day assumes almost no stars. With at least 1 star per win, the floor is about 230 a day. With 2 stars per level it is about 290 a day, about 2 free rescues a day at the report's 150-gold spare. Re-derive this in the economy simulator before soft launch. The greybox measures gold earned per UTC day per tester (`gold_change`).
- **Caps:** at most 4 fail screens per session; rewarded ads at most 1 per fail, 2 per session and 5 per UTC day.
- **Session (*proposal*):** runs from app foreground until the app has been in the background for 30 minutes. **Day:** the UTC day, with the same 00:00 UTC rollover as the daily (E1). `Shared.Client` counts both and passes them into `OfferContext` / `RewardedAdCapState`. The engine never reads a clock.

**Confirmed by you on 2026-11-07 (both games):**
- **Gold is sold for real money.** The gold packs and the starter pack are real IAP from soft launch on. Every gold price shows its real-money equivalent on the same screen (`RealMoney.For`). `GoldNotSold` is left only for a storefront that has no gold pack.
- **Pastel Rows' ad rules carry over:** interstitials under K8's caps, rewarded ads under K8, and a Remove Ads purchase.

**Interstitials and Remove Ads (both games):**
- **Caps:** K8's numbers, unchanged. They are recorded in the Block engine repo (`docs/plan-ve-kararlar.md`), not in these briefs, so copy them into this section before coding the ad module. If K8's rewarded-ad caps differ from the caps above, K8 wins. A completed level counts as one completed game, as a rule room does in `GAME_DESIGN.md`.
- **Placement (*proposal*):** only after a win, once the celebration and the solution replay have played, and before the next level loads. Never on the fail path (between a fail and its offer, or right after a rescue), never in `DailyProof` or before its result, and never during onboarding (about the first 20 plain levels). The fail screen keeps the rewarded ad only, so the one Black Hat moment stays a single, opted-in offer.
- **Remove Ads (*proposal*, unless K8 says otherwise):** a non-consumable, restorable purchase that removes interstitials only. The rewarded ad stays an opt-in choice on the fail screen, with the same caps. It is sold in the shop beside the gold packs, never on the fail screen and never chained after another purchase.
- **Greybox:** no interstitials and no ads SDK. `Interstitials.After` and its tests arrive with the ad module, before soft launch.
- **Types:** `Shared.Client` decides the ad slot; the engine never does.

```csharp
public enum AdEntitlement : byte { Standard, AdsRemoved }   // AdsRemoved is restored from the store receipt
public readonly struct InterstitialCapState { /* K8's counters for this session and UTC day, copied from K8 */ }
public sealed record LevelEndContext(PlayMode Mode, Outcome Outcome, LevelIndex Level,
                                     AdEntitlement Entitlement, InterstitialCapState Caps);
public enum InterstitialSkipReason : byte { AdsRemoved, NotAWin, DailyMode, Onboarding, CapReached }
public abstract record InterstitialSlot
{
    private InterstitialSlot() { }
    public sealed record Show : InterstitialSlot;
    public sealed record Skip(InterstitialSkipReason Reason) : InterstitialSlot;
}
public static class Interstitials { public static InterstitialSlot After(LevelEndContext context); }  // pure
```

**Decided by you on 2026-11-07, second round (both games):**
- **Lives: yes**, with a refill timer.
- **One currency for now:** gold only (K12 holds, for now).
- **No boosters in competitive modes:** K7 holds.
- **Streaks: yes.** E9 does not carry over to these games.
- **Paid random rewards: yes** (for example chests or packs).

**Assumed, not confirmed:** no subscriptions (a 28-day non-renewing pass is allowed); 13+; minimum iOS 15; real-money equivalents on gold prices (K24). **Still open:** the daily's counted attempts N (§8).

**To design in the coding session, before any of it is built.** None of it is in the greybox. The Money sections and the mode tables still describe the game without these systems until this design is done.
- **Lives:** max lives, minutes per life, and what a fail, a quit and a restart cost (a deduction game needs free restarts for thinking). Also the out-of-lives screen, with the timer as the free route; gold and ad refills; and which modes use lives (Campaign only is the obvious default).
- **Streak:** the type (a Campaign win streak or a daily-play streak), its rewards, whether a paid rescue keeps it, streak protection, and what the fail screen says about it. The teen checklist bans loss-framed copy.
- **Paid random rewards:**
  - Contents: tokens for the existing gated actions (a spare token still needs `NearMiss.Yes`, a rewind token still needs `Winnable`) and cosmetics. Never Insight, never the answer, never gold for gold.
  - The price and a published odds table.
  - Where they are sold: the shop only, never on the fail screen, and never chained after a continue.
- **Engine placement:** lives, streaks and chests are meta (`PuzzleMeta`, `Shared.Client`, server), never `PuzzleCore`. The engine never reads a clock. Chest draws happen on the server, with a versioned odds table the client cannot predict.
- **Then update both briefs:** analytics events, tests, the Rules-by-mode columns, the Octalysis ratings (CD6, CD7 and CD8 go up) and the red lines.

**Check before shipping paid random rewards in a 13+ game.** This comes from general knowledge, has not been re-verified here, and is not legal advice.
- Both stores require the odds of each item to be shown before purchase (App Store guideline 3.1.1; Google Play's payments policy).
- The age-rating questionnaires add "In-game purchases (includes random items)".
- Some markets restrict paid random rewards. Belgium treats paid loot boxes as gambling. Brazil's ECA Digital law bans them for minors. Australia rates games with paid loot boxes M or higher. South Korea requires published probabilities.
- Plan a per-storefront switch, and verify each market before launch.

### 8. Mode rules and the daily

- Only `Campaign` allows `TakeSpare`, `Rewind` and `UseInsight`. The validator enforces this for every other `PlayMode` (K7).
- **Daily (both games; the Proof and the Daily Lot are both `DailyProof`):**
  - It rolls over at **00:00 UTC** (E1).
  - **Seed (one format for both games, *proposal*):** the one-level pack `daily-{game}-{yyyy-MM-dd}` (UTC date, built like a stream key), whose `PackSeed` comes from that pack id through `SeedDerivation`.
  - Only the **first N attempts** count toward "X% solved it unaided". Later retries are practice: they are logged with `proof_result.counted = false` and not counted.
  - **N is an open question for the user:** K9's block daily uses 3, and Rule of Rows chose 1 because a rule board's answer is revealed after the first attempt.

## Shared plan (one developer, both prototypes)

This plan is identical in both briefs. There is one developer and one queue, and the two games are never built in parallel. **Covered Lot (Jam) goes first, by your decision of 2026-11-07**; Sealed Vials (Sort) follows and reuses everything shared. The report still ranks Sealed Vials first, and the cross-game decision below still decides what ships. All dates and day sizes are estimates.

**Tomorrow's first 3 tasks (Sunday 2026-11-08, shared)**
- [ ] Create `puzzle-engine` with `Directory.Build.props/.targets` (TreatWarningsAsErrors, overflow checks, layering rules), the IL/determinism guards, the `HostileCulture` initializer and the linked `src/Shared/IsExternalInit.cs`, ported from the Block engine.
- [ ] Add every id and value type from shared engine §2 (`ColourId`, `VialId`, `LayerIndex`, `PieceId`, `SlotIndex`, `HiddenCell`, `LevelIndex`, `ActionIndex`, `MoveCount`, `StepCount`, `PerMille`, `CapacityCount`, `OfferCount`, `AdCount`, `NodeBudget`, `NodeCount`), plus `Option<T>`, `ReadOnlyArray<T>`, `Outcome`, `FailKind` and `RandomPurpose`, with equality tests.
- [ ] Port `Pcg32`, `Seed` and `SeedDerivation` from `Block.Core.Randomness`, and pin golden values for two stream keys.

Sunday is done when the guards fail on a planted `float` and a planted `System.Random`, and the tests pass under tr-TR and sv-SE.

| When | Game | Work | Done when |
|---|---|---|---|
| Sun 11-08 | Shared | The 3 tasks above | Guards and golden values pass |
| Mon 11-09 | Jam | Half a day first: jam Stage 0 (the competitor and originality check and the "differs from" table, see Covered Lot), then freeze the jam rules. Then `LotState` (`ExactKey`/`CanonicalKey`), `Exit`, `JamRules` (`TryApply`, `Evaluate`, `TryWithOneSpare`), `LotLoader`. A full day; the core may spill into Tuesday | Stage 0 log and table written; the Jam golden rule tests, the apply/rewind property and the key-split test pass |
| Tue 11-10 | Shared | `pc-record` reader/writer (after `RecordJson`), `PuzzleEvent`, `IReplayValidator` with the mode checks, CLI `replay`, first golden replays. Stage 0 write-up | A hand-written record replays to `Valid(Won)`; golden replays pass |
| Wed 11-11 | Shared | Solver (BFS + IDA*, bounded transposition table) with the jam feasibility DFS and `PeakPar`, CLI `bench` | Solver ⇔ validator test passes; p50/p99 solve times recorded at 8×8 |
| Thu 11-12 | Jam | `LotGenerator` (plain, reverse-exit), try schedule, `pc-lot` pack writer, golden fingerprint | `gen` makes 1,000 plain levels, all `Solved` under `validate`, with zero loader rejects; generation rate recorded |
| Fri 11-13 | Shared | `NearMiss`, the spare limit, `RewindReach`, `FailOffer.Eligible`, `PlanGrade` fields, `DeadEnd` playouts, `FailCause` | Offer-eligibility, near-miss-soundness and spare-limit tests pass |
| Sat 11-14 | Shared | Buffer. Recruit 15-20+ testers (one shared pool for both games). Set the default `NodeBudget`s from the week's `bench` numbers | Tester list; budgets set |
| Week 2 (11-15 to 11-21) | Jam | `LotClues`, uniqueness at 5 covers, D1-D3 grader, mutation test, covered generator (200 covered levels); Greedy and Planner bots with termination and the `sim` CSV against slack 0-3; the jam engineering gate; 40 covered + 40 plain matched levels | Covered levels `Unique` and `Graded`; engineering gate passes; first calibration table |
| Week 3 (11-22 to 11-28) | Sort | Sun 11-22, half a day: Sort Stage 0 (hidden-layer levels in the top sort titles). `SortState` (`ExactKey`/`CanonicalKey`), `Pour`, `SortRules`, `SortGenerator` (plain, reverse-pour), `pc-sort` pack writer, reusing records, validator, solver, near-miss and offers | Sort golden rule, solver ⇔ validator and golden pack tests pass; 1,000 plain levels `Solved` |
| Week 4 (11-29 to 12-05) | Jam (Unity) | Only once both plain cores pass their tests: Covered Lot `Play` scene, shared offer screen (test gold, simulated ad), analytics adapter, `EngineSmokeTest`, greybox pack | **Jam greybox about Dec 4** |
| Week 5 (12-06 to 12-12) | Sort | `Cover` step, `SortClues`, `Uniqueness`, D1-D3 grader, mutation test; sort bots and calibration; 40 sealed + 40 plain matched levels; Sealed Vials `Play` scene on `Shared.Client` | **Sort greybox about Dec 11-14** |
| Week 6 (12-13 to 12-19) | Both | Joint playtest on the shared analytics schema, then the cross-game decision | **Decision about Dec 18** |
| Early January 2027 | Lead | Stage 2 paid creative test for the chosen game(s) | |

**Schedule risk:** the report's verifier sized the clue layer (cover, uniqueness, grader) at weeks, not days. If week 2 ends without `Unique` + `Graded` covered levels, the work continues into week 3 and every later date slips by the overrun. Week 5 is the tightest week, because it holds the whole Sort clue layer and its scene, so the Sort greybox and the decision are the dates most likely to move.

### Cross-game decision

Each game is first judged against its own greybox bands (its own go/no-go). Then this combined rule applies (*proposal*).

**Definitions (both games, same denominators):**
- **Core passes:** ≥ 50% of bot fails on clue levels are near-misses (the single shared definition), ≥ 70% of players call fails their own mistake, stalls on ≤ 25% of clue-level attempts, and the engineering gate passes.
- **Clears take-up:** clue-level take-up of the fail offer is ≥ 0.6× the plain-level take-up in the same game.

| Sealed Vials | Covered Lot | Action |
|---|---|---|
| Core passes and clears take-up | Any result | Ship Sealed Vials first. If Covered Lot also passes, keep it for later, as a mode or a second app |
| Core passes, take-up fails | Any result | Retune Sealed Vials first, as the report says: cut the deduction dose (1 sealed level in 8), then deduction in the daily Proof only. Do not switch genre for an economy problem |
| Core fails (near-misses land mid-board, or "don't know" stalls that cutting the dose does not cure) | Core passes and clears take-up | Switch the lead to Covered Lot (the report's switch rule) |
| Core fails | Core passes, take-up fails | Test the hybrid (below) before choosing |
| Core fails | Core fails | Ship neither. If plain-level take-up is also low in both games, the problem is the economy rules: revisit prices, slack and the ad caps before trying another genre |

Small samples only point in a direction.

### Hybrid options (nothing is built before the decision)

1. **One app, two level types (no engine cost).** One map that interleaves sort and jam levels as one sawtooth, with one ledger and tag vocabulary, one gold economy and one daily. A client assembly references both `PuzzleCore.Sort` and `PuzzleCore.Jam`; the rules modules stay separate. Test it with the joint playtest pack ordered as one sawtooth.
2. **A true jam-to-sort rule set (new module).** Vehicles leave the lot under the jam exit rule, and each one adds a layer of its colour to a capacity-4 vial on a shelf instead of a holding slot; a full single-colour vial clears. Fail = no legal exit (shelf full). Covered vehicles and the ledger carry the deduction layer. It would get its own rules module and pack type on `PuzzleCore`, must not make `Sort` and `Jam` depend on each other, and reuses records, the validator, near-miss, offers and analytics unchanged. Build it only if option 1 shows players want both loops in one game.

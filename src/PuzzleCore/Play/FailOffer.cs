using System.Collections.Generic;
using PuzzleCore.Records;

namespace PuzzleCore.Play
{
    /// <summary>
    /// Whether one more spare can still win: the single near-miss definition for both games,
    /// computed on Failed only by solving TryWithOneSpare(final) with full information. Closed set.
    /// </summary>
    public abstract record NearMiss
    {
        private NearMiss()
        {
        }

        /// <summary>The solver wins from the failed state plus one spare, in this many more moves (Jam: the vehicles left).</summary>
        public sealed record Yes(MoveCount MovesFromWin) : NearMiss;

        /// <summary>Even one more spare cannot win.</summary>
        public sealed record No : NearMiss;

        /// <summary>The attempt's one spare is already used, or TryWithOneSpare is None at the packing limit.</summary>
        public sealed record SpareUnavailable : NearMiss;

        /// <summary>The solve hit its node budget. Log it; never offer on it.</summary>
        public sealed record Unknown(NodeCount NodesUsed) : NearMiss;
    }

    /// <summary>
    /// Whether some rewind the attempt allows (1 to its RewindLimit moves) reaches a winnable
    /// position; at limit 0 it is NotWinnable. Unknown: a solve hit its node budget; log it, and offer
    /// no rewind, for gold or for an ad. The spare depends on NearMiss alone.
    /// </summary>
    public enum RewindReach
    {
        Winnable,
        NotWinnable,
        Unknown,
    }

    /// <summary>
    /// Whether the player has completed their first game (in Covered Lot, a completed level: brief §7).
    /// K24 in the Block engine's decisions: the age question follows the first completed game, so no
    /// rewarded ad is offered before it. The gold options stay open.
    /// </summary>
    public enum FirstGame
    {
        InProgress,
        Completed,
    }

    /// <summary>
    /// Rewarded ads already watched, per scope, as Shared.Client counts them (the engine never reads
    /// a clock), and the K24 first-game gate. Build it with named arguments: the three counts share
    /// one unit. Caps: K8 allows one ad continue per attempt, which replaces the brief's "1 per fail"
    /// because K8 wins on any difference; the brief adds 2 per session and 5 per UTC day.
    /// </summary>
    public sealed record RewardedAdCapState(FirstGame FirstGame, AdCount ThisAttempt, AdCount ThisSession, AdCount ThisUtcDay)
    {
        public const int PerAttempt = 1;
        public const int PerSession = 2;
        public const int PerUtcDay = 5;

        /// <summary>Whether one more rewarded ad is allowed: the player has completed their first game (K24) and every cap has room.</summary>
        public bool AllowsOneMore =>
            FirstGame == FirstGame.Completed
            && ThisAttempt.Value < PerAttempt
            && ThisSession.Value < PerSession
            && ThisUtcDay.Value < PerUtcDay;
    }

    /// <summary>Whether this fail has already shown its offer screen; a fail never shows a second one. Closed set.</summary>
    public enum FailScreen
    {
        NotYetShown,
        AlreadyShown,
    }

    /// <summary>Whether the attempt's one spare is used; the spare is never sold twice in one attempt. Closed set.</summary>
    public enum SpareUse
    {
        Unused,
        Used,
    }

    /// <summary>
    /// What the fail offer is decided from. The engine supplies NearMiss and RewindReach;
    /// Shared.Client supplies the session counts and the ad state.
    /// </summary>
    public sealed record OfferContext(
        PlayMode Mode,
        NearMiss NearMiss,
        RewindReach RewindReach,
        OfferCount OffersShownThisSession,
        FailScreen ThisFail,
        SpareUse SpareThisAttempt,
        RewardedAdCapState AdCap);

    /// <summary>A paid option on the fail screen, in the order the screen lists them. Closed set.</summary>
    public enum PaidOption
    {
        SpareForGold,
        RewindForGold,
        AdForSpare,
        AdForRewind,
    }

    /// <summary>What a fail shows. Closed set: the free retry alone, or the fail screen.</summary>
    public abstract record OfferSet
    {
        private OfferSet()
        {
        }

        /// <summary>No screen, only the free retry. Not counted toward the session cap.</summary>
        public sealed record RetryOnly : OfferSet;

        /// <summary>
        /// The fail screen: the free retry beside these paid options, in <see cref="PaidOption"/> order.
        /// Counted toward the session cap, even when <paramref name="Paid"/> is empty.
        /// </summary>
        public sealed record Screen(ReadOnlyArray<PaidOption> Paid) : OfferSet;
    }

    /// <summary>The one fail offer, identical in both games. A pure function: no prices and no clock.</summary>
    public static class FailOffer
    {
        /// <summary>Fail screens per session. From the fifth fail on, only the free retry is offered.</summary>
        public const int ScreensPerSession = 4;

        /// <summary>
        /// The offer for one fail (call it on Failed only). Only Campaign has paid options, so no other
        /// mode opens a screen. In Campaign a screen opens unless this fail already showed one or the
        /// session has shown <see cref="ScreensPerSession"/>. It sells the spare for gold on
        /// <see cref="NearMiss.Yes"/> while the spare is unused, and the rewind for gold when
        /// <see cref="RewindReach.Winnable"/>. One rewarded ad joins them within its caps; it grants
        /// the spare when the spare is on sale, otherwise the rewind, and it never appears alone.
        /// Each test compares with the one value that allows an option, so an enum value outside its
        /// set offers nothing.
        /// </summary>
        public static OfferSet Eligible(OfferContext context)
        {
            bool opensScreen = context.Mode == PlayMode.Campaign
                && context.ThisFail == FailScreen.NotYetShown
                && context.OffersShownThisSession.Value < ScreensPerSession;
            if (!opensScreen)
            {
                return new OfferSet.RetryOnly();
            }

            bool sellsSpare = context.NearMiss is NearMiss.Yes && context.SpareThisAttempt == SpareUse.Unused;
            bool sellsRewind = context.RewindReach == RewindReach.Winnable;
            var paid = new List<PaidOption>();
            if (sellsSpare)
            {
                paid.Add(PaidOption.SpareForGold);
            }

            if (sellsRewind)
            {
                paid.Add(PaidOption.RewindForGold);
            }

            bool adGrantsSomething = sellsSpare || sellsRewind;
            if (adGrantsSomething && context.AdCap.AllowsOneMore)
            {
                paid.Add(sellsSpare ? PaidOption.AdForSpare : PaidOption.AdForRewind);
            }

            return new OfferSet.Screen(ReadOnlyArray.CopyOf(paid));
        }
    }
}

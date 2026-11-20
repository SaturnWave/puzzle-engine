using System;

namespace PuzzleCore.Bot
{
    /// <summary>
    /// A bot profile. Each game uses its own profiles: Covered Lot's are Greedy, Planner1, Planner3 and Expert (Q40,
    /// Q75); Sealed Vials appends its own. A profile's name is fixed forever, because it seeds the run's Bot stream
    /// ("Bot|{packId}:{slot}:{profile}:{run}") and names its rows in the sim CSV, so renaming one moves every band. Add
    /// new profiles at the end only; their names come from <see cref="BotNames"/>.
    /// </summary>
    public enum BotProfileId
    {
        Greedy,
        Planner1,
        Planner3,
        Expert,
    }

    /// <summary>
    /// How a bot run ended (shared-design.md §2): Failed with a game's fail kind, a repeated position (Cycle), or the
    /// move cap (StepCap). Covered Lot's runs end in Won or SlotsFull. Names are fixed forever (the sim CSV's
    /// bot_end_reason column); add new reasons at the end only.
    /// </summary>
    public enum BotEndReason
    {
        Won,
        NoLegalMove,
        SlotsFull,
        Cycle,
        StepCap,
    }

    /// <summary>The fixed names of the bot enums, for stream keys and the sim CSV. Never use the enum's ToString or ordinal.</summary>
    public static class BotNames
    {
        public static string Of(BotProfileId profile) => profile switch
        {
            BotProfileId.Greedy => "Greedy",
            BotProfileId.Planner1 => "Planner1",
            BotProfileId.Planner3 => "Planner3",
            BotProfileId.Expert => "Expert",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown bot profile."),
        };

        public static string Of(BotEndReason reason) => reason switch
        {
            BotEndReason.Won => "Won",
            BotEndReason.NoLegalMove => "NoLegalMove",
            BotEndReason.SlotsFull => "SlotsFull",
            BotEndReason.Cycle => "Cycle",
            BotEndReason.StepCap => "StepCap",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown bot end reason."),
        };
    }
}

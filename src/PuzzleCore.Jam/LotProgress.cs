namespace PuzzleCore.Jam
{
    /// <summary>How far a Covered Lot attempt got (Q81), for sim's reports and the late-fail share.</summary>
    public static class LotProgress
    {
        /// <summary>
        /// cleared_permille: the vehicles that have left, per mille of the level's vehicles, rounded down. It is 1000 only
        /// once the lot is empty, so a fail always shows less.
        /// </summary>
        public static PerMille Cleared(LotLevel level, LotState state) => PerMille.Of(state.ExitedCount, level.VehicleCount);
    }
}

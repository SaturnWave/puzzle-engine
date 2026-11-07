using PuzzleCore.Randomness;
using PuzzleCore.Search;

namespace PuzzleCore.Jam
{
    /// <summary>
    /// One campaign level ready to ship: the level, its par (fewest bays that win) with a par order,
    /// and, on covered levels, the deduction grade. LevelKind follows the brief's sawtooth.
    /// </summary>
    public sealed record BuiltLevel(LotLevel Level, CapacityCount PeakPar, ReadOnlyArray<Exit> ParPath, Option<DeductionGrade.Graded> Deduction)
    {
        public bool IsCovered => Deduction.HasValue;

        public int Slack => Level.Bays.Value - PeakPar.Value;
    }

    /// <summary>Why a campaign slot has no level. The pack builder must handle it (skip or regenerate), never hide it.</summary>
    public enum BuildFailure
    {
        PlainRejected,
        CoverRejected,
    }

    public abstract record BuildResult
    {
        private BuildResult()
        {
        }

        public sealed record Built(BuiltLevel Level) : BuildResult;

        public sealed record Rejected(BuildFailure Reason) : BuildResult;
    }

    /// <summary>
    /// Builds campaign slot <c>index</c> of a pack from the content plan: the plain generator first,
    /// then the coverer when the plan covers that slot. The single path used by the CLI, the
    /// playtest client and (later) the offline pack writer, so they always agree.
    /// </summary>
    public static class LotPackBuilder
    {
        public static BuildResult Build(PackId pack, LevelIndex index)
        {
            if (!(LotGenerator.Generate(pack, index, LotContentPlan.For(index)) is GenerateResult.Generated plain))
            {
                return new BuildResult.Rejected(BuildFailure.PlainRejected);
            }

            GeneratedLot lot = plain.Lot;
            if (!LotContentPlan.CoversFor(index).TryGet(out CoverPlan? coverPlan))
            {
                return new BuildResult.Built(new BuiltLevel(lot.Level, lot.PeakPar, lot.ParPath, Option.None<DeductionGrade.Graded>()));
            }

            if (!(LotCoverer.Cover(lot.Level, pack, index, coverPlan) is CoverResult.Covered covered))
            {
                return new BuildResult.Rejected(BuildFailure.CoverRejected);
            }

            return new BuildResult.Built(new BuiltLevel(covered.Lot.Level, lot.PeakPar, lot.ParPath, Option.Some(covered.Lot.Grade)));
        }
    }
}

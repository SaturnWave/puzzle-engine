using System.Globalization;
using System.Linq;
using System.Text;
using PuzzleCore.Jam;
using PuzzleCore.Records;
using PuzzleCore.Search;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// The grading report, {packId}.grades.csv (Q68): a header line, then one line per graded level in slot order. The
    /// fields are level ids, contract names and invariant integers, so none ever needs quoting. Rung and steps are
    /// empty on Plan levels, and peak_par is empty for a grade without one. Every line ends with '\n'. The greedy win
    /// rate and the late-fail share come with the bots, in P5's sim CSV.
    /// </summary>
    public static class LotGradingCsv
    {
        public const string Header =
            "level_id,slot,kind,width,height,vehicles,colours,bays,peak_par,min_free_capacity,spare_need,par,"
            + "dead_end_permille,branching_thousandths,buried_third_thousandths,covers,tags,rung,steps";

        public static string Write(GradedLotPack pack)
        {
            var csv = new StringBuilder();
            csv.Append(Header).Append('\n');
            foreach (GradedLot lot in pack.Levels)
            {
                csv.Append(string.Join(",", Fields(lot))).Append('\n');
            }

            return csv.ToString();
        }

        private static string[] Fields(GradedLot lot)
        {
            LotLevel level = lot.Built.Level;
            PlanGrade plan = lot.Grade.Plan;
            string rung = string.Empty;
            string steps = string.Empty;
            if (lot.Grade.Deduction.TryGet(out DeductionGrade.Graded? deduction))
            {
                rung = ContractNames.Of(deduction.Rung);
                steps = Number(deduction.Steps.Value);
            }

            return new[]
            {
                lot.Id.Value,
                Number(lot.Slot.Value),
                ContractNames.Of(lot.Grade.Kind),
                Number(level.Size.Width),
                Number(level.Size.Height),
                Number(level.VehicleCount),
                Number(ColoursOf(level)),
                Number(level.Bays.Value),
                plan.PeakPar.TryGet(out CapacityCount peakPar) ? Number(peakPar.Value) : string.Empty,
                Number(plan.MinFreeCapacity.Value),
                Number(plan.SpareNeed.Value),
                Number(plan.Par.Value),
                Number(plan.DeadEnd.Value),
                Number(lot.Branching.Value),
                Number(lot.BuriedThird.Value),
                Number(level.Vehicles.Count(vehicle => vehicle.StartsCovered)),
                Number(level.Clues.Count),
                rung,
                steps,
            };
        }

        private static int ColoursOf(LotLevel level)
        {
            var isUsed = new bool[ColourId.MaxValue + 1];
            foreach (Vehicle vehicle in level.Vehicles)
            {
                isUsed[vehicle.Colour.Value] = true;
            }

            return isUsed.Count(used => used);
        }

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}

using System.IO;
using System.Linq;
using System.Text;
using PuzzleCore.Bot;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Records;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// The files sim writes for a pack (Q84): {packId}.sim.csv and {packId}.sim-fails.csv (<see cref="LotSimCsv"/>), and
    /// for each level that Planner3 failed, its first failed run as {levelId}.planner3-{run}.pc-record.json beside the
    /// level's text {levelId}.pc-lot, so "replay" reproduces it. Every text is UTF-8 without a byte-order mark.
    /// </summary>
    public static class LotSimFiles
    {
        private static readonly Encoding Utf8WithoutMark = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static void Write(SimPack pack, string directory)
        {
            Directory.CreateDirectory(directory);
            WriteText(directory, SimFileName(pack.Pack), LotSimCsv.Write(pack));
            WriteText(directory, FailsFileName(pack.Pack), LotSimCsv.WriteFails(pack));
            foreach (SimSlot.Simulated level in pack.Simulated)
            {
                SimProfileRuns planner3 = level.Profiles.Single(profile => profile.Profile.Id == BotProfileId.Planner3);
                if (planner3.Runs.FirstOrDefault(run => run.Failed) is SimRun failed)
                {
                    WriteText(directory, LotPackFiles.LevelFileName(level.Id), LotText.Write(level.Built.Level));
                    WriteText(directory, RecordFileName(level.Id, failed.Run), RecordJson.Write(RecordOf(pack.Pack, level, failed), ExitJson.Instance));
                }
            }
        }

        public static string SimFileName(PackId pack) => pack.Value + ".sim.csv";

        public static string FailsFileName(PackId pack) => pack.Value + ".sim-fails.csv";

        public static string RecordFileName(LevelId level, int run) =>
            level.Value + ".planner3-" + run.ToString("000", System.Globalization.CultureInfo.InvariantCulture) + ".pc-record.json";

        /// <summary>The run as a Campaign record of its moves, with no time between them.</summary>
        public static ActionRecord<Exit> RecordOf(PackId pack, SimSlot.Simulated level, SimRun run) => new(
            GameKind.Jam,
            PlayMode.Campaign,
            new LevelRef(pack, level.Id, LotGrading.FingerprintOf(level.Built.Level)),
            LotText.RulesVersion,
            ReadOnlyArray.CopyOf(run.Moves.Select(move => (RecordAction<Exit>)new RecordAction<Exit>.Move(move, new ElapsedMs(0)))));

        private static void WriteText(string directory, string fileName, string text) =>
            File.WriteAllText(Path.Combine(directory, fileName), text, Utf8WithoutMark);
    }
}

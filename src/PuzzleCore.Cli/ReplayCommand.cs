using System.Globalization;
using System.IO;
using PuzzleCore.Jam;
using PuzzleCore.Play;
using PuzzleCore.Records;

namespace PuzzleCore.Cli
{
    /// <summary>
    /// "replay &lt;level.pc-lot&gt; &lt;record.json&gt;": validates a Jam record against its level, as the
    /// server will. Exit code 0 = valid, 1 = invalid record, 2 = unreadable input.
    /// </summary>
    internal static class ReplayCommand
    {
        public static int Run(string levelPath, string recordPath, TextWriter output)
        {
            if (!(LotText.Read(File.ReadAllText(levelPath)) is LotTextResult.Read text))
            {
                output.Write("The level file is not valid pc-lot text.\n");
                return 2;
            }

            if (!(LotLoader.Load(text.Definition) is LoadResult.Loaded loaded))
            {
                output.Write("The level was rejected by the loader.\n");
                return 2;
            }

            switch (RecordJson.Read(File.ReadAllText(recordPath), ExitJson.Instance))
            {
                case RecordReadResult<Exit>.Read read:
                    return Validate(loaded.Level, read.Record, output);
                case RecordReadResult<Exit>.UnsupportedVersion:
                    output.Write("UnsupportedVersion\n");
                    return 1;
                case RecordReadResult<Exit>.Invalid invalid:
                    foreach (Json.FileProblem problem in invalid.Problems)
                    {
                        output.Write(problem.ToString() + "\n");
                    }

                    return 2;
                default:
                    return 2;
            }
        }

        private static int Validate(LotLevel level, ActionRecord<Exit> record, TextWriter output)
        {
            if (record.Level.Fingerprint.Hex != LotText.Fingerprint(level))
            {
                output.Write("UnknownLevel: the record's fingerprint does not match this level.\n");
                return 1;
            }

            var rules = new JamRules(level);
            ReplayReport report = Replay.Run(rules, rules.Start, record.Mode, record.Actions);
            output.Write(report.Verdict.ToString() + "\n");
            output.Write("events: " + report.Events.Count.ToString(CultureInfo.InvariantCulture) + ", final key: " + report.Final.ToString() + "\n");
            return report.Verdict is ReplayVerdict.Valid ? 0 : 1;
        }
    }
}

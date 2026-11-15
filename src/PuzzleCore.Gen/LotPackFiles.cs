using System.IO;
using System.Text;
using PuzzleCore.Jam;
using PuzzleCore.Packs;
using PuzzleCore.Randomness;
using PuzzleCore.Records;

namespace PuzzleCore.Gen
{
    /// <summary>
    /// The files gen writes for a pack: each graded level's canonical text as {levelId}.pc-lot (Q66), the presentation
    /// file {packId}.presentation.json and the grading report {packId}.grades.csv. Every text is written as built, in
    /// UTF-8 without a byte-order mark; the canonical texts end without a newline.
    /// </summary>
    public static class LotPackFiles
    {
        private static readonly Encoding Utf8WithoutMark = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static void Write(GradedLotPack pack, string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (GradedLot lot in pack.Levels)
            {
                WriteText(directory, LevelFileName(lot.Id), LotText.Write(lot.Built.Level));
            }

            WriteText(directory, PresentationFileName(pack.Pack), PresentationJson.Write(pack.Presentation));
            WriteText(directory, GradesFileName(pack.Pack), LotGradingCsv.Write(pack));
        }

        public static string LevelFileName(LevelId level) => level.Value + ".pc-lot";

        public static string PresentationFileName(PackId pack) => pack.Value + ".presentation.json";

        public static string GradesFileName(PackId pack) => pack.Value + ".grades.csv";

        private static void WriteText(string directory, string fileName, string text) =>
            File.WriteAllText(Path.Combine(directory, fileName), text, Utf8WithoutMark);
    }
}

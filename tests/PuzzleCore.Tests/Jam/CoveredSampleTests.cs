using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using PuzzleCore.Search;
using Xunit;

namespace PuzzleCore.Tests.Jam
{
    /// <summary>
    /// The covered-levels pin (Q30, TC10) over the sample the development plan's P3 named before its first
    /// run: every covered slot below 100 of the packs rung-sample-00 to rung-sample-24, which plans 100 D1,
    /// 100 D2 and 200 D3 slots. The CLI prints the same sample slot by slot: "rungs rung-sample 25 100".
    /// Building it takes about ten seconds, so the class builds it once.
    /// </summary>
    public sealed class CoveredSampleTests
    {
        /// <summary>
        /// Pinned from the generator before P3 switched the grader to the one ladder (self-pinned: no
        /// independent implementation). A changed hash means generated covered content changed: stop and
        /// ask the user before re-pinning.
        /// </summary>
        private const string SamplePin = "ab84a5317508e5f30de3d2689e3e872e0e9bec7a820552728e956a3a24754a9b";

        private static readonly Lazy<IReadOnlyList<string>> SampleLines = new(BuildSampleLines);

        [Fact]
        public void The_covered_sample_matches_its_pin()
        {
            Assert.Equal(SamplePin, Sha256(string.Join("\n", SampleLines.Value)));
        }

        /// <summary>
        /// One line per planned covered slot: pack, slot, planned rung, then the built level's rung, steps and
        /// fingerprint (the SHA-256 of its canonical text, covers and tags included), or REJECTED and the reason.
        /// </summary>
        private static IReadOnlyList<string> BuildSampleLines()
        {
            var lines = new List<string>();
            for (int packNumber = 0; packNumber < 25; packNumber++)
            {
                string packText = "rung-sample-" + packNumber.ToString("00", CultureInfo.InvariantCulture);
                PackId pack = PackId.Parse(packText).Or(default);
                for (int index = 0; index < 100; index++)
                {
                    var slot = new LevelIndex(index);
                    if (LotContentPlan.CoversFor(slot).TryGet(out CoverPlan? plan))
                    {
                        string head = packText + " " + index.ToString("0000", CultureInfo.InvariantCulture) + " " + plan.Rung.ToString();
                        lines.Add(head + " " + Outcome(LotPackBuilder.Build(pack, slot)));
                    }
                }
            }

            return lines;
        }

        private static string Outcome(BuildResult result) => result switch
        {
            BuildResult.Built built when built.Level.Deduction.TryGet(out DeductionGrade.Graded? grade) => string.Concat(
                "built ", grade.Rung.ToString(), " ", grade.Steps.Value.ToString(CultureInfo.InvariantCulture), " ", LotText.Fingerprint(built.Level.Level)),
            BuildResult.Rejected rejected => "REJECTED " + rejected.Reason.ToString(),
            _ => throw new InvalidOperationException("A covered slot built without a deduction grade."),
        };

        private static string Sha256(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}

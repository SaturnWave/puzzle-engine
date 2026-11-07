using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PuzzleCore.Jam;
using PuzzleCore.Randomness;
using Xunit;

namespace PuzzleCore.Tests.Jam
{
    public sealed class LotGeneratorTests
    {
        private static readonly PackId TestPack = PackId.Parse("generator-test").Or(default);

        [Fact]
        public void Generation_is_deterministic()
        {
            for (int index = 0; index < 40; index += 7)
            {
                var level = new LevelIndex(index);

                Assert.Equal(TextOf(LotGenerator.Generate(TestPack, level, LotContentPlan.For(level))), TextOf(LotGenerator.Generate(TestPack, level, LotContentPlan.For(level))));
            }
        }

        [Fact]
        public void Generated_levels_never_hit_a_construction_bug_and_meet_every_gate()
        {
            int generatedCount = 0;
            for (int index = 0; index < 100; index++)
            {
                var levelIndex = new LevelIndex(index);
                LotPlan plan = LotContentPlan.For(levelIndex);
                switch (LotGenerator.Generate(TestPack, levelIndex, plan))
                {
                    case GenerateResult.Generated generated:
                        generatedCount++;
                        LotLevel level = generated.Lot.Level;
                        Assert.Equal(plan.Sets * 3, level.VehicleCount);
                        Assert.InRange(generated.Lot.Slack, plan.MinSlack, plan.MaxSlack);
                        Assert.True(BlockedPerMille(level) >= plan.MinBlocked.Value);
                        Assert.False(level.IsCoveredLevel);
                        Assert.IsType<LotTextResult.Read>(LotText.Read(LotText.Write(level)));
                        break;
                    case GenerateResult.Rejected rejected:
                        Assert.DoesNotContain(TryFailure.LoaderRejected, rejected.Tries);
                        Assert.DoesNotContain(TryFailure.WitnessUnsolvable, rejected.Tries);
                        Assert.Equal(LotGenerator.TriesPerSlot, rejected.Tries.Count);
                        break;
                }
            }

            // The plan rejects a few hard 8×8 slots; a pack builder must cope with a rejected slot.
            Assert.True(generatedCount >= 95, "Generated " + generatedCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of 100.");
        }

        [Fact]
        public void The_golden_pack_fingerprint_is_pinned()
        {
            // Pinned from the first run of generator r=1 (2026-11-07); no independent implementation exists.
            // A change here changes every generated pack: bump the generator version instead of re-pinning.
            var texts = new List<string>();
            PackId pack = PackId.Parse("golden-gen").Or(default);
            for (int index = 0; index < 12; index++)
            {
                var level = new LevelIndex(index);
                texts.Add(TextOf(LotGenerator.Generate(pack, level, LotContentPlan.For(level))));
            }

            string actual = Sha256(string.Join("\n---\n", texts));
            Assert.True(actual == GoldenPackSha256, "The golden pack hash is now " + actual);
        }

        private const string GoldenPackSha256 = "8556f271393d250fcdf62542e8f6d641d052ae681265dc55e93903652279e45c";

        private static string TextOf(GenerateResult result) => result switch
        {
            GenerateResult.Generated generated => LotText.Write(generated.Lot.Level) + "\npeakPar=" + generated.Lot.PeakPar.ToString() + "\ntries=" + generated.Lot.Tries.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => "rejected",
        };

        private static int BlockedPerMille(LotLevel level)
        {
            int blocked = 0;
            for (int index = 0; index < level.VehicleCount; index++)
            {
                if (level.FirstOnPathAtStart(new PieceId((byte)index)).HasValue)
                {
                    blocked++;
                }
            }

            return PerMille.Of(blocked, level.VehicleCount).Value;
        }

        private static string Sha256(string text)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            var hex = new StringBuilder();
            foreach (byte value in digest)
            {
                hex.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return hex.ToString();
        }
    }
}

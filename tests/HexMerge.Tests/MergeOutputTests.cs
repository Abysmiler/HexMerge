using System.Collections.Generic;
using System.IO;
using HexMerge.Core;
using NUnit.Framework;

namespace HexMerge.Tests
{
    [TestFixture]
    public class MergeOutputTests
    {
        private static SortedDictionary<uint, byte> Map(params uint[] addrValPairs)
        {
            var m = new SortedDictionary<uint, byte>();
            for (int i = 0; i + 1 < addrValPairs.Length; i += 2)
            {
                m[addrValPairs[i]] = (byte)addrValPairs[i + 1];
            }
            return m;
        }

        [Test]
        public void Write_ContinuousBytes_RoundTripsExactly()
        {
            var bytes = Map(0, 0x01, 1, 0x02, 2, 0x03);

            string hex = HexWriter.Write(bytes);

            uint mismatch;
            Assert.That(RoundTripVerifier.Verify(hex, bytes, out mismatch), Is.True);
        }

        [Test]
        public void Write_Spanning64kBoundary_EmitsType04AndRoundTrips()
        {
            // 跨 64K 边界：0xFFFE, 0xFFFF, 0x10000
            var bytes = Map(0xFFFE, 0xAA, 0xFFFF, 0xBB, 0x10000, 0xCC);

            string hex = HexWriter.Write(bytes);

            Assert.That(hex.Contains(":02000004"), Is.True, "跨 64K 应输出类型04扩展线性地址");
            uint mismatch;
            Assert.That(RoundTripVerifier.Verify(hex, bytes, out mismatch), Is.True);
        }

        [Test]
        public void Write_AddressGap_SplitsIntoSeparateLines()
        {
            // 地址不连续：0, 1, 跳到 16
            var bytes = Map(0, 0x01, 1, 0x02, 16, 0x10);

            string hex = HexWriter.Write(bytes);

            uint mismatch;
            Assert.That(RoundTripVerifier.Verify(hex, bytes, out mismatch), Is.True,
                        "不连续地址回读不应在 0x" + mismatch.ToString("X") + " 处出错");
        }

        [Test]
        public void Verify_Mismatch_ReturnsFalseWithAddress()
        {
            var bytes = Map(0, 0x01);
            var wrong = Map(0, 0x02); // 期望值与实际不符
            string hex = HexWriter.Write(bytes);

            uint mismatch;
            bool ok = RoundTripVerifier.Verify(hex, wrong, out mismatch);

            Assert.That(ok, Is.False);
            Assert.That(mismatch, Is.EqualTo(0u));
        }

        [Test]
        public void Write_HighAddress_EmitsType04ForE00000()
        {
            // Pflash 风格高地址：必须用类型04 才能表达 0xE00000
            var bytes = Map(0xE00000, 0x01, 0xE00001, 0x02);

            string hex = HexWriter.Write(bytes);

            // 0xE00000 >> 16 = 0x00E0，类型04 数据应为 00 E0
            Assert.That(hex.Contains(":0200000400E0"), Is.True, "0xE00000 应输出类型04 数据 00E0");
            uint mismatch;
            Assert.That(RoundTripVerifier.Verify(hex, bytes, out mismatch), Is.True);
        }

        [Test]
        public void Write_WithStartAddr_EmitsType05AndRoundTrips()
        {
            var bytes = Map(0, 0x01, 1, 0x02);
            string hex = HexWriter.Write(bytes, 0x08002000);

            // 类型05 起始地址记录：:04 0000 05 08002000 <cc>
            Assert.That(hex.Contains(":0400000508002000"), Is.True, "应输出类型05 起始地址记录");
            // 05 不产生数据地址，回读校验仍应通过
            uint mismatch;
            Assert.That(RoundTripVerifier.Verify(hex, bytes, out mismatch), Is.True,
                "05 记录不应干扰回读校验（在 0x" + mismatch.ToString("X") + " 处出错）");
        }

        // ===== 真实样本端到端闭环 =====

        private static string FindSample(string name)
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(d, "Doc", name);
                if (File.Exists(candidate)) return candidate;
                d = Path.GetFullPath(Path.Combine(d, ".."));
            }
            return null;
        }

        [Test]
        public void FullPipeline_RealBootApp_RoundTripsExactly()
        {
            string boot = FindSample("7504ProjectRam-BOOT.hex");
            string app = FindSample("7504_FOC-APP.hex");
            if (boot == null || app == null) { Assert.Ignore("未找到样本文件"); return; }

            var bootImg = HexParser.Parse(boot, 0);
            var appImg = HexParser.Parse(app, 1);
            var units = ConflictDetector.Compare(bootImg, appImg);
            var merged = Arbiter.Resolve(units, null, new int[] { 0, 1 }); // BOOT>APP
            string hex = HexWriter.Write(merged);

            uint mismatch;
            bool ok = RoundTripVerifier.Verify(hex, merged, out mismatch);

            Assert.That(ok, Is.True, "真实 BOOT+APP 全流程回读应在 0x" + mismatch.ToString("X") + " 不匹配");
        }

        [Test]
        public void FullPipeline_RealThreeFiles_RoundTripsExactly()
        {
            string boot = FindSample("7504ProjectRam-BOOT.hex");
            string app = FindSample("7504_FOC-APP.hex");
            string pflash = FindSample("DFLASH_0xE00000_0xE0AFFF.dat");
            if (boot == null || app == null || pflash == null) { Assert.Ignore("未找到样本文件"); return; }

            var bootImg = HexParser.Parse(boot, 0);
            var appImg = HexParser.Parse(app, 1);
            var pflashImg = BinaryDatParser.Parse(pflash, 0xE00000, 2);

            var units = ConflictDetector.Compare(bootImg, appImg, pflashImg);

            // BOOT∩APP 真实冲突 174 字节；Pflash 地址独立，无新增冲突
            Assert.That(ConflictDetector.CountConflicts(units), Is.EqualTo(174),
                "三文件冲突字节应仍为 174（Pflash 独立无冲突）");

            var merged = Arbiter.Resolve(units, null, new int[] { 0, 1, 2 });
            string hex = HexWriter.Write(merged);

            uint mismatch;
            bool ok = RoundTripVerifier.Verify(hex, merged, out mismatch);
            Assert.That(ok, Is.True, "三文件全流程回读应在 0x" + mismatch.ToString("X") + " 不匹配");

            // Pflash 独立地址（0xE00000 段）应完整保留到合并结果
            int pflashKept = 0;
            foreach (uint a in pflashImg.Addresses)
            {
                byte v;
                if (merged.TryGetValue(a, out v))
                {
                    byte pv;
                    pflashImg.TryGet(a, out pv);
                    Assert.That(v, Is.EqualTo(pv), "Pflash 地址 0x" + a.ToString("X") + " 字节应保留");
                    pflashKept++;
                }
            }
            Assert.That(pflashKept, Is.EqualTo(pflashImg.Count), "Pflash 全部字节应保留到合并结果");
        }
    }
}

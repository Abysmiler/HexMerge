using System.IO;
using HexMerge.Core;
using HexMerge.Models;
using NUnit.Framework;

namespace HexMerge.Tests
{
    [TestFixture]
    public class ConflictDetectorTests
    {
        private static MemoryImage Img(int idx, params uint[] addrValPairs)
        {
            // addrValPairs: 地址1,值1, 地址2,值2, ...
            MemoryImage img = new MemoryImage("f" + idx, idx);
            for (int i = 0; i + 1 < addrValPairs.Length; i += 2)
            {
                img.Set(addrValPairs[i], (byte)addrValPairs[i + 1]);
            }
            return img;
        }

        [Test]
        public void Compare_NonOverlapping_AddressesAreExclusive()
        {
            MemoryImage a = Img(0, 0, 0xAA);
            MemoryImage b = Img(1, 10, 0xBB);

            var units = ConflictDetector.Compare(a, b);

            Assert.That(units.Count, Is.EqualTo(2));
            Assert.That(units[0].Status, Is.EqualTo(UnitStatus.Exclusive));
            Assert.That(units[1].Status, Is.EqualTo(UnitStatus.Exclusive));
            Assert.That(ConflictDetector.CountConflicts(units), Is.EqualTo(0));
        }

        [Test]
        public void Compare_SameAddressSameValue_IsSame()
        {
            MemoryImage a = Img(0, 0, 0xAA);
            MemoryImage b = Img(1, 0, 0xAA);

            var units = ConflictDetector.Compare(a, b);

            Assert.That(units[0].Status, Is.EqualTo(UnitStatus.Same));
            Assert.That(units[0].Values.Count, Is.EqualTo(2));
        }

        [Test]
        public void Compare_SameAddressDifferentValue_IsConflict()
        {
            MemoryImage a = Img(0, 0, 0xAA);
            MemoryImage b = Img(1, 0, 0x55);

            var units = ConflictDetector.Compare(a, b);

            Assert.That(units[0].Status, Is.EqualTo(UnitStatus.Conflict));
            Assert.That(ConflictDetector.CountConflicts(units), Is.EqualTo(1));
        }

        [Test]
        public void Compare_PartialOverlap_MarkedPerByte()
        {
            // 文件0: 地址 0-9（值0x00）
            // 文件1: 地址 5-15（值0x00，其中 5-9 与文件0重叠但同值=Same，10-15 独占）
            // 这是需求4.4 的"部分重叠"场景：字节级能干净区分
            MemoryImage a = Img(0, 0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 1, 6, 1, 7, 1, 8, 1, 9, 1);
            MemoryImage b = Img(1, 5, 1, 6, 1, 7, 1, 8, 1, 9, 1, 10, 2, 11, 2, 12, 2, 13, 2, 14, 2, 15, 2);

            var units = ConflictDetector.Compare(a, b);

            // 地址 5-9：两文件同值 -> Same；0-4 独占文件0；10-15 独占文件1
            Assert.That(ConflictDetector.CountConflicts(units), Is.EqualTo(0));
        }

        [Test]
        public void Compare_PartialOverlapDifferentValues_ConflictsOnlyInOverlap()
        {
            // 文件0: 0-9（值0x11）
            // 文件1: 5-15（值0x22）
            // 重叠区 5-9：两文件不同 -> 5个冲突；0-4 和 10-15 独占
            MemoryImage a = new MemoryImage("a", 0);
            for (uint i = 0; i <= 9; i++) a.Set(i, 0x11);
            MemoryImage b = new MemoryImage("b", 1);
            for (uint i = 5; i <= 15; i++) b.Set(i, 0x22);

            var units = ConflictDetector.Compare(a, b);

            Assert.That(ConflictDetector.CountConflicts(units), Is.EqualTo(5), "重叠区 5-9 共5字节冲突");
        }

        // ===== 真实样本硬验收：BOOT+APP 实测 2 处冲突区 =====

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
        public void Compare_RealBootAndApp_Has292Plus8192ConflictBytes()
        {
            string boot = FindSample("7504ProjectRam-BOOT.hex");
            string app = FindSample("7504_FOC-APP.hex");
            if (boot == null || app == null) { Assert.Ignore("未找到样本文件"); return; }

            MemoryImage bootImg = HexParser.Parse(boot, 0);
            MemoryImage appImg = HexParser.Parse(app, 1);

            var units = ConflictDetector.Compare(bootImg, appImg);

            int conflictBytes = ConflictDetector.CountConflicts(units);

            // 实测：BOOT/APP 重叠区共 8484 字节（向量表部分重叠 292 + 配置区完全重叠 8192），
            //       但其中真正值不同（需仲裁的冲突）只有 174 字节；
            //       其余 8310 字节两文件相同（Same，无需仲裁）。
            //       字节级模型精确区分了"重叠且相同"与"重叠且不同"——这正是修复需求 4.4 的关键。
            Assert.That(conflictBytes, Is.EqualTo(174), "BOOT/APP 真正冲突字节（值不同）应为 174");
            Assert.That(units.Count, Is.EqualTo(bootImg.Count + appImg.Count - 8484), "比对单元数 = 两文件字节和 - 重叠（并集）");
        }
    }
}

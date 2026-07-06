using System.IO;
using FTHexMerge.Core;
using FTHexMerge.Models;
using NUnit.Framework;

namespace FTHexMerge.Tests
{
    [TestFixture]
    public class BinaryDatParserTests
    {
        /// <summary>写几个字节到临时 .dat 文件。</summary>
        private static string WriteTempDat(byte[] data)
        {
            string tmp = Path.GetTempFileName();
            File.WriteAllBytes(tmp, data);
            return tmp;
        }

        [Test]
        public void Parse_PlacesAllBytesStartingAtGivenBaseAddress()
        {
            byte[] data = { 0x10, 0x20, 0x30, 0x40 };
            string path = WriteTempDat(data);

            MemoryImage img = BinaryDatParser.Parse(path, 0xE00000, 2);

            Assert.That(img.Count, Is.EqualTo(4));
            Assert.That(img.MinAddress, Is.EqualTo(0xE00000));
            Assert.That(img.MaxAddress, Is.EqualTo(0xE00003));

            byte v;
            Assert.That(img.TryGet(0xE00000, out v), Is.True);
            Assert.That(v, Is.EqualTo(0x10));
            Assert.That(img.TryGet(0xE00003, out v), Is.True);
            Assert.That(v, Is.EqualTo(0x40));
        }

        [Test]
        public void Parse_BaseZero_Works()
        {
            byte[] data = { 0xAA, 0xBB };
            string path = WriteTempDat(data);

            MemoryImage img = BinaryDatParser.Parse(path, 0, 0);

            Assert.That(img.MinAddress, Is.EqualTo(0u));
            Assert.That(img.MaxAddress, Is.EqualTo(1u));
            Assert.That(img.Count, Is.EqualTo(2));
        }

        [Test]
        public void GetSegments_BinaryFileIsOneContiguousSegment()
        {
            byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05 };
            string path = WriteTempDat(data);

            MemoryImage img = BinaryDatParser.Parse(path, 0x100, 0);

            Assert.That(img.GetSegments().Count, Is.EqualTo(1));
            Segment s = img.GetSegments()[0];
            Assert.That(s.Start, Is.EqualTo(0x100));
            Assert.That(s.End, Is.EqualTo(0x104));
        }

        // ===== 真实样本硬验收 =====

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
        public void Parse_RealPflashDat_MatchesKnownFacts()
        {
            string path = FindSample("DFLASH_0xE00000_0xE0AFFF.dat");
            if (path == null) { Assert.Ignore("未找到 Pflash 样本文件"); return; }

            MemoryImage img = BinaryDatParser.Parse(path, 0xE00000, 2);

            Assert.That(img.Count, Is.EqualTo(45056), "Pflash 字节总数应为 45056 (0xB000)");
            Assert.That(img.MinAddress, Is.EqualTo(0xE00000));
            Assert.That(img.MaxAddress, Is.EqualTo(0xE0AFFF));
            Assert.That(img.GetSegments().Count, Is.EqualTo(1), "全 0xFF 也应是连续的 1 段");

            // 已知有效数据：0xE01000=01 00 00 00, 0xE01010=AA（实测）
            byte v;
            Assert.That(img.TryGet(0xE01000, out v), Is.True);
            Assert.That(v, Is.EqualTo(0x01));
            Assert.That(img.TryGet(0xE01010, out v), Is.True);
            Assert.That(v, Is.EqualTo(0xAA));
        }
    }
}

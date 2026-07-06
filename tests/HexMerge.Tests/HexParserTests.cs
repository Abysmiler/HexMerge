using System.IO;
using System.Text;
using HexMerge.Core;
using HexMerge.Models;
using NUnit.Framework;

namespace HexMerge.Tests
{
    [TestFixture]
    public class HexParserTests
    {
        /// <summary>生成一条带正确校验和的 HEX 行（便于构造测试数据）。</summary>
        private static string HexLine(uint addr, int type, byte[] data)
        {
            int sum = data.Length;
            sum += (int)((addr >> 8) & 0xFF);
            sum += (int)(addr & 0xFF);
            sum += type;
            for (int i = 0; i < data.Length; i++) sum += data[i];
            int cc = (~sum + 1) & 0xFF; // 校验和 = 二进制补码

            StringBuilder sb = new StringBuilder();
            sb.Append(':');
            sb.Append(data.Length.ToString("X2"));
            sb.Append(addr.ToString("X4"));
            sb.Append(type.ToString("X2"));
            for (int i = 0; i < data.Length; i++) sb.Append(data[i].ToString("X2"));
            sb.Append(cc.ToString("X2"));
            return sb.ToString();
        }

        private const string EofLine = ":00000001FF";

        /// <summary>把 HEX 文本写到临时文件后解析。</summary>
        private static MemoryImage ParseText(string hexText, int fileIndex)
        {
            string tmp = Path.GetTempFileName();
            File.WriteAllText(tmp, hexText);
            try
            {
                return HexParser.Parse(tmp, fileIndex);
            }
            finally
            {
                File.Delete(tmp);
            }
        }

        /// <summary>取某地址字节，不存在则断言失败。</summary>
        private static byte Get(MemoryImage img, uint addr)
        {
            byte v;
            Assert.That(img.TryGet(addr, out v), Is.True, "地址 0x" + addr.ToString("X") + " 应存在");
            return v;
        }

        /// <summary>断言解析会抛 HexParseException，并返回异常。</summary>
        private static HexParseException ExpectError(string hex)
        {
            HexParseException ex = null;
            try
            {
                ParseText(hex, 0);
            }
            catch (HexParseException e)
            {
                ex = e;
            }
            Assert.That(ex, Is.Not.Null, "应抛出 HexParseException");
            return ex;
        }

        [Test]
        public void Parse_DataRecord_StoresBytesAtCorrectAddress()
        {
            byte[] data = { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                            0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10 };
            string hex = HexLine(0x0000, 0x00, data) + "\n" + EofLine;

            MemoryImage img = ParseText(hex, 0);

            Assert.That(img.Count, Is.EqualTo(16));
            Assert.That(Get(img, 0x0000), Is.EqualTo(0x01));
            Assert.That(Get(img, 0x000F), Is.EqualTo(0x10));
        }

        [Test]
        public void Parse_BadChecksum_ThrowsWithLineNumber()
        {
            // 1 字节数据 0x01 在地址 0，正确校验和应为 0xFE，这里故意写成 0x00
            string hex = ":010000000100\n" + EofLine;

            HexParseException ex = ExpectError(hex);

            Assert.That(ex.LineNumber, Is.EqualTo(1));
        }

        [Test]
        public void Parse_Type04_ExtendsAddressByLeftShift16()
        {
            // 类型 04：数据 0x0001 -> base = 0x0001 << 16 = 0x00010000
            string hex = HexLine(0x0000, 0x04, new byte[] { 0x00, 0x01 })
                       + "\n" + HexLine(0x0000, 0x00, new byte[] { 0xAA }) + "\n" + EofLine;

            MemoryImage img = ParseText(hex, 0);

            Assert.That(Get(img, 0x00010000), Is.EqualTo(0xAA));
        }

        [Test]
        public void Parse_Type02_ExtendsAddressByLeftShift4()
        {
            // 类型 02：数据 0x0010 -> base = 0x0010 << 4 = 0x0100
            string hex = HexLine(0x0000, 0x02, new byte[] { 0x00, 0x10 })
                       + "\n" + HexLine(0x0000, 0x00, new byte[] { 0xAA }) + "\n" + EofLine;

            MemoryImage img = ParseText(hex, 0);

            Assert.That(Get(img, 0x0100), Is.EqualTo(0xAA));
        }

        [Test]
        public void Parse_Type03_IsIgnoredWithoutError()
        {
            string hex = HexLine(0x0000, 0x03, new byte[] { 0x00, 0x00, 0x00, 0x80 })
                       + "\n" + HexLine(0x0000, 0x00, new byte[] { 0xAA }) + "\n" + EofLine;

            MemoryImage img = ParseText(hex, 0);

            Assert.That(img.Count, Is.EqualTo(1));
            Assert.That(Get(img, 0x0000), Is.EqualTo(0xAA));
        }

        [Test]
        public void Parse_Type05_RecordsStartAddress()
        {
            // 类型05：起始线性地址 0x08002000（记录但不作为数据字节）
            string hex = HexLine(0x0000, 0x05, new byte[] { 0x08, 0x00, 0x20, 0x00 })
                       + "\n" + HexLine(0x0000, 0x00, new byte[] { 0xAA }) + "\n" + EofLine;

            MemoryImage img = ParseText(hex, 0);

            Assert.That(img.StartLinearAddress, Is.EqualTo(0x08002000u));
            Assert.That(img.Count, Is.EqualTo(1));          // 05 不产生数据字节
            Assert.That(Get(img, 0x0000), Is.EqualTo(0xAA));
        }

        [Test]
        public void Parse_EofRecord_StopsParsing()
        {
            // EOF 之后的数据记录不应被解析
            string hex = HexLine(0x0000, 0x00, new byte[] { 0xAA })
                       + "\n" + EofLine + "\n" + HexLine(0x0001, 0x00, new byte[] { 0xBB });

            MemoryImage img = ParseText(hex, 0);

            Assert.That(img.Count, Is.EqualTo(1));
            byte dummy;
            Assert.That(img.TryGet(0x0001, out dummy), Is.False);
        }

        [Test]
        public void Parse_InvalidHexChar_ThrowsWithLineNumber()
        {
            // 含非法字符 'ZZ'
            string hex = ":01000000ZZ55\n" + EofLine;

            HexParseException ex = ExpectError(hex);

            Assert.That(ex.LineNumber, Is.EqualTo(1));
        }

        [Test]
        public void GetSegments_MergesContiguousAddressesIntoSegments()
        {
            MemoryImage img = new MemoryImage("t.hex", 0);
            img.Set(0, 0x00); img.Set(1, 0x01); img.Set(2, 0x02); // 段1: 0-2
            img.Set(10, 0xAA); img.Set(11, 0xBB);                 // 段2: 10-11

            System.Collections.Generic.List<Segment> segs = img.GetSegments();

            Assert.That(segs.Count, Is.EqualTo(2));
            Assert.That(segs[0].Start, Is.EqualTo(0u));
            Assert.That(segs[0].End, Is.EqualTo(2u));
            Assert.That(segs[1].Start, Is.EqualTo(10u));
            Assert.That(segs[1].End, Is.EqualTo(11u));
        }

        // ===== 真实样本集成测试（硬验收：与实测数据一致）=====

        /// <summary>从测试目录向上查找 Doc 目录下的样本文件。</summary>
        private static string FindSample(string name)
        {
            string d = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 6; i++)
            {
                string candidate = System.IO.Path.Combine(d, "Doc", name);
                if (System.IO.File.Exists(candidate)) return candidate;
                d = System.IO.Path.GetFullPath(System.IO.Path.Combine(d, ".."));
            }
            return null;
        }

        [Test]
        public void Parse_RealBootFile_MatchesKnownFacts()
        {
            string path = FindSample("7504ProjectRam-BOOT.hex");
            if (path == null) { Assert.Ignore("未找到 BOOT 样本文件"); return; }

            MemoryImage img = HexParser.Parse(path, 0);

            Assert.That(img.Count, Is.EqualTo(57344), "BOOT 字节总数应为 57344 (0xE000)");
            Assert.That(img.MinAddress, Is.EqualTo(0u));
            Assert.That(img.MaxAddress, Is.EqualTo(0x00080FFF));
            Assert.That(img.GetSegments().Count, Is.EqualTo(2), "BOOT 应归并为 2 段");
        }

        [Test]
        public void Parse_RealAppFile_MatchesKnownFacts()
        {
            string path = FindSample("7504_FOC-APP.hex");
            if (path == null) { Assert.Ignore("未找到 APP 样本文件"); return; }

            MemoryImage img = HexParser.Parse(path, 1);

            Assert.That(img.Count, Is.EqualTo(196900), "APP 字节总数应为 196900");
            Assert.That(img.MinAddress, Is.EqualTo(0u));
            Assert.That(img.MaxAddress, Is.EqualTo(0x00080FFF));
            Assert.That(img.GetSegments().Count, Is.EqualTo(3), "APP 应归并为 3 段");
        }
    }
}

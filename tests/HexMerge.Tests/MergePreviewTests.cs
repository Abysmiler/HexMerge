using System.Collections.Generic;
using FTHexMerge.ViewModels;
using NUnit.Framework;

namespace FTHexMerge.Tests
{
    /// <summary>
    /// CompareViewModel.ComputePreview 预览摘要计算测试（纯函数，不依赖 WPF）。
    /// 覆盖段数、间隙、填充后大小等。
    /// </summary>
    [TestFixture]
    public class MergePreviewTests
    {
        private static SortedDictionary<uint, byte> Map(params uint[] kv)
        {
            var m = new SortedDictionary<uint, byte>();
            for (int i = 0; i + 1 < kv.Length; i += 2) m[kv[i]] = (byte)kv[i + 1];
            return m;
        }

        [Test]
        public void Contiguous_SingleSegment_NoGap()
        {
            var m = Map(0, 1, 1, 2, 2, 3);
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.None, uint.MaxValue);
            Assert.That(p.SegmentCount, Is.EqualTo(1));
            Assert.That(p.GapCount, Is.EqualTo(0));
            Assert.That(p.TotalBytes, Is.EqualTo(3));
            Assert.That(p.MinAddress, Is.EqualTo(0u));
            Assert.That(p.MaxAddress, Is.EqualTo(2u));
            Assert.That(p.FinalBytes, Is.EqualTo(3));
        }

        [Test]
        public void TwoSegments_OneGap()
        {
            // 0,1 连续；2,3,4 间隙；5 起新段
            var m = Map(0, 1, 1, 2, 5, 3);
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.None, uint.MaxValue);
            Assert.That(p.SegmentCount, Is.EqualTo(2));
            Assert.That(p.GapCount, Is.EqualTo(1));
            Assert.That(p.GapBytes, Is.EqualTo(3));
            Assert.That(p.FinalBytes, Is.EqualTo(3)); // 不填充
        }

        [Test]
        public void FillFF_AddsGapBytesToFinal()
        {
            var m = Map(0, 1, 5, 2); // 间隙 1,2,3,4 = 4 字节
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.FillFF, uint.MaxValue);
            Assert.That(p.FillBytes, Is.EqualTo(4));
            Assert.That(p.FinalBytes, Is.EqualTo(2 + 4));
        }

        [Test]
        public void Empty_ReturnsZeros()
        {
            var p = CompareViewModel.ComputePreview(new SortedDictionary<uint, byte>(), CompareViewModel.GapFill.None, uint.MaxValue);
            Assert.That(p.SegmentCount, Is.EqualTo(0));
            Assert.That(p.TotalBytes, Is.EqualTo(0));
            Assert.That(p.FinalBytes, Is.EqualTo(0));
        }

        [Test]
        public void LargeGap_NotCountedAsFillable_RecordsSkipped()
        {
            // 间隙 > 阈值：计入 GapBytes，但不计入可填充；并记入 SkippedGaps 供预览说明
            uint thr = CompareViewModel.GapFillMaxBytes;
            uint far = thr + 10;                       // gap = thr + 9，超阈值
            var m = Map(0, 1, far, 2);
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.FillFF, uint.MaxValue);
            Assert.That(p.GapCount, Is.EqualTo(1));
            Assert.That(p.SegmentCount, Is.EqualTo(2));
            Assert.That(p.FillBytes, Is.EqualTo(0));          // 超阈值不填
            Assert.That(p.FinalBytes, Is.EqualTo(2));         // 大间隙不填
            Assert.That(p.SkippedGaps.Count, Is.EqualTo(1));  // 记录了跳过的间隙
            Assert.That(p.SkippedGapBytes, Is.EqualTo(far - 1));
            Assert.That(p.SkippedGaps[0].Start, Is.EqualTo(1u));
            Assert.That(p.SkippedGaps[0].End, Is.EqualTo(far - 1));
            Assert.That(p.SkippedGaps[0].Length, Is.EqualTo(far - 1));
        }

        [Test]
        public void FillFF_BelowThreshold_IsFilled()
        {
            // 间隙 < 阈值：可填充，不计入跳过
            uint thr = CompareViewModel.GapFillMaxBytes;
            uint far = thr >> 1;                       // 阈值的一半
            var m = Map(0, 1, far, 2);
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.FillFF, uint.MaxValue);
            Assert.That(p.FillBytes, Is.EqualTo(far - 1));    // 全部间隙可填
            Assert.That(p.SkippedGaps.Count, Is.EqualTo(0));
            Assert.That(p.FinalBytes, Is.EqualTo(2 + far - 1));
        }

        [Test]
        public void GapAtThreshold_IsFilled_AboveIsSkipped()
        {
            // gap 恰等于阈值（≤）仍按可填充；阈值+1 才跳过
            uint thr = CompareViewModel.GapFillMaxBytes;
            var mEq = Map(0, 1, thr + 1, 2);           // gap == thr，可填
            var pEq = CompareViewModel.ComputePreview(mEq, CompareViewModel.GapFill.FillFF, uint.MaxValue);
            Assert.That(pEq.FillBytes, Is.EqualTo(thr));
            Assert.That(pEq.SkippedGaps.Count, Is.EqualTo(0));

            var mOver = Map(0, 1, thr + 2, 2);         // gap == thr + 1，跳过
            var pOver = CompareViewModel.ComputePreview(mOver, CompareViewModel.GapFill.FillFF, uint.MaxValue);
            Assert.That(pOver.FillBytes, Is.EqualTo(0));
            Assert.That(pOver.SkippedGaps.Count, Is.EqualTo(1));
        }

        [Test]
        public void Segments_ListedCorrectly()
        {
            // seg1: 0-1，seg2: 5-6
            var m = Map(0, 1, 1, 2, 5, 3, 6, 4);
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.None, uint.MaxValue);
            Assert.That(p.Segments.Count, Is.EqualTo(2));
            Assert.That(p.Segments[0].Start, Is.EqualTo(0u));
            Assert.That(p.Segments[0].End, Is.EqualTo(1u));
            Assert.That(p.Segments[0].Length, Is.EqualTo(2));
            Assert.That(p.Segments[1].Start, Is.EqualTo(5u));
            Assert.That(p.Segments[1].End, Is.EqualTo(6u));
            Assert.That(p.Segments[1].Length, Is.EqualTo(2));
        }

        [Test]
        public void DFlashGap_IdentifiedAndIgnored()
        {
            // PFlash 段 0-1，间隙 2..0xFF，DFlash 段从 dflashStart(0x100) 起
            uint dfs = 0x100;
            var m = Map(0, 1, 1, 2, dfs, 3, dfs + 1, 4);
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.FillFF, dfs);
            Assert.That(p.DFlashGaps.Count, Is.EqualTo(1));   // 落点跨进 DFlash → 识别为到 DFlash 间隙
            Assert.That(p.DFlashGapBytes, Is.EqualTo(dfs - 2));
            Assert.That(p.DFlashGaps[0].Start, Is.EqualTo(2u));
            Assert.That(p.DFlashGaps[0].End, Is.EqualTo(dfs - 1));
            Assert.That(p.FillBytes, Is.EqualTo(0));          // DFlash 间隙不填（即使选了 FillFF）
            Assert.That(p.SkippedGaps.Count, Is.EqualTo(0));  // 不归入"超阈值"
            Assert.That(p.FinalBytes, Is.EqualTo(4));         // 仅 4 字节真实数据，间隙未膨胀输出
        }

        [Test]
        public void NoDFlash_SameGapStillTreatedByThreshold()
        {
            // 无 DFlash 起点（哨兵 MaxValue）：间隙仍按阈值处理，不被误识别为 DFlash
            var m = Map(0, 1, 5, 2); // 间隙 1,2,3,4 = 4 字节（< 阈值）
            var p = CompareViewModel.ComputePreview(m, CompareViewModel.GapFill.FillFF, uint.MaxValue);
            Assert.That(p.DFlashGaps.Count, Is.EqualTo(0));
            Assert.That(p.FillBytes, Is.EqualTo(4));          // 按阈值可填
        }
    }
}

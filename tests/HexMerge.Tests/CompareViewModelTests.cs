using FTHexMerge.ViewModels;
using NUnit.Framework;

namespace FTHexMerge.Tests
{
    /// <summary>
    /// CompareViewModel 的冲突段跳转逻辑测试（纯逻辑，不依赖 WPF 渲染）。
    /// 布局约定：[否, 是, 是(与上一行同段), 否, 是] —— 两个冲突段，起始行分别为 idx1 与 idx4。
    /// </summary>
    [TestFixture]
    public class CompareViewModelTests
    {
        private static CompareViewModel.Row MakeRow(uint addr, bool conflict)
        {
            return new CompareViewModel.Row { Address = addr, HasConflict = conflict };
        }

        private static CompareViewModel MakeVm(params bool[] conflicts)
        {
            var vm = new CompareViewModel();
            for (int i = 0; i < conflicts.Length; i++)
                vm.Rows.Add(MakeRow((uint)(i * 16), conflicts[i]));
            return vm;
        }

        // [否, 是, 是(同段), 否, 是]
        private static readonly bool[] Layout = { false, true, true, false, true };

        [Test]
        public void FindNext_FromBeforeAll_ReturnsFirstSegmentStart()
        {
            var vm = MakeVm(Layout);
            Assert.That(vm.FindNextConflictSegment(-1), Is.EqualTo(1));
        }

        [Test]
        public void FindNext_FromInsideSegment_SkipsToNextSegmentStart()
        {
            var vm = MakeVm(Layout);
            // 段内（idx1/idx2 同属段1）应跳到段2起始 idx4，而不是同段的 idx2
            Assert.That(vm.FindNextConflictSegment(1), Is.EqualTo(4));
            Assert.That(vm.FindNextConflictSegment(2), Is.EqualTo(4));
        }

        [Test]
        public void FindNext_FromBeforeNextSegment_ReturnsIt()
        {
            var vm = MakeVm(Layout);
            Assert.That(vm.FindNextConflictSegment(3), Is.EqualTo(4));
        }

        [Test]
        public void FindNext_FromLastSegment_WrapsAround()
        {
            var vm = MakeVm(Layout);
            Assert.That(vm.FindNextConflictSegment(4), Is.EqualTo(1));
        }

        [Test]
        public void FindNext_FromOutOfRange_WrapsToFirst()
        {
            var vm = MakeVm(Layout);
            Assert.That(vm.FindNextConflictSegment(99), Is.EqualTo(1));
        }

        [Test]
        public void FindNext_NoConflicts_ReturnsMinusOne()
        {
            var vm = MakeVm(false, false, false);
            Assert.That(vm.FindNextConflictSegment(-1), Is.EqualTo(-1));
        }

        // ===== 上一个冲突（FindPrevConflictSegment）=====

        [Test]
        public void FindPrev_FromMid_ReturnsPreviousSegmentStart()
        {
            var vm = MakeVm(Layout); // [否, 是, 是(同段), 否, 是]
            Assert.That(vm.FindPrevConflictSegment(4), Is.EqualTo(1)); // seg2@4 的上一段 = seg1@1
            Assert.That(vm.FindPrevConflictSegment(2), Is.EqualTo(4)); // seg1 第2行：跳过 seg1，无前段 → 回绕 seg2@4
        }

        [Test]
        public void FindPrev_FromFirst_WrapsToLast()
        {
            var vm = MakeVm(Layout);
            Assert.That(vm.FindPrevConflictSegment(1), Is.EqualTo(4)); // 回绕到 seg2@4
            Assert.That(vm.FindPrevConflictSegment(0), Is.EqualTo(4));
        }

        [Test]
        public void FindPrev_NoConflicts_ReturnsMinusOne()
        {
            var vm = MakeVm(false, false, false);
            Assert.That(vm.FindPrevConflictSegment(2), Is.EqualTo(-1));
        }

        // ===== 转到地址（FindRowForAddress / GoToAddress）=====

        [Test]
        public void FindRowForAddress_ExactAligned_AndNearestBelow()
        {
            var vm = new CompareViewModel();
            vm.Rows.Add(new CompareViewModel.Row { Address = 0x00 });
            vm.Rows.Add(new CompareViewModel.Row { Address = 0x10 });
            vm.Rows.Add(new CompareViewModel.Row { Address = 0x20 });
            Assert.That(vm.FindRowForAddress(0x14), Is.EqualTo(1));  // 0x14 → 0x10 行
            Assert.That(vm.FindRowForAddress(0x00), Is.EqualTo(0));
            Assert.That(vm.FindRowForAddress(0x2F), Is.EqualTo(2));  // 0x20 行
            Assert.That(vm.FindRowForAddress(0x100), Is.EqualTo(2)); // 无精确，最近(下)=0x20
        }

        [Test]
        public void GoToAddress_ParsesHexAndInvalid()
        {
            var vm = new CompareViewModel();
            vm.Rows.Add(new CompareViewModel.Row { Address = 0x00 });
            vm.Rows.Add(new CompareViewModel.Row { Address = 0x10 });
            Assert.That(vm.GoToAddress("0x14"), Is.EqualTo(1));
            Assert.That(vm.GoToAddress("14"), Is.EqualTo(1));    // 无 0x 前缀，仍按十六进制
            Assert.That(vm.GoToAddress("xyz"), Is.EqualTo(-1));  // 无效
            Assert.That(vm.GoToAddress(""), Is.EqualTo(-1));     // 空
        }

        [Test]
        public void FindNext_EmptyRows_ReturnsMinusOne()
        {
            var vm = new CompareViewModel();
            Assert.That(vm.FindNextConflictSegment(-1), Is.EqualTo(-1));
        }

        [Test]
        public void GoNextConflict_ReturnsIndexAndNotFoundReturnsMinusOne()
        {
            var vm = MakeVm(false, false);
            Assert.That(vm.GoNextConflict(0), Is.EqualTo(-1));
            Assert.That(vm.StatusMessage, Does.Contain("没有冲突"));
        }

        [Test]
        public void GoNextConflict_HitsTarget()
        {
            var vm = MakeVm(Layout);
            Assert.That(vm.GoNextConflict(0), Is.EqualTo(1));
            Assert.That(vm.StatusMessage, Does.Contain("0x00000010"));
        }

        /// <summary>首次进入（CurrentConflictIndex==0，浮窗显示"-/N"）时光标在行0，
        /// 若行0即首个冲突段起始，"下一个"应落到该段而非被跳过。</summary>
        [Test]
        public void GoNextConflict_FirstEntry_Row0IsConflict_LandsOnFirstSegment()
        {
            // 行0即首个冲突段起始：[是, 是, 否, 是] → seg0@0, seg1@3，共 2 段。
            var vm = MakeVm(true, true, false, true);
            // 未定位（CurrentConflictIndex==0），光标在首可见行0
            Assert.That(vm.GoNextConflict(0), Is.EqualTo(0)); // 应回到 seg0@0，而非跳到 seg1@3
        }

        // ===== 「无」优先级模式：未确认冲突段计数（CountUnconfirmedConflictSegments）=====

        [Test]
        public void CountUnconfirmed_AllSegmentsUnconfirmed_ReturnsTotal()
        {
            // [否, 是(seg0), 是(同段), 否, 是(seg1)]：两段，均未手动确认（RowChoice 默认 -1）
            var vm = MakeVm(Layout);
            Assert.That(vm.CountUnconfirmedConflictSegments(), Is.EqualTo(2));
        }

        [Test]
        public void CountUnconfirmed_ConfirmedSegmentExcluded()
        {
            var vm = MakeVm(Layout);
            vm.Rows[1].RowChoice = 0; // 手动确认 seg0（段首行）
            Assert.That(vm.CountUnconfirmedConflictSegments(), Is.EqualTo(1)); // 只剩 seg1
        }

        [Test]
        public void CountUnconfirmed_AllConfirmed_ReturnsZero()
        {
            var vm = MakeVm(Layout);
            vm.Rows[1].RowChoice = 0; // seg0
            vm.Rows[4].RowChoice = 1; // seg1
            Assert.That(vm.CountUnconfirmedConflictSegments(), Is.EqualTo(0));
        }

        [Test]
        public void CountUnconfirmed_NoConflicts_ReturnsZero()
        {
            var vm = MakeVm(false, false, false);
            Assert.That(vm.CountUnconfirmedConflictSegments(), Is.EqualTo(0));
        }
    }
}

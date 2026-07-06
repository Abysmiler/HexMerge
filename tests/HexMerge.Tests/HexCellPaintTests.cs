using FTHexMerge.ViewModels;
using FTHexMerge.Views;
using NUnit.Framework;

namespace FTHexMerge.Tests
{
    /// <summary>
    /// HexCellPaint 着色决策测试（纯逻辑，不加载 WPF 渲染管线）。
    /// 覆盖行背景 / 字节背景 / 字色的优先级真值表。
    /// </summary>
    [TestFixture]
    public class HexCellPaintTests
    {
        // 构造一个 2 文件的 Row，所有字节默认有值(0xAB)、非冲突、非缺失、未选中、RowChoice=-1
        private static CompareViewModel.Row MakeRow(int fileCount = 2)
        {
            var row = new CompareViewModel.Row();
            row.Cells = new byte?[fileCount][];
            for (int f = 0; f < fileCount; f++)
            {
                row.Cells[f] = new byte?[16];
                for (int k = 0; k < 16; k++) row.Cells[f][k] = 0xAB;
            }
            row.ConflictBits = new bool[16];
            row.IsMissingPerFile = new bool[fileCount];
            row.RowChoice = -1;
            return row;
        }

        // ===== 行背景 =====

        [Test]
        public void RowBg_Default_IsWhite()
        {
            Assert.That(HexCellPaint.ResolveRowBg(MakeRow(), 0), Is.EqualTo(HexCellPaint.RowBg.White));
        }

        [Test]
        public void RowBg_HasConflict_IsConflict()
        {
            var row = MakeRow(); row.HasConflict = true;
            Assert.That(HexCellPaint.ResolveRowBg(row, 0), Is.EqualTo(HexCellPaint.RowBg.Conflict));
        }

        [Test]
        public void RowBg_MissingFile_IsDeleted()
        {
            var row = MakeRow(); row.IsMissingPerFile[1] = true;
            Assert.That(HexCellPaint.ResolveRowBg(row, 1), Is.EqualTo(HexCellPaint.RowBg.Deleted));
            Assert.That(HexCellPaint.ResolveRowBg(row, 0), Is.EqualTo(HexCellPaint.RowBg.White));
        }

        [Test]
        public void RowBg_RowChoiceEqualsF_IsChosen()
        {
            var row = MakeRow(); row.RowChoice = 0;
            Assert.That(HexCellPaint.ResolveRowBg(row, 0), Is.EqualTo(HexCellPaint.RowBg.Chosen));
            Assert.That(HexCellPaint.ResolveRowBg(row, 1), Is.EqualTo(HexCellPaint.RowBg.White));
        }

        [Test]
        public void RowBg_RowChoice_BeatsConflictAndMissing()
        {
            // RowChoice==0 且该行冲突且该文件缺失 → 仍 Chosen（优先级最高）
            var row = MakeRow();
            row.HasConflict = true;
            row.IsMissingPerFile[0] = true;
            row.RowChoice = 0;
            Assert.That(HexCellPaint.ResolveRowBg(row, 0), Is.EqualTo(HexCellPaint.RowBg.Chosen));
        }

        [Test]
        public void RowBg_Missing_BeatsConflict()
        {
            // 缺失且冲突、RowChoice=-1 → Deleted（缺失优先于冲突）
            var row = MakeRow();
            row.HasConflict = true;
            row.IsMissingPerFile[0] = true;
            Assert.That(HexCellPaint.ResolveRowBg(row, 0), Is.EqualTo(HexCellPaint.RowBg.Deleted));
        }

        // ===== 字节背景 / 字色 =====

        [Test]
        public void Cell_Default_IsNoneBlack()
        {
            HexCellPaint.ResolveCell(MakeRow(), 0, 5, out var bg, out var fg);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.None));
            Assert.That(fg, Is.EqualTo(HexCellPaint.CellFg.Black));
        }

        [Test]
        public void Cell_Selected_IsSelectedBlack()
        {
            var row = MakeRow(); row.IsSelected = true;
            HexCellPaint.ResolveCell(row, 0, 5, out var bg, out var fg);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.Selected));
            Assert.That(fg, Is.EqualTo(HexCellPaint.CellFg.Black));
        }

        [Test]
        public void Cell_ConflictUnchosen_WhenRowChoiceMinus1()
        {
            var row = MakeRow(); row.ConflictBits[5] = true;
            HexCellPaint.ResolveCell(row, 0, 5, out var bg, out _);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.ConflictUnchosen));
            HexCellPaint.ResolveCell(row, 0, 4, out var bg4, out _);
            Assert.That(bg4, Is.EqualTo(HexCellPaint.CellBg.None));
        }

        [Test]
        public void Cell_ConflictChosen_WhenRowChoiceEqualsThisFile()
        {
            var row = MakeRow(); row.ConflictBits[5] = true; row.RowChoice = 0;
            // 选定文件 0 的冲突字节 → 珊瑚
            HexCellPaint.ResolveCell(row, 0, 5, out var bg0, out _);
            Assert.That(bg0, Is.EqualTo(HexCellPaint.CellBg.ConflictChosen));
            // 另一文件该冲突字节：段已选定（RowChoice=0≠-1），字节不再标冲突 → None（透出该文件行底黄绿）
            HexCellPaint.ResolveCell(row, 1, 5, out var bg1, out _);
            Assert.That(bg1, Is.EqualTo(HexCellPaint.CellBg.None));
        }

        [Test]
        public void Cell_RowChoiceEqualsF_AllBytesRed()
        {
            // 段选定 RowChoice==0：该文件该行所有有内容字节都红（不只冲突字节）
            var row = MakeRow(); row.RowChoice = 0;
            HexCellPaint.ResolveCell(row, 0, 5, out var bg, out _);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.ConflictChosen));
        }

        [Test]
        public void RowHasDiff_Conflict_InlineMissing_True_Same_False()
        {
            var r1 = MakeRow(); r1.HasConflict = true;
            Assert.That(HexCellPaint.RowHasDiff(r1), Is.True);                    // 值冲突 → 有差异
            var r2 = MakeRow(); r2.Cells[1][3] = null;
            Assert.That(HexCellPaint.RowHasDiff(r2), Is.True);                    // 行内缺失 → 有差异
            var r3 = MakeRow(); r3.Cells[1][3] = null; r3.IsMissingPerFile[1] = true;
            Assert.That(HexCellPaint.RowHasDiff(r3), Is.False);                   // 整行缺失 → 不算
            Assert.That(HexCellPaint.RowHasDiff(MakeRow()), Is.False);            // 两文件相同 → 无差异
        }

        [Test]
        public void Cell_Missing_OverridesSelected()
        {
            // 整行缺失且选中 → 缺失优先（灰），不是蓝底
            var row = MakeRow();
            row.Cells[0][3] = null;
            row.IsMissingPerFile[0] = true;
            row.IsSelected = true;
            HexCellPaint.ResolveCell(row, 0, 3, out var bg, out var fg);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.TransparentMissing));
            Assert.That(fg, Is.EqualTo(HexCellPaint.CellFg.Gray));
        }

        [Test]
        public void Cell_RowMissing_IsGray()
        {
            // 整行缺失（该文件该行全无数据）→ 灰
            var row = MakeRow();
            row.Cells[0][3] = null;
            row.IsMissingPerFile[0] = true;
            HexCellPaint.ResolveCell(row, 0, 3, out var bg, out var fg);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.TransparentMissing));
            Assert.That(fg, Is.EqualTo(HexCellPaint.CellFg.Gray));
        }

        [Test]
        public void Cell_InlineMissing_IsLightYellow()
        {
            // 行内部分缺失（该文件该行有部分数据，仅此字节空 = "写满vs没写满"的空余）→ 浅黄（差异）
            var row = MakeRow();
            row.Cells[0][3] = null; // IsMissingPerFile[0] 默认 false → 行内缺失
            HexCellPaint.ResolveCell(row, 0, 3, out var bg, out var fg);
            Assert.That(bg, Is.EqualTo(HexCellPaint.CellBg.ConflictUnchosen));
            Assert.That(fg, Is.EqualTo(HexCellPaint.CellFg.Black));
        }

        [Test]
        public void Cell_InlineDiff_BothSidesLightYellow()
        {
            // 一方写满、一方没写满（行内缺失）：缺失方与有内容方都标浅黄（差异一致，不再一边深黄）
            var row = MakeRow(); row.Cells[1][7] = null;
            HexCellPaint.ResolveCell(row, 0, 7, out var bg0, out _); // 文件0 有内容
            Assert.That(bg0, Is.EqualTo(HexCellPaint.CellBg.ConflictUnchosen));
            HexCellPaint.ResolveCell(row, 1, 7, out var bg1, out var fg1); // 文件1 缺失
            Assert.That(bg1, Is.EqualTo(HexCellPaint.CellBg.ConflictUnchosen));
            Assert.That(fg1, Is.EqualTo(HexCellPaint.CellFg.Black));
        }

        [Test]
        public void Cell_RowMissing_PresentSideIsNone()
        {
            // 整行缺失（一方完全没这段地址）：缺失方灰，有内容方 None（不染浅黄，避免大面积误染）
            var row = MakeRow();
            row.Cells[1][7] = null;
            row.IsMissingPerFile[1] = true; // 文件1 整行缺失
            HexCellPaint.ResolveCell(row, 0, 7, out var bg0, out _); // 文件0 有内容
            Assert.That(bg0, Is.EqualTo(HexCellPaint.CellBg.None));
            HexCellPaint.ResolveCell(row, 1, 7, out var bg1, out var fg1); // 文件1 缺失
            Assert.That(bg1, Is.EqualTo(HexCellPaint.CellBg.TransparentMissing));
            Assert.That(fg1, Is.EqualTo(HexCellPaint.CellFg.Gray));
        }
    }
}

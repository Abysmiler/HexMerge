using FTHexMerge.ViewModels;

namespace FTHexMerge.Views
{
    /// <summary>
    /// 比较视图的着色决策（纯逻辑，不引用 System.Windows.Media，便于单元测试）。
    /// 将"行背景 / 字节背景 / 字色"的判定从 WPF 触发器提取为确定性的纯函数，
    /// 自绘控件与单测共用同一套规则，避免实现与契约脱节。
    /// </summary>
    public static class HexCellPaint
    {
        /// <summary>行背景（整条，该文件该行）。</summary>
        public enum RowBg { White, Deleted, Conflict, Chosen }

        /// <summary>字节背景。</summary>
        public enum CellBg
        {
            None,               // 无字节底（透出行底）
            TransparentMissing, // 缺失：透明底 + 灰字
            Selected,           // 选中行字节：浅蓝
            ConflictUnchosen,   // 冲突且段未选：浅黄褐
            ConflictChosen      // 冲突且本文件被选定保留：珊瑚
        }

        /// <summary>字色。</summary>
        public enum CellFg { Black, Gray }

        /// <summary>
        /// 行背景：RowChoice==f→Chosen；否则 IsMissingPerFile[f]→Deleted；
        /// 否则 HasConflict→Conflict；否则 White。
        /// </summary>
        public static RowBg ResolveRowBg(CompareViewModel.Row row, int f)
        {
            if (row.RowChoice == f) return RowBg.Chosen;
            if (row.IsMissingPerFile != null && f < row.IsMissingPerFile.Length && row.IsMissingPerFile[f])
                return RowBg.Deleted;
            if (row.HasConflict) return RowBg.Conflict;
            return RowBg.White;
        }

        /// <summary>
        /// 字节背景 + 字色，优先级（高→低）：
        /// 缺失(Cells[f][k]==null)→(TransparentMissing,Gray)；
        /// 否则 IsSelected→(Selected,Black)；
        /// 否则 ConflictBits[k]&&RowChoice==f→(ConflictChosen,Black)；
        /// 否则 ConflictBits[k]&&RowChoice==-1→(ConflictUnchosen,Black)；
        /// 否则 (None,Black)。
        /// </summary>
        public static void ResolveCell(CompareViewModel.Row row, int f, int k, out CellBg bg, out CellFg fg)
        {
            // 缺失优先于一切（含选中、冲突）：
            //   整行缺失（该文件该行全无数据）→ 灰；
            //   行内部分缺失（该行有部分数据、仅此字节空，即"一方写满一方没写满"的空余）→ 浅黄（差异）
            if (row.Cells == null || row.Cells[f] == null || row.Cells[f][k] == null)
            {
                bool rowMissing = row.IsMissingPerFile != null && f < row.IsMissingPerFile.Length && row.IsMissingPerFile[f];
                if (rowMissing) { bg = CellBg.TransparentMissing; fg = CellFg.Gray; }
                else { bg = CellBg.ConflictUnchosen; fg = CellFg.Black; }
                return;
            }
            if (row.IsSelected)
            {
                bg = CellBg.Selected;
                fg = CellFg.Black;
                return;
            }
            // 段选定：RowChoice==f → 该文件该行所有有内容字节红（不只冲突字节）
            if (row.RowChoice == f)
            {
                bg = CellBg.ConflictChosen;
                fg = CellFg.Black;
                return;
            }
            bool conflict = row.ConflictBits != null && row.ConflictBits[k];
            if (conflict && row.RowChoice == -1)
            {
                bg = CellBg.ConflictUnchosen;
                fg = CellFg.Black;
                return;
            }
            // 行内差异位：对方在该行"行内缺失"此字节（写满 vs 没写满）→ 本字节（有内容）也标浅黄，
            // 与缺失方保持一致，不再透出冲突行底（深黄）
            if (IsInlineDiffByte(row, k))
            {
                bg = CellBg.ConflictUnchosen;
                fg = CellFg.Black;
                return;
            }
            bg = CellBg.None;
            fg = CellFg.Black;
        }

        /// <summary>该字节位是否为"行内差异"：存在某文件该行有数据(IsMissingPerFile=false)但此字节为空
        /// （即"一方写满、一方没写满"的空余位）。整行缺失(IsMissingPerFile=true)不算，避免大面积误染。</summary>
        private static bool IsInlineDiffByte(CompareViewModel.Row row, int k)
        {
            if (row.Cells == null) return false;
            for (int ff = 0; ff < row.Cells.Length; ff++)
            {
                bool rowMissing = row.IsMissingPerFile != null && ff < row.IsMissingPerFile.Length && row.IsMissingPerFile[ff];
                if (rowMissing) continue; // 整行缺失不算行内差异
                if (row.Cells[ff] == null || k >= row.Cells[ff].Length || row.Cells[ff][k] == null)
                    return true;
            }
            return false;
        }

        /// <summary>该行是否有差异（供地址槽染色）：值冲突 或 行内缺失（一有一无）。整行缺失不算。</summary>
        public static bool RowHasDiff(CompareViewModel.Row row)
        {
            if (row.HasConflict) return true;
            if (row.Cells == null) return false;
            int fc = row.Cells.Length;
            for (int f = 0; f < fc; f++)
            {
                bool rowMissing = row.IsMissingPerFile != null && f < row.IsMissingPerFile.Length && row.IsMissingPerFile[f];
                if (rowMissing) continue; // 整行缺失不算
                if (row.Cells[f] == null) continue;
                for (int k = 0; k < 16; k++)
                    if (row.Cells[f][k] == null) return true; // 行内缺失
            }
            return false;
        }
    }
}

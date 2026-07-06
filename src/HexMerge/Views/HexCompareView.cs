using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FTHexMerge.ViewModels;

namespace FTHexMerge.Views
{
    /// <summary>
    /// 比较视图自绘控件：用 OnRender 直接画"地址 + 每文件 16 字节"网格，
    /// 只画视口内可见的二三十行，零绑定/零触发器/零容器回收——
    /// 彻底消除 DataGrid 多列字节网格在滚动时的卡顿。
    /// 着色决策复用 <see cref="HexCellPaint"/>（纯函数，已单测）。
    /// 所有文件画在同一滚动面上，多列同步滚动天然成立。
    /// </summary>
    public class HexCompareView : FrameworkElement
    {
        // ===== 布局常量 =====
        private const double LineHeight = 24;     // 行高
        private const double HeaderLineH = 18;    // 表头文本行高
        private const double SegLineH = 13;       // 段信息行间距（更紧凑）
        private double _headerHeight = 60;        // 表头总高（按各文件段数动态计算）
        private const double GapWidth = 16;       // 文件块之间的间隔
        private const double FontSize = 14;

        // ===== 配色（与 HexCellPaint 枚举对应；RGB 同 WinMerge Default.ini）=====
        // 配色统一到 HexPalette（见文件末），此处别名仅为缩短引用
        private static readonly Brush BgWhite = HexPalette.White;
        private static readonly Brush BgDeleted = HexPalette.Deleted;
        private static readonly Brush BgConflict = HexPalette.Conflict;
        private static readonly Brush BgChosen = HexPalette.Chosen;
        private static readonly Brush CellSelected = HexPalette.CellSelected;
        private static readonly Brush CellConflictUnchosen = HexPalette.ConflictUnchosen;
        private static readonly Brush TextBlack = HexPalette.TextBlack;
        private static readonly Brush TextGray = HexPalette.TextGray;
        private static readonly Brush TextHeader = HexPalette.TextHeader;
        private static readonly Brush Separator = HexPalette.Separator;

        // ===== 状态 =====
        private CompareViewModel _vm;
        private ScrollBar _scrollBar;
        private bool _syncing;             // ScrollBar 双向同步防环
        private double _verticalOffset;    // 像素级垂直滚动偏移
        private double _viewportW, _viewportH;
        private double _scrollableH;       // = max(0, 总高 - 视口高)
        private double _addrWidth = 84;          // 地址槽宽（按字号测量，容纳 "0xXXXXXXXX"）
        private double _byteMinWidth = 20;       // 字节格最小宽（按字号测量，容纳 "FF"）
        private double _byteWidth = 26;          // 动态字节宽（ArrangeOverride 重算）
        private double _blockWidth = 84 + 16 * 26; // 单文件块宽 = 地址槽 + 16 字节
        private int _selectedRow = -1;     // 当前选中行（-1=无）
        private double _lastDpi;           // 上次建缓存用的 DPI（变化则重建）

        // ===== 文本缓存（按 DPI 建，绘制时复用，避免每帧分配 FormattedText）=====
        private readonly Typeface _typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private FormattedText[] _byteText;        // 0..255 黑色
        private FormattedText _missingText;       // "--" 灰
        private FormattedText[] _headerByteText;  // "0".."F" 表头
        private FormattedText _addrHeader;        // 表头"地址"标签
        private FormattedText[] _nameText;        // 各文件名
        private string[][] _segLines;             // 各文件的段信息文本（"Block N Start/End/Length"）
        private FormattedText[][] _segText;       // 段信息 FormattedText 预建缓存（size 11），避免每帧分配

        public HexCompareView()
        {
            ClipToBounds = true;
            Focusable = true;
        }

        /// <summary>绑定视图模型并首次刷新。</summary>
        public void SetViewModel(CompareViewModel vm)
        {
            _vm = vm;
            _selectedRow = -1;
            _verticalOffset = 0;
            InvalidateVisual();
        }

        /// <summary>失效文本缓存，强制下次渲染重建。
        /// 后台加载时首次渲染（数据空）会建空缓存；数据就绪后须调此方法，
        /// 否则 EnsureTextCache 的"仅按 DPI 重建"守卫会让段信息/文件名一直画空。</summary>
        public void InvalidateTextCache()
        {
            _byteText = null;
        }

        /// <summary>滚动位置或视口尺寸变化时触发（供定位栏更新视口指示器）。</summary>
        public event EventHandler Scrolled;
        private void RaiseScrolled() { Scrolled?.Invoke(this, EventArgs.Empty); }

        /// <summary>挂接外部 ScrollBar（由 CompareWindow 创建并放入布局）。</summary>
        public void AttachScrollBar(ScrollBar sb)
        {
            _scrollBar = sb;
            sb.Minimum = 0;
            sb.Orientation = Orientation.Vertical;
            sb.Scroll += (s, e) =>
            {
                if (_syncing) return;
                SetVerticalOffset(e.NewValue, fromScrollBar: true);
            };
            UpdateScrollBarGeometry();
        }

        // ===== 布局 =====

        protected override Size MeasureOverride(Size availableSize)
        {
            // 无特殊尺寸需求：让父级（Grid 1* 列）分配宽度，Arrange 时拿到最终尺寸。
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _viewportW = finalSize.Width;
            _viewportH = finalSize.Height;
            EnsureTextCache(SafeDpi()); // 字号/DPI 变化或首排时按测量槽宽重算

            // 每文件占等宽（Star 语义，同原 DataGrid）：字节宽 = (区域宽 - 地址槽) / 16，下限为容纳 "FF" 的最小宽。
            // 默认窗口下 ≈28（同原 DataGrid 字节列），全屏时自动拉伸铺满，不留右侧空白。
            int fc = _vm != null ? _vm.FileCount : 0;
            if (fc > 0 && _viewportW > 0)
            {
                double regionW = (_viewportW - (fc - 1) * GapWidth) / fc;
                _byteWidth = Math.Max(_byteMinWidth, (regionW - _addrWidth) / 16);
                _blockWidth = _addrWidth + _byteWidth * 16;
            }

            double totalH = _headerHeight + RowCount * LineHeight;
            _scrollableH = Math.Max(0, totalH - _viewportH);
            if (_verticalOffset > _scrollableH) _verticalOffset = _scrollableH;

            UpdateScrollBarGeometry();
            InvalidateVisual();
            RaiseScrolled();
            return finalSize;
        }

        private int RowCount { get { return _vm != null && _vm.Rows != null ? _vm.Rows.Count : 0; } }

        /// <summary>文件块 f 的起始 x（块间留 GapWidth 间隔）。</summary>
        private double BlockX(int f) { return f * (_blockWidth + GapWidth); }

        /// <summary>当前元素 DPI（pixelsPerDip）；未挂入可视树时兜底 1.0。</summary>
        private double SafeDpi()
        {
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            return dpi <= 0 ? 1.0 : dpi;
        }

        // ===== 滚动 =====

        /// <summary>像素级垂直偏移。外部（滚轮/跳转/定位栏）改它；ScrollBar 拖动经 Scroll 事件改它。</summary>
        public double VerticalOffset
        {
            get { return _verticalOffset; }
            set { SetVerticalOffset(value, fromScrollBar: false); }
        }

        private void SetVerticalOffset(double value, bool fromScrollBar)
        {
            double clamped = value < 0 ? 0 : (value > _scrollableH ? _scrollableH : value);
            if (clamped == _verticalOffset) return;
            _verticalOffset = clamped;
            InvalidateVisual();
            if (!fromScrollBar) UpdateScrollBarValue();
            RaiseScrolled();
        }

        private void UpdateScrollBarGeometry()
        {
            if (_scrollBar == null) return;
            _syncing = true;
            _scrollBar.Maximum = _scrollableH;
            _scrollBar.ViewportSize = Math.Max(1, _viewportH);
            _scrollBar.SmallChange = LineHeight;
            _scrollBar.LargeChange = Math.Max(LineHeight, _viewportH);
            _scrollBar.Value = _verticalOffset;
            _syncing = false;
        }

        private void UpdateScrollBarValue()
        {
            if (_scrollBar == null) return;
            _syncing = true;
            _scrollBar.Value = _verticalOffset;
            _syncing = false;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            double notches = e.Delta / 120.0;
            VerticalOffset = _verticalOffset - notches * 3 * LineHeight; // 向上滚→偏移减小
            e.Handled = true;
        }

        /// <summary>方向键 ↑/↓ 移动选中行（带视口跟随）。需先聚焦本控件（鼠标点击即聚焦）。</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Handled) return;
            if (_vm == null || RowCount == 0) return;

            int cur = _selectedRow >= 0 ? _selectedRow : CurrentRowIndex;
            int target = e.Key == Key.Down ? cur + 1 : (e.Key == Key.Up ? cur - 1 : cur);
            if (target == cur) return;                                          // 非方向键：交还默认处理
            if (target < 0 || target >= RowCount) { e.Handled = true; return; } // 到边界：吃掉，不翻页

            SelectRow(target);
            EnsureRowVisible(target);
            e.Handled = true;
        }

        /// <summary>确保指定行在可见区，不在则滚动到位（方向键导航用）。</summary>
        private void EnsureRowVisible(int idx)
        {
            double viewH = _viewportH - _headerHeight; // 可见内容高（扣除固定表头）
            if (viewH <= 0) return;
            double rowTop = idx * LineHeight;
            double rowBot = rowTop + LineHeight;
            if (rowTop < _verticalOffset)
                VerticalOffset = rowTop;              // 行在视口上方：滚到顶
            else if (rowBot > _verticalOffset + viewH)
                VerticalOffset = rowBot - viewH;      // 行在视口下方：滚到底
        }

        // ===== 文本缓存（按 DPI）=====

        private void EnsureTextCache(double dpi)
        {
            if (dpi <= 0) dpi = 1.0;
            if (_byteText != null && dpi == _lastDpi) return;
            _lastDpi = dpi;

            _byteText = new FormattedText[256];
            for (int i = 0; i < 256; i++) _byteText[i] = MkText(i.ToString("X2"), TextBlack, dpi);
            _missingText = MkText("--", TextGray, dpi);
            _headerByteText = new FormattedText[16];
            for (int i = 0; i < 16; i++) _headerByteText[i] = MkText(i.ToString("X"), TextHeader, dpi);
            _addrHeader = MkText("地址", TextHeader, dpi);

            // 按字号测量槽宽：地址槽容纳 "0xFFFFFFFF"，字节格容纳 "FF"，避免文字溢出色块
            _addrWidth = MkText("0xFFFFFFFF", TextBlack, dpi).Width + 8;
            _byteMinWidth = _byteText[255].Width + 4;

            int fc = _vm != null ? _vm.FileCount : 0;
            _nameText = new FormattedText[Math.Max(1, fc)];
            _segLines = new string[Math.Max(1, fc)][];
            _segText = new FormattedText[Math.Max(1, fc)][];
            int maxSeg = 0;
            for (int f = 0; f < fc; f++)
            {
                _nameText[f] = MkText(_vm.FileNames[f] ?? "", TextHeader, dpi);
                var segs = _vm.GetFileSegments(f);
                _segLines[f] = new string[segs.Count];
                _segText[f] = new FormattedText[segs.Count];
                for (int s = 0; s < segs.Count; s++)
                {
                    var seg = segs[s];
                    _segLines[f][s] = string.Format("Block {0}  Start at 0x{1:X}  Ends at 0x{2:X}  (Length: 0x{3:X}={4})",
                        s, seg.Start, seg.End, seg.Length, seg.Length);
                    _segText[f][s] = MkTextAt(_segLines[f][s], TextHeader, dpi, 11);
                }
                if (segs.Count > maxSeg) maxSeg = segs.Count;
            }
            // 表头总高 = 文件名行 + 各段行(按最多段数, 行间距 SegLineH) + 字节索引行
            _headerHeight = 8 + HeaderLineH + maxSeg * SegLineH + HeaderLineH + 6;
        }

        private FormattedText MkText(string text, Brush brush, double dpi)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, FontSize, brush, dpi);
        }

        /// <summary>指定字号的 FormattedText（段信息用较小字号，避免溢出窄块）。</summary>
        private FormattedText MkTextAt(string text, Brush brush, double dpi, double size)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, size, brush, dpi);
        }

        // ===== 绘制 =====

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            EnsureTextCache(SafeDpi());

            double w = _viewportW, h = _viewportH;
            dc.DrawRectangle(BgWhite, null, new Rect(0, 0, w, h));

            if (_vm == null || _vm.Rows == null || _vm.FileCount == 0) return;

            DrawHeader(dc);

            int n = _vm.Rows.Count;
            if (n == 0) return;

            int startRow = (int)(_verticalOffset / LineHeight);
            if (startRow < 0) startRow = 0;
            int endRow = (int)((_verticalOffset + _viewportH) / LineHeight) + 1;
            if (endRow > n - 1) endRow = n - 1;

            // 行内容裁剪到表头下方：像素级滚动时半行不会盖住固定的表头（0..F / 文件名）
            dc.PushClip(new RectangleGeometry(new Rect(0, _headerHeight, _viewportW, _viewportH - _headerHeight)));
            for (int i = startRow; i <= endRow; i++)
                DrawRow(dc, _vm.Rows[i], i);
            dc.Pop();
        }

        private void DrawHeader(DrawingContext dc)
        {
            int fc = _vm.FileCount;
            double yName = 4;
            double ySeg = 4 + HeaderLineH;          // 段信息起始（文件名下方）
            double yByte = _headerHeight - HeaderLineH - 4; // 字节索引行贴底
            for (int f = 0; f < fc; f++)
            {
                double bx = BlockX(f);
                // 文件名
                if (_nameText != null && f < _nameText.Length)
                    dc.DrawText(_nameText[f], new Point(bx + 2, yName));
                // 各段信息（Block N Start/End/Length）
                if (_segText != null && f < _segText.Length && _segText[f] != null)
                {
                    FormattedText[] fts = _segText[f];
                    for (int s = 0; s < fts.Length; s++)
                    {
                        if (fts[s] != null)
                            dc.DrawText(fts[s], new Point(bx + 2, ySeg + s * SegLineH));
                    }
                }
                // "地址" 标签 + 0..F 字节索引（贴底）
                if (_addrHeader != null)
                    dc.DrawText(_addrHeader, new Point(bx + (_addrWidth - _addrHeader.Width) / 2, yByte));
                for (int k = 0; k < 16; k++)
                {
                    FormattedText ht = _headerByteText[k];
                    dc.DrawText(ht, new Point(bx + _addrWidth + k * _byteWidth + (_byteWidth - ht.Width) / 2, yByte));
                }
                // 文件块之间的分隔线（贯穿视口）
                if (f < fc - 1)
                    dc.DrawRectangle(Separator, null, new Rect(bx + _blockWidth + GapWidth / 2, 0, 1, _viewportH));
            }
            // 表头分隔线
            dc.DrawRectangle(TextHeader, null, new Rect(0, _headerHeight - 1, _viewportW, 1));
        }

        private void DrawRow(DrawingContext dc, CompareViewModel.Row row, int i)
        {
            double y = _headerHeight + i * LineHeight - _verticalOffset;
            int fc = _vm.FileCount;

            // 地址文本（同行的所有文件地址相同，建一次复用）
            FormattedText addr = MkText(row.AddressText, TextBlack, _lastDpi);
            double addrY = y + (LineHeight - addr.Height) / 2;

            bool rowHasDiff = HexCellPaint.RowHasDiff(row); // 行级差异（地址槽染色用）

            for (int f = 0; f < fc; f++)
            {
                double bx = BlockX(f);

                // 行背景：只白（有数据）/ 灰（整行缺失）。
                bool rowMissing = row.IsMissingPerFile != null && f < row.IsMissingPerFile.Length && row.IsMissingPerFile[f];
                dc.DrawRectangle(rowMissing ? BgDeleted : BgWhite, null,
                    new Rect(bx, y, _blockWidth, LineHeight));

                // 地址槽染色：段选定(RowChoice==f)→红；否则差异行且非灰块→深黄；灰块(整行缺失)不染
                if (row.RowChoice == f)
                    dc.DrawRectangle(BgChosen, null, new Rect(bx, y, _addrWidth, LineHeight));
                else if (rowHasDiff && !rowMissing)
                    dc.DrawRectangle(CellConflictUnchosen, null, new Rect(bx, y, _addrWidth, LineHeight));

                // 地址（黑字，居中于地址槽）
                dc.DrawText(addr, new Point(bx + (_addrWidth - addr.Width) / 2, addrY));

                // 逐字节
                for (int k = 0; k < 16; k++)
                {
                    HexCellPaint.ResolveCell(row, f, k, out HexCellPaint.CellBg cbg, out HexCellPaint.CellFg cfg);
                    double cx = bx + _addrWidth + k * _byteWidth;
                    Brush cellBg = CellBgBrush(cbg);
                    if (cellBg != null)
                        dc.DrawRectangle(cellBg, null, new Rect(cx, y, _byteWidth, LineHeight));

                    // 文字：有值→黑字节；缺失（无论字色，含行内缺失）→灰"--"。按数据有无选择，避免对 null 取 .Value 崩溃
                    byte? cellVal = row.Cells[f][k];
                    FormattedText ft = cellVal.HasValue ? _byteText[cellVal.Value] : _missingText;
                    dc.DrawText(ft, new Point(cx + (_byteWidth - ft.Width) / 2, y + (LineHeight - ft.Height) / 2));
                }
            }
        }

        /// <summary>字节背景画笔；None/TransparentMissing 返回 null（不画矩形，透出行底）。</summary>
        private static Brush CellBgBrush(HexCellPaint.CellBg bg)
        {
            switch (bg)
            {
                case HexCellPaint.CellBg.Selected: return CellSelected;
                case HexCellPaint.CellBg.ConflictUnchosen: return CellConflictUnchosen;
                case HexCellPaint.CellBg.ConflictChosen: return BgChosen;
                default: return null;
            }
        }

        // ===== 命中测试 / 交互 =====

        private int HitTestRow(Point p)
        {
            if (p.Y < _headerHeight) return -1; // 表头不算行
            int idx = (int)((p.Y - _headerHeight + _verticalOffset) / LineHeight);
            int n = RowCount;
            if (idx < 0) idx = 0;
            if (idx > n - 1) idx = n - 1;
            return idx;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            Focus();
            int idx = HitTestRow(e.GetPosition(this));
            if (idx >= 0) SelectRow(idx);
            e.Handled = true;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            int idx = HitTestRow(e.GetPosition(this));
            if (idx < 0) return;
            CompareViewModel.Row row = _vm.Rows[idx];
            if (row.SegmentId < 0) return; // 非冲突段不弹菜单

            SelectRow(idx);
            ContextMenu menu = BuildSegmentMenu(row);
            menu.PlacementTarget = this;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            e.Handled = true;
        }

        /// <summary>构建段级右键菜单：列出段内参与文件（保留 X）+ 恢复按优先级。</summary>
        private ContextMenu BuildSegmentMenu(CompareViewModel.Row row)
        {
            ContextMenu menu = new ContextMenu();
            List<int> files = _vm.GetSegmentFiles(row);
            int curChoice = row.RowChoice;
            foreach (int f in files)
            {
                int fileIdx = f;
                MenuItem item = new MenuItem();
                item.Header = string.Format("保留 {0}（{1}）", fileIdx + 1, _vm.FileNames[fileIdx]);
                item.IsChecked = (curChoice == fileIdx);
                item.Click += (s, args) => { _vm.SetSegmentChoice(row, fileIdx); InvalidateVisual(); };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            MenuItem restore = new MenuItem();
            restore.Header = "恢复按优先级";
            restore.IsChecked = (curChoice < 0);
            restore.Click += (s, args) => { _vm.SetSegmentChoice(row, -1); InvalidateVisual(); };
            menu.Items.Add(restore);
            return menu;
        }

        // ===== 选择 / 跳转（供 CompareWindow 调用）=====

        /// <summary>当前光标行：优先选中行，否则首可见行。</summary>
        public int CurrentRowIndex
        {
            get
            {
                if (_selectedRow >= 0 && _selectedRow < RowCount) return _selectedRow;
                int r = (int)(_verticalOffset / LineHeight);
                if (r < 0) r = 0;
                if (r > RowCount - 1) r = RowCount - 1;
                return r;
            }
        }

        /// <summary>选中指定行（全局单选，跨所有文件列同步高亮）。</summary>
        public void SelectRow(int idx)
        {
            if (RowCount == 0) return;
            if (idx < 0) idx = 0;
            if (idx > RowCount - 1) idx = RowCount - 1;
            if (_selectedRow >= 0 && _selectedRow < _vm.Rows.Count)
                _vm.Rows[_selectedRow].IsSelected = false;
            _selectedRow = idx;
            _vm.Rows[idx].IsSelected = true;
            _vm.SyncCurrentConflictToRow(idx); // 左键选中冲突行 → 浮窗"当前"同步
            InvalidateVisual();
        }

        /// <summary>跳到指定行：选中并滚动到视口中部。</summary>
        public void GotoRow(int idx)
        {
            SelectRow(idx);
            double target = idx * LineHeight + LineHeight / 2 - _viewportH / 2;
            VerticalOffset = target;
        }

        /// <summary>按比例(0..1)滚动到文档位置（定位栏拖动用，类似滚动条）。</summary>
        public void ScrollToFraction(double ratio)
        {
            if (ratio < 0) ratio = 0;
            if (ratio > 1) ratio = 1;
            VerticalOffset = ratio * _scrollableH;
        }

        /// <summary>定位栏视口指示器：返回当前可见区在文档中的 [topFrac, heightFrac]（0..1）。</summary>
        public void GetViewportFraction(out double topFrac, out double heightFrac)
        {
            int n = RowCount;
            if (n <= 0 || _viewportH <= 0) { topFrac = 0; heightFrac = 1; return; }
            double totalContent = n * LineHeight;
            topFrac = _verticalOffset / totalContent;
            heightFrac = _viewportH / totalContent;
            if (topFrac < 0) topFrac = 0;
            if (topFrac > 1) topFrac = 1;
            if (heightFrac > 1) heightFrac = 1;
        }
    }

    /// <summary>比较视图统一配色（RGB 同 WinMerge Default.ini）；HexCompareView 与 LocationBar 共用，改色一处生效。</summary>
    internal static class HexPalette
    {
        public static readonly Brush White = Freeze(Brushes.White);
        public static readonly Brush Deleted = Freeze(new SolidColorBrush(Color.FromRgb(192, 192, 192)));   // #C0C0C0 缺失/删除
        public static readonly Brush Conflict = Freeze(new SolidColorBrush(Color.FromRgb(239, 203, 5)));     // #EFCB05 冲突
        public static readonly Brush Chosen = Freeze(new SolidColorBrush(Color.FromRgb(239, 119, 116)));     // #EF7774 段选定
        public static readonly Brush CellSelected = Freeze(new SolidColorBrush(Color.FromRgb(74, 144, 217)));   // 深蓝 #4A90D9 选中行
        public static readonly Brush ConflictUnchosen = Freeze(new SolidColorBrush(Color.FromRgb(255, 192, 0)));   // 深黄 #FFC000 差异(冲突未选/行内缺失/行内差异)
        public static readonly Brush TextBlack = Freeze(Brushes.Black);
        public static readonly Brush TextGray = Freeze(Brushes.Gray);
        public static readonly Brush TextHeader = Freeze(new SolidColorBrush(Color.FromRgb(80, 80, 80)));
        public static readonly Brush Separator = Freeze(new SolidColorBrush(Color.FromRgb(200, 200, 200)));

        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }
}

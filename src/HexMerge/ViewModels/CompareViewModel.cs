using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexMerge.Core;
using HexMerge.Views;
using HexMerge.Models;
using Microsoft.Win32;

namespace HexMerge.ViewModels
{
    /// <summary>
    /// 比较视图的视图模型：WinMerge 风格地址轴（每行地址 + 16 字节）。
    /// 列数随文件数变化（2 或 3）；冲突高亮、缺失标灰。
    /// </summary>
    public class CompareViewModel : ObservableObject
    {
        /// <summary>一行：地址 + 每个文件该行的 16 字节（缺失则为 null，用于标灰）。</summary>
        public class Row : ObservableObject
        {
            public uint Address { get; set; }
            /// <summary>每文件 16 字节；null 表示该文件此地址无数据（标灰）。</summary>
            public byte?[][] Cells { get; set; }
            /// <summary>该行是否有冲突（影响高亮）。</summary>
            public bool HasConflict { get; set; }
            /// <summary>该行 16 个字节位中哪些是冲突位（字节级高亮），长度=16。</summary>
            public bool[] ConflictBits { get; set; }

            /// <summary>地址显示文本（0xXXXXXXXX）。</summary>
            public string AddressText { get { return "0x" + Address.ToString("X8"); } }

            /// <summary>每文件该行的 16 字节文本（缺失字节显示 --）。</summary>
            public string[] HexPerFile { get; set; }

            /// <summary>每文件该行是否全部缺失（用于标灰）。</summary>
            public bool[] IsMissingPerFile { get; set; }

            private int _rowChoice = -1;
            /// <summary>该行手动选定的保留文件索引（-1=按优先级）。改选后触发高亮刷新。</summary>
            public int RowChoice { get { return _rowChoice; } set { SetProperty(ref _rowChoice, value); } }

            private bool _isSelected;
            /// <summary>是否被选中（字节区选中蓝绑此，避免 FindAncestor 开销）。</summary>
            public bool IsSelected { get { return _isSelected; } set { SetProperty(ref _isSelected, value); } }

            /// <summary>所属冲突段 ID（连续冲突行同 ID；-1=非冲突行）。用于右键段级选择。</summary>
            public int SegmentId { get; set; }
        }

        private string _statusMessage = "";
        public string StatusMessage { get { return _statusMessage; } private set { SetProperty(ref _statusMessage, value); } }

        private int _fileCount;
        /// <summary>参与比较的文件数（决定显示几列）。</summary>
        public int FileCount { get { return _fileCount; } private set { SetProperty(ref _fileCount, value); } }

        /// <summary>各文件显示名。</summary>
        public ObservableCollection<string> FileNames { get; private set; }

        /// <summary>按地址升序的行集合（供 DataGrid 绑定）。</summary>
        public ObservableCollection<Row> Rows { get; private set; }

        /// <summary>优先级（文件索引顺序，靠前优先）。默认 0,1,2。</summary>
        private int[] _priority;
        public int[] Priority { get { return _priority; } }

        /// <summary>用户逐块改选：地址 -> 选定保留的文件索引（覆盖优先级）。</summary>
        private readonly Dictionary<uint, int> _choices = new Dictionary<uint, int>();

        private int _prioritySelection = 0;
        /// <summary>优先级下拉选择（0=文件1优先,1=文件2优先,...）。</summary>
        public int PrioritySelection
        {
            get { return _prioritySelection; }
            set { SetProperty(ref _prioritySelection, value); RebuildPriority(); UpdateCurrentPriorityName(); }
        }

        // ===== 浮窗（冲突导航）用属性 =====
        private int _conflictSegmentCount;
        /// <summary>冲突段总数。</summary>
        public int ConflictSegmentCount { get { return _conflictSegmentCount; } private set { SetProperty(ref _conflictSegmentCount, value); UpdateConflictInfoText(); } }

        private int _currentConflictIndex; // 1-based，0=未定位
        /// <summary>当前冲突段序号（1-based，0=未导航定位）。</summary>
        public int CurrentConflictIndex { get { return _currentConflictIndex; } private set { SetProperty(ref _currentConflictIndex, value); UpdateConflictInfoText(); } }

        private string _conflictInfoText = "无冲突";
        /// <summary>浮窗显示的冲突计数文本，如"冲突 3/10"或"无冲突"。</summary>
        public string ConflictInfoText { get { return _conflictInfoText; } private set { SetProperty(ref _conflictInfoText, value); } }

        private string _currentPriorityName = "";
        /// <summary>浮窗显示的当前优先级，如"文件1优先（BOOT.hex）"。</summary>
        public string CurrentPriorityName { get { return _currentPriorityName; } private set { SetProperty(ref _currentPriorityName, value); } }

        private void UpdateConflictInfoText()
        {
            ConflictInfoText = _conflictSegmentCount == 0 ? "无冲突"
                : string.Format("{0} / {1}", _currentConflictIndex == 0 ? "-" : _currentConflictIndex.ToString(), _conflictSegmentCount);
        }

        private void UpdateCurrentPriorityName()
        {
            CurrentPriorityName = (_prioritySelection >= 0 && _prioritySelection < PriorityOptions.Count) ? PriorityOptions[_prioritySelection] : "";
        }

        /// <summary>把"当前冲突"同步到指定行所在的冲突段（左键选中/导航跳转后调用）。非冲突行不改变当前序号。</summary>
        public void SyncCurrentConflictToRow(int rowIdx)
        {
            if (rowIdx < 0 || rowIdx >= Rows.Count) return;
            int seg = Rows[rowIdx].SegmentId;
            if (seg >= 0) CurrentConflictIndex = seg + 1;
        }

        public ObservableCollection<string> PriorityOptions { get; private set; }

        /// <summary>空隙填充方式：0=不填，1=0xFF，2=0x00。</summary>
        public enum GapFill { None, FillFF, Fill00 }

        /// <summary>空隙填充阈值：单段间隙超过此字节数即视为"跨大区"，不填充。
        /// 设为 1MB 的原因（两道硬约束，不可随意抬高）：
        /// 1) Intel HEX 无游程压缩，每字节占 ~2.75 字符文本；填一个 14MB 间隙会让输出膨胀到 40MB+；
        /// 2) 数据模型是"逐字节字典"（SortedDictionary&lt;uint,byte&gt;），千万级填充项会撑爆 32 位进程内存
        ///    （合并时 OOM、再次打开解析时也 OOM）。flash 空区烧录默认即未编程态(≈0xFF)，大间隙无需实填。
        /// ComputePreview 统计 与 ApplyGapFill 写入 共用此常量，保证口径一致。</summary>
        public const uint GapFillMaxBytes = 1u << 20;

        /// <summary>输出段信息（一段连续地址区间）。</summary>
        public class MergeSegmentInfo { public uint Start; public uint End; public long Length; }

        /// <summary>被跳过的间隙：超过 <see cref="GapFillMaxBytes"/> 阈值、未填充的地址区间。</summary>
        public class SkippedGapInfo { public uint Start; public uint End; public long Length; }

        /// <summary>合并预览摘要（合并前供用户确认）。</summary>
        public class MergePreview
        {
            public int SegmentCount;   // 输出段数
            public long TotalBytes;    // 合并字节（填充前）
            public uint MinAddress;
            public uint MaxAddress;
            public int GapCount;       // 间隙数
            public long GapBytes;      // 间隙总字节
            public long FillBytes;     // 当前填充方式下会填的字节（间隙 ≤ 阈值）
            public long FinalBytes;    // 填充后总字节
            public List<MergeSegmentInfo> Segments = new List<MergeSegmentInfo>(); // 输出段列表
            public List<SkippedGapInfo> SkippedGaps = new List<SkippedGapInfo>();  // 超阈值、未填充的间隙
            public long SkippedGapBytes; // 被跳过间隙的总字节
            public List<SkippedGapInfo> DFlashGaps = new List<SkippedGapInfo>();  // 通往 DFlash 的跨区间隙（默认忽略）
            public long DFlashGapBytes;  // 到 DFlash 间隙的总字节
        }

        public ObservableCollection<string> FillOptions { get; private set; }

        private int _fillSelection = 1; // 默认 0xFF（FillFF）
        /// <summary>空隙填充下拉选择（0/1/2）。</summary>
        public int FillSelection
        {
            get { return _fillSelection; }
            set { SetProperty(ref _fillSelection, value); }
        }

        public ICommand MergeCommand { get; private set; }

        /// <summary>合并前预览确认回调（由 View 设置，返回 true=确认合并）。</summary>
        public Func<MergePreview, bool> ConfirmMergePreview;

        private List<AddressUnit> _units;
        private MemoryImage[] _images;

        /// <summary>地址 -> AddressUnit 索引（BuildRows 填充），供 FindUnit/CycleChoice O(1) 查找，避免线性遍历。</summary>
        private readonly Dictionary<uint, AddressUnit> _unitByAddr = new Dictionary<uint, AddressUnit>();

        /// <summary>DFlash 起点 = 所有 DAT 文件基地址的最小值（DAT 即 DFlash 数据，其基地址即 DFlash 区起点）。
        /// 无 DAT 文件时为 uint.MaxValue（哨兵：不识别 DFlash 间隙，间隙仍按 1MB 阈值处理）。</summary>
        private uint _dflashStart = uint.MaxValue;

        public CompareViewModel()
        {
            FileNames = new ObservableCollection<string>();
            Rows = new ObservableCollection<Row>();
            PriorityOptions = new ObservableCollection<string>();
            FillOptions = new ObservableCollection<string> { "不填充", "0xFF", "0x00" };
            MergeCommand = new RelayCommand(Merge);
        }

        /// <summary>数据加载完成（后台解析后）触发，供 View 做依赖数据的 UI 构建（如 BuildMenus）。</summary>
        public event Action Loaded;

        /// <summary>加载多个文件并构建比较视图。images 为已解析结果（复用，避免重复解析）；
        /// 比对在后台线程进行（防大文件 UI 假死），完成后切回 UI 线程构建并触发 <see cref="Loaded"/>。</summary>
        public void Load(string[] paths, uint[] baseAddrs, MemoryImage[] images)
        {
            Rows.Clear();
            FileNames.Clear();
            StatusMessage = "正在比对…";

            // 拷贝参数，避免后台线程读到被调用方修改的数组
            string[] pathsArg = paths;
            uint[] baseArg = baseAddrs;
            MemoryImage[] imagesArg = images;

            Task.Run(() =>
            {
                Exception error = null;
                uint dfs = uint.MaxValue;
                List<AddressUnit> units = null;
                int conflicts = 0;
                try
                {
                    // DFlash 起点：取所有 DAT 文件基地址的最小值（DAT 即 DFlash 数据）。
                    for (int i = 0; i < pathsArg.Length; i++)
                    {
                        if (FirmwareLoader.IsDat(pathsArg[i]))
                        {
                            uint ba = (baseArg != null && i < baseArg.Length) ? baseArg[i] : 0;
                            if (ba < dfs) dfs = ba;
                        }
                    }
                    units = ConflictDetector.Compare(imagesArg);
                    conflicts = ConflictDetector.CountConflicts(units);
                }
                catch (Exception ex) { error = ex; }

                // ===== 回 UI 线程：构建绑定集合、状态栏（ObservableCollection 须在 UI 线程改）=====
                Action uiWork = () =>
                {
                    if (error != null)
                    {
                        Logger.Error("比较窗口比对失败", error);
                        StatusMessage = "比对失败，请重试或检查文件。";
                        return;
                    }
                    _images = imagesArg;
                    _dflashStart = dfs;
                    _units = units;
                    FileNames.Clear();
                    for (int i = 0; i < pathsArg.Length; i++) FileNames.Add(Path.GetFileName(pathsArg[i]));
                    FileCount = imagesArg.Length;
                    BuildRows();
                    BuildPriorityOptions();
                    _priority = MakePriority(0);
                    UpdateCurrentPriorityName(); // PriorityOptions 建好后刷新浮窗显示
                    StatusMessage = string.Format("{0} 个文件 / 比对 {1} 个地址 / 冲突 {2} 字节",
                        imagesArg.Length, units.Count, conflicts);
                    Logger.Info("比较窗口加载完成：" + StatusMessage);
                    Loaded?.Invoke();
                };
                if (Application.Current != null)
                    Application.Current.Dispatcher.Invoke(uiWork);
                else
                    uiWork(); // 非 UI 上下文（如单元测试）：同步执行
            });
        }

        /// <summary>按 16 字节/行构建显示行（仅至少一个文件有数据的地址）。</summary>
        private void BuildRows()
        {
            // 把冲突信息按地址索引（缓存到字段），便于行内字节级高亮 + FindUnit O(1) 查找
            _unitByAddr.Clear();
            foreach (AddressUnit u in _units) _unitByAddr[u.Address] = u;

            // 收集所有地址（已升序），按 16 字节分组
            List<uint> addrs = _units.ConvertAll(u => u.Address);
            int idx = 0;
            while (idx < addrs.Count)
            {
                uint rowAddr = (addrs[idx] / 16) * 16;
                Row row = new Row();
                row.Address = rowAddr;
                row.Cells = new byte?[FileCount][];
                for (int f = 0; f < FileCount; f++) row.Cells[f] = new byte?[16];
                row.ConflictBits = new bool[16];

                // 填充本行 16 个地址（缺失留 null）
                bool anyConflict = false;
                for (int k = 0; k < 16; k++)
                {
                    uint a = rowAddr + (uint)k;
                    if (_unitByAddr.TryGetValue(a, out AddressUnit u))
                    {
                        for (int f = 0; f < FileCount; f++)
                        {
                            byte v;
                            if (u.Values.TryGetValue(f, out v)) row.Cells[f][k] = v;
                        }
                        if (u.Status == UnitStatus.Conflict)
                        {
                            row.ConflictBits[k] = true;
                            anyConflict = true;
                        }
                    }
                }
                row.HasConflict = anyConflict;
                row.HexPerFile = new string[FileCount];
                row.IsMissingPerFile = new bool[FileCount];
                for (int f = 0; f < FileCount; f++)
                {
                    bool any = false;
                    StringBuilder sb = new StringBuilder();
                    for (int k = 0; k < 16; k++)
                    {
                        byte? b = row.Cells[f][k];
                        if (k > 0) sb.Append(' ');
                        sb.Append(b.HasValue ? b.Value.ToString("X2") : "--");
                        if (b.HasValue) any = true;
                    }
                    row.HexPerFile[f] = sb.ToString();
                    row.IsMissingPerFile[f] = !any;
                }
                Rows.Add(row);

                // 跳过本行覆盖的地址
                while (idx < addrs.Count && addrs[idx] < rowAddr + 16) idx++;
            }

            // 段：连续冲突行（HasConflict）归为同一段，供右键段级选择
            int segId = -1;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].HasConflict)
                {
                    if (i == 0 || !Rows[i - 1].HasConflict) segId++;
                    Rows[i].SegmentId = segId;
                }
                else
                {
                    Rows[i].SegmentId = -1;
                }
            }

            // 冲突段总数（segId 从 -1 起、有冲突递增；segId+1 = 段数，无冲突为 0）
            ConflictSegmentCount = segId + 1;
            CurrentConflictIndex = 0; // 重新加载，重置当前定位
        }

        private void BuildPriorityOptions()
        {
            PriorityOptions.Clear();
            // 「无」优先级模式（索引 0，默认）：不自动仲裁，要求用户手动确认每个冲突段
            PriorityOptions.Add("无");
            for (int i = 0; i < FileCount; i++)
                PriorityOptions.Add(string.Format("文件{0}优先（{1}）", i + 1, FileNames[i]));
        }

        private int[] MakePriority(int sel)
        {
            int[] p = new int[FileCount];
            for (int i = 0; i < FileCount; i++) p[i] = i;
            // sel==0 为「无」模式（默认顺序）；sel>=1 表示文件 (sel-1) 优先，把它移到最前
            if (sel >= 1 && sel <= FileCount)
            {
                int chosen = sel - 1;
                int t = p[0]; p[0] = p[chosen]; p[chosen] = t;
            }
            return p;
        }

        private void RebuildPriority()
        {
            _priority = MakePriority(_prioritySelection);
        }

        /// <summary>按优先级取首个含类型05 起始地址的文件入口；都没有则 null（输出不含05）。</summary>
        private uint? PickStartLinearAddress()
        {
            if (_images == null || _priority == null) return null;
            foreach (int f in _priority)
            {
                if (f >= 0 && f < _images.Length && _images[f].StartLinearAddress.HasValue)
                    return _images[f].StartLinearAddress;
            }
            return null;
        }

        /// <summary>合并：先算预览 → 经 View 确认 → 选保存路径 → 按填充方式处理空隙 → 回读校验。</summary>
        public void Merge()
        {
            // 「无」优先级模式：必须手动确认每个冲突段才能合并，否则提示并中止
            if (IsManualMode)
            {
                int unconfirmed = CountUnconfirmedConflictSegments();
                if (unconfirmed > 0)
                {
                    CardDialog.Show("HexMerge",
                        string.Format("当前为「无」优先级模式，还有 {0} 个冲突段未手动确认，请逐个选择后再合并。", unconfirmed));
                    StatusMessage = string.Format("有 {0} 个冲突段未确认，无法合并。", unconfirmed);
                    return;
                }
            }

            SortedDictionary<uint, byte> merged = Arbiter.Resolve(_units, _choices.Count == 0 ? null : _choices, _priority);

            // 合并前预览：交由 View 弹窗确认，取消则中止
            MergePreview preview = ComputePreview(merged, (GapFill)FillSelection, _dflashStart);
            if (ConfirmMergePreview != null && !ConfirmMergePreview(preview))
            {
                StatusMessage = "已取消合并。";
                return;
            }

            SaveFileDialog dlg = new SaveFileDialog();
            // 默认保存到程序目录下的 UserConfig（用户工作区），不存在则自动创建
            string userDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserConfigFile");
            try { Directory.CreateDirectory(userDir); } catch { }
            dlg.InitialDirectory = userDir;
            dlg.Filter = "Intel HEX (*.hex)|*.hex|所有文件 (*.*)|*.*";
            dlg.FileName = "Full.hex";
            dlg.AddExtension = true;
            dlg.DefaultExt = ".hex";
            if (dlg.ShowDialog() != true)
            {
                StatusMessage = "已取消保存。";
                return;
            }
            string outPath = dlg.FileName;

            // 空隙填充（仅填充同一连续区段内的间隙，跨大区的间隙不填，避免输出膨胀）
            GapFill fill = (GapFill)FillSelection;
            if (fill != GapFill.None)
            {
                byte fb = (fill == GapFill.FillFF) ? (byte)0xFF : (byte)0x00;
                ApplyGapFill(merged, fb, _dflashStart);
            }

            // 起始执行地址（类型05）：按优先级取首个含05 的文件入口，写入 EOF 前
            uint? startAddr = PickStartLinearAddress();
            string hex = HexWriter.Write(merged, startAddr);
            File.WriteAllText(outPath, hex);

            uint mismatch;
            bool ok = RoundTripVerifier.Verify(hex, merged, out mismatch);
            long size = new FileInfo(outPath).Length;

            // 状态栏提示已忽略的"到 DFlash 的区间"（若有）
            string dflashHint = "";
            if (preview.DFlashGaps.Count > 0)
            {
                SkippedGapInfo g0 = preview.DFlashGaps[0];
                dflashHint = string.Format("；已忽略到 DFlash 的区间 0x{0:X8}~0x{1:X8}", g0.Start, g0.End);
                if (preview.DFlashGaps.Count > 1)
                    dflashHint += string.Format(" 等 {0} 处", preview.DFlashGaps.Count);
            }

            StatusMessage = ok
                ? string.Format("已保存到 {0}（{1} 字节，回读校验通过，含 {2} 处手动改选{3}）", outPath, size, _choices.Count, dflashHint)
                : "已保存，但数据校验异常，请检查输出文件。";

            if (ok) Logger.Info("合并保存成功：" + StatusMessage);
            else Logger.Warn(string.Format("合并保存但回读校验失败：0x{0:X} 不匹配，输出路径 {1}", mismatch, outPath));
        }

        /// <summary>填充间隙：连续区段内（间隙 ≤ 1MB）的空隙补 fill；跨大区间隙不填。</summary>
        private static void ApplyGapFill(SortedDictionary<uint, byte> merged, byte fill, uint dflashStart)
        {
            if (merged.Count == 0) return;
            List<uint> keys = new List<uint>(merged.Keys);
            for (int i = 1; i < keys.Count; i++)
            {
                uint prev = keys[i - 1];
                uint cur = keys[i];
                if (cur <= prev) continue;
                uint gap = cur - prev - 1;
                bool toDflash = prev < dflashStart && cur >= dflashStart; // 通往 DFlash 的跨区间隙
                // 仅填充 ≤ 阈值、且非 DFlash 跨区的间隙；DFlash 间隙与超阈值间隙都不填，避免输出膨胀
                if (gap > 0 && gap <= GapFillMaxBytes && !toDflash)
                {
                    for (uint a = prev + 1; a < cur; a++) merged[a] = fill;
                }
            }
        }

        /// <summary>由合并结果计算预览摘要：输出段列表 + 段数/大小/范围/间隙/填充后大小。纯函数，可单测。</summary>
        public static MergePreview ComputePreview(SortedDictionary<uint, byte> merged, GapFill fill, uint dflashStart)
        {
            MergePreview p = new MergePreview();
            if (merged == null || merged.Count == 0) return p;
            bool first = true; uint prev = 0; uint segStart = 0;
            foreach (uint a in merged.Keys)
            {
                if (first) { p.MinAddress = a; segStart = a; first = false; }
                else if (a > prev + 1) // 地址不连续 → 结束当前段、开始新段、记间隙
                {
                    p.Segments.Add(new MergeSegmentInfo { Start = segStart, End = prev, Length = prev - segStart + 1 });
                    uint gap = a - prev - 1;
                    p.GapCount++;
                    p.GapBytes += gap;
                    if (prev < dflashStart && a >= dflashStart)
                    {
                        // 落点跨进 DFlash 区：通往 DFlash 的跨区间隙，默认忽略（不填充），单独记录供提示
                        p.DFlashGaps.Add(new SkippedGapInfo { Start = prev + 1, End = a - 1, Length = gap });
                        p.DFlashGapBytes += gap;
                    }
                    else if (gap <= GapFillMaxBytes)
                    {
                        p.FillBytes += gap; // ≤ 阈值：当前填充方式下会填
                    }
                    else
                    {
                        // 超阈值：跳过间隙，供预览说明未填充原因
                        p.SkippedGaps.Add(new SkippedGapInfo { Start = prev + 1, End = a - 1, Length = gap });
                        p.SkippedGapBytes += gap;
                    }
                    segStart = a;
                }
                prev = a;
            }
            if (!first) p.Segments.Add(new MergeSegmentInfo { Start = segStart, End = prev, Length = prev - segStart + 1 });
            p.MaxAddress = prev;
            p.TotalBytes = merged.Count;
            p.SegmentCount = p.Segments.Count;
            p.FinalBytes = (fill == GapFill.None) ? p.TotalBytes : p.TotalBytes + p.FillBytes;
            return p;
        }

        /// <summary>
        /// 对某地址的冲突进行逐块改选：在参与该地址的文件之间循环切换。
        /// 返回新选定的文件索引；若该地址无冲突/独占返回 -1。
        /// </summary>
        public int CycleChoice(uint address)
        {
            AddressUnit unit = FindUnit(address);
            if (unit == null || unit.Status != UnitStatus.Conflict) return -1;

            // 参与该地址冲突的文件索引（升序）
            List<int> files = new List<int>();
            foreach (int f in unit.Values.Keys) files.Add(f);
            files.Sort();

            // 当前选择：手动选过的，否则按优先级决定的
            int current;
            if (!_choices.TryGetValue(address, out current))
            {
                current = PickByPriority(unit);
            }

            // 循环到下一个参与文件
            int pos = files.IndexOf(current);
            int next = files[(pos + 1) % files.Count];
            _choices[address] = next;
            return next;
        }

        /// <summary>清除某地址的手动改选（恢复按优先级）。</summary>
        public void ClearChoice(uint address)
        {
            _choices.Remove(address);
        }

        /// <summary>某地址当前选定的文件索引（手动 > 优先级 > -1）。</summary>
        public int CurrentChoice(uint address)
        {
            AddressUnit unit = FindUnit(address);
            if (unit == null) return -1;
            int manual;
            if (_choices.TryGetValue(address, out manual)) return manual;
            return PickByPriority(unit);
        }

        private AddressUnit FindUnit(uint address)
        {
            AddressUnit u;
            return _unitByAddr.TryGetValue(address, out u) ? u : null;
        }

        private int PickByPriority(AddressUnit unit)
        {
            foreach (int f in _priority)
                if (unit.Values.ContainsKey(f)) return f;
            foreach (int f in unit.Values.Keys) return f;
            return -1;
        }

        /// <summary>获取某冲突段参与冲突的文件列表（供右键菜单列出"保留 X"项）。</summary>
        public List<int> GetSegmentFiles(Row row)
        {
            List<int> result = new List<int>();
            if (row == null || row.SegmentId < 0) return result;
            foreach (Row r in Rows)
            {
                if (r.SegmentId != row.SegmentId) continue;
                for (int k = 0; k < 16; k++)
                {
                    if (r.ConflictBits != null && r.ConflictBits[k])
                    {
                        AddressUnit u = FindUnit(r.Address + (uint)k);
                        if (u != null)
                            foreach (int f in u.Values.Keys)
                                if (!result.Contains(f)) result.Add(f);
                    }
                }
                break; // 同一段文件集一致，用第一个有冲突的行即可
            }
            result.Sort();
            return result;
        }

        /// <summary>获取某文件的段列表（供比较视图表头显示每文件的 Block 信息）。</summary>
        public List<Segment> GetFileSegments(int f)
        {
            if (_images == null || f < 0 || f >= _images.Length) return new List<Segment>();
            return _images[f].GetSegments();
        }

        /// <summary>段级选择：整段（连续冲突行）保留指定文件；file&lt;0 表示恢复按优先级。</summary>
        public void SetSegmentChoice(Row row, int file)
        {
            if (row == null || row.SegmentId < 0) return;
            CurrentConflictIndex = row.SegmentId + 1; // 手动选择该段，浮窗"当前"同步
            List<Row> segRows = new List<Row>();
            foreach (Row r in Rows) if (r.SegmentId == row.SegmentId) segRows.Add(r);

            List<uint> addrs = new List<uint>();
            foreach (Row r in segRows)
                for (int k = 0; k < 16; k++)
                    if (r.ConflictBits != null && r.ConflictBits[k])
                        addrs.Add(r.Address + (uint)k);

            if (file < 0)
            {
                foreach (uint a in addrs) _choices.Remove(a);
            }
            else
            {
                foreach (uint a in addrs) _choices[a] = file;
            }
            foreach (Row r in segRows) r.RowChoice = file; // 段内所有行高亮刷新
            StatusMessage = file < 0
                ? string.Format("段 0x{0:X}（{1} 行）：恢复按优先级", row.Address, segRows.Count)
                : string.Format("段 0x{0:X}（{1} 行）：保留 {2}", row.Address, segRows.Count, FileNames[file]);
        }

        /// <summary>
        /// 从 fromIndex 之后查找下一个冲突段的起始行索引（连续冲突行只算段首，
        /// 避免在同一段内原地跳）。找到末尾未命中则回绕到开头；没有任何冲突返回 -1。
        /// fromIndex 为 -1 表示从头开始。
        /// </summary>
        public int FindNextConflictSegment(int fromIndex)
        {
            if (Rows.Count == 0) return -1;
            if (fromIndex < 0) fromIndex = -1;

            // 从当前位置向后查找
            for (int i = fromIndex + 1; i < Rows.Count; i++)
                if (IsConflictSegmentStart(i)) return i;
            // 回绕到开头继续找
            for (int i = 0; i <= fromIndex && i < Rows.Count; i++)
                if (IsConflictSegmentStart(i)) return i;
            return -1;
        }

        /// <summary>该行是否为冲突段起始行（本行冲突且上一行非冲突）。</summary>
        private bool IsConflictSegmentStart(int i)
        {
            if (i < 0 || i >= Rows.Count) return false;
            if (!Rows[i].HasConflict) return false;
            return i == 0 || !Rows[i - 1].HasConflict;
        }

        /// <summary>当前是否「无优先级（手动确认每个冲突）」模式：选中的是 PriorityOptions 首项「无」（索引 0，默认）。</summary>
        public bool IsManualMode
        {
            get { return PriorityOptions.Count > 0 && _prioritySelection == 0; }
        }

        /// <summary>未手动确认的冲突段数：段首行 RowChoice &lt; 0（仍按默认/优先级，未手动选定保留文件）。</summary>
        public int CountUnconfirmedConflictSegments()
        {
            int n = 0;
            for (int i = 0; i < Rows.Count; i++)
                if (IsConflictSegmentStart(i) && Rows[i].RowChoice < 0) n++;
            return n;
        }

        /// <summary>
        /// 跳转到下一个冲突段：返回目标行索引（-1=无冲突可跳），并更新状态栏提示。
        /// currentIndex 为当前光标行（通常取选中行或首可见行）。
        /// </summary>
        public int GoNextConflict(int currentIndex)
        {
            // 未定位（首次进入或重置后，CurrentConflictIndex==0，浮窗显示"-/N"）时从最前找首个冲突段；
            // 否则从当前光标行向后找。避免光标恰停在首个冲突段（如行0即冲突）时被"下一个"跳过，
            // 导致首次点"下一个"落到第 2 段而非第 1 段。
            int anchor = _currentConflictIndex == 0 ? -1 : currentIndex;
            int next = FindNextConflictSegment(anchor);
            if (next < 0)
            {
                StatusMessage = "没有冲突可跳转。";
                return -1;
            }
            CurrentConflictIndex = Rows[next].SegmentId + 1; // 1-based
            StatusMessage = string.Format("已跳转到冲突段 0x{0:X8}（第 {1} 行）", Rows[next].Address, next + 1);
            return next;
        }

        /// <summary>
        /// 从 fromIndex 之前查找上一个冲突段的起始行索引。找到开头未命中则回绕到末尾；无冲突返回 -1。
        /// </summary>
        public int FindPrevConflictSegment(int fromIndex)
        {
            if (Rows.Count == 0) return -1;
            if (fromIndex >= Rows.Count) fromIndex = Rows.Count;
            if (fromIndex < 0) fromIndex = 0;

            // 若 fromIndex 在冲突段内，跳过当前段（从段首之前开始找），避免点段中间跳回本段首
            int start = fromIndex - 1;
            if (fromIndex < Rows.Count && Rows[fromIndex].HasConflict)
                while (start >= 0 && Rows[start].HasConflict) start--;

            // 从起点向前查找段首
            for (int i = start; i >= 0; i--)
                if (IsConflictSegmentStart(i)) return i;
            // 回绕到末尾继续找
            for (int i = Rows.Count - 1; i >= fromIndex && i >= 0; i--)
                if (IsConflictSegmentStart(i)) return i;
            return -1;
        }

        /// <summary>跳转到上一个冲突段：返回目标行索引（-1=无冲突可跳），并更新状态栏提示。</summary>
        public int GoPrevConflict(int currentIndex)
        {
            int prev = FindPrevConflictSegment(currentIndex);
            if (prev < 0)
            {
                StatusMessage = "没有冲突可跳转。";
                return -1;
            }
            CurrentConflictIndex = Rows[prev].SegmentId + 1; // 1-based
            StatusMessage = string.Format("已跳转到冲突段 0x{0:X8}（第 {1} 行）", Rows[prev].Address, prev + 1);
            return prev;
        }

        /// <summary>查找地址所在行索引：精确匹配 16 对齐行，否则取不大于目标的最近行；无则 -1。</summary>
        public int FindRowForAddress(uint addr)
        {
            uint target = (addr / 16u) * 16u;
            int best = -1;
            for (int i = 0; i < Rows.Count; i++)
            {
                uint a = Rows[i].Address;
                if (a == target) return i;
                if (a < target) best = i;
                else break; // 已超过 target，Rows 按地址升序，后续更大
            }
            return best;
        }

        /// <summary>转到地址（数值）：返回目标行索引（-1=未找到），并更新状态栏。</summary>
        public int GoToAddress(uint addr)
        {
            int idx = FindRowForAddress(addr);
            if (idx < 0) { StatusMessage = string.Format("未找到地址 0x{0:X} 附近的行。", addr); return -1; }
            StatusMessage = string.Format("已转到 0x{0:X8}", Rows[idx].Address);
            return idx;
        }

        /// <summary>转到地址（文本，支持 0x 前缀，按十六进制）：解析失败/未找到返回 -1 并更新状态栏。</summary>
        public int GoToAddress(string text)
        {
            uint addr;
            if (!TryParseAddress(text, out addr)) { StatusMessage = "地址格式无效（示例：0x1000 或 1000）。"; return -1; }
            return GoToAddress(addr);
        }

        private static bool TryParseAddress(string s, out uint addr)
        {
            addr = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            return uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out addr);
        }

        /// <summary>按扩展名加载：DAT 用传入基地址，HEX 直接解析。</summary>
        // 按 HEX/DAT 分发的加载逻辑已提取到 Core.FirmwareLoader（供 FileSelectViewModel 复用）
    }
}

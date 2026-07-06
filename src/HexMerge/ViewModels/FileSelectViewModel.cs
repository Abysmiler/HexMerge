using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HexMerge.Core;
using HexMerge.Models;
using Microsoft.Win32;

namespace HexMerge.ViewModels
{
    /// <summary>
    /// 文件选择窗的视图模型（参考 WinMerge 打开对话框：分组 + 交换 + 状态栏）。
    /// 任务12 阶段：比较命令先解析+显示摘要（接 Core），任务13 再接比较视图。
    /// </summary>
    public class FileSelectViewModel : ObservableObject
    {
        private string _path1 = "";
        public string Path1 { get { return _path1; } set { SetProperty(ref _path1, value); UpdateBaseAddr(1); UpdateStatus(); } }

        private string _path2 = "";
        public string Path2 { get { return _path2; } set { SetProperty(ref _path2, value); UpdateBaseAddr(2); UpdateStatus(); } }

        private string _path3 = "";
        public string Path3 { get { return _path3; } set { SetProperty(ref _path3, value); UpdateBaseAddr(3); UpdateStatus(); } }

        // DAT 基地址（仅 .dat 文件用；从文件名预填，可手动改）
        private string _baseAddr1 = "";
        public string BaseAddr1 { get { return _baseAddr1; } set { SetProperty(ref _baseAddr1, value); } }
        private string _baseAddr2 = "";
        public string BaseAddr2 { get { return _baseAddr2; } set { SetProperty(ref _baseAddr2, value); } }
        private string _baseAddr3 = "";
        public string BaseAddr3 { get { return _baseAddr3; } set { SetProperty(ref _baseAddr3, value); } }

        private bool _baseVisible1, _baseVisible2, _baseVisible3;
        public bool BaseAddrVisible1 { get { return _baseVisible1; } private set { SetProperty(ref _baseVisible1, value); } }
        public bool BaseAddrVisible2 { get { return _baseVisible2; } private set { SetProperty(ref _baseVisible2, value); } }
        public bool BaseAddrVisible3 { get { return _baseVisible3; } private set { SetProperty(ref _baseVisible3, value); } }

        private string _resultMessage = "";
        public string ResultMessage { get { return _resultMessage; } private set { SetProperty(ref _resultMessage, value); } }

        private string _statusMessage = "请选择至少 2 个文件。";
        public string StatusMessage { get { return _statusMessage; } private set { SetProperty(ref _statusMessage, value); } }

        private readonly ObservableCollection<string> _pathHistory = new ObservableCollection<string>();
        /// <summary>路径下拉历史（三个路径框共享，最近的在前）。</summary>
        public ObservableCollection<string> PathHistory { get { return _pathHistory; } }

        public ICommand Browse1Command { get; private set; }
        public ICommand Browse2Command { get; private set; }
        public ICommand Browse3Command { get; private set; }
        public ICommand Swap12Command { get; private set; }
        public ICommand Swap23Command { get; private set; }
        public ICommand Swap13Command { get; private set; }
        public ICommand CompareCommand { get; private set; }

        /// <summary>解析比较成功后触发：文件路径、基地址、已解析的 MemoryImage[]（供比较窗复用，避免重复解析）。</summary>
        public event Action<string[], uint[], MemoryImage[]> CompareSucceeded;

        public FileSelectViewModel()
        {
            Browse1Command = new RelayCommand(() => Browse(v => Path1 = v));
            Browse2Command = new RelayCommand(() => Browse(v => Path2 = v));
            Browse3Command = new RelayCommand(() => Browse(v => Path3 = v));
            Swap12Command = new RelayCommand(() => Swap(1, 2));
            Swap23Command = new RelayCommand(() => Swap(2, 3));
            Swap13Command = new RelayCommand(() => Swap(1, 3));
            CompareCommand = new RelayCommand(Compare);
        }

        private void Browse(Action<string> setPath)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            // 默认打开程序目录下的 UserConfigFile（用户工作区），不存在则创建
            string userDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserConfigFile");
            try { Directory.CreateDirectory(userDir); } catch { }
            dlg.InitialDirectory = userDir;
            dlg.Filter = "HEX/DAT 文件 (*.hex;*.dat)|*.hex;*.dat|所有文件 (*.*)|*.*";
            dlg.CheckFileExists = true;
            if (dlg.ShowDialog() == true)
            {
                setPath(dlg.FileName);
                AddHistory(dlg.FileName);
            }
        }

        /// <summary>把路径加入下拉历史（去重，最新的在前）。</summary>
        private void AddHistory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (!_pathHistory.Contains(path))
            {
                _pathHistory.Insert(0, path);
            }
        }

        /// <summary>交换两个组的路径。</summary>
        private void Swap(int a, int b)
        {
            string pa = GetPath(a);
            string pb = GetPath(b);
            SetPath(a, pb);
            SetPath(b, pa);
        }

        private string GetPath(int n)
        {
            if (n == 1) return Path1;
            if (n == 2) return Path2;
            return Path3;
        }

        private void SetPath(int n, string value)
        {
            if (n == 1) Path1 = value;
            else if (n == 2) Path2 = value;
            else Path3 = value;
        }

        /// <summary>根据已填路径数量更新底部状态栏。</summary>
        private void UpdateStatus()
        {
            int n = 0;
            if (!string.IsNullOrWhiteSpace(Path1)) n++;
            if (!string.IsNullOrWhiteSpace(Path2)) n++;
            if (!string.IsNullOrWhiteSpace(Path3)) n++;
            StatusMessage = n < 2
                ? string.Format("已选择 {0} 个文件，至少需要 2 个。", n)
                : string.Format("已选择 {0} 个文件，可以比较。", n);
        }

        /// <summary>比较：收集非空路径，后台解析+比对，产出摘要并带出已解析结果（供比较窗复用）。</summary>
        public void Compare()
        {
            // UI 线程：收集路径与基地址（快）
            List<string> paths = new List<string>();
            List<uint> baseAddrs = new List<uint>();
            if (!string.IsNullOrWhiteSpace(Path1)) { paths.Add(Path1); baseAddrs.Add(ParseBaseAddrUser(BaseAddr1)); }
            if (!string.IsNullOrWhiteSpace(Path2)) { paths.Add(Path2); baseAddrs.Add(ParseBaseAddrUser(BaseAddr2)); }
            if (!string.IsNullOrWhiteSpace(Path3)) { paths.Add(Path3); baseAddrs.Add(ParseBaseAddrUser(BaseAddr3)); }

            if (paths.Count < 2)
            {
                ResultMessage = "请至少选择 2 个文件再比较。";
                return;
            }

            ResultMessage = "";
            StatusMessage = "正在解析文件…";

            string[] pathsArr = paths.ToArray();
            uint[] baseArr = baseAddrs.ToArray();

            // 后台解析（防大文件 UI 假死）
            Task.Run(() =>
            {
                Exception error = null;
                MemoryImage[] images = null;
                List<AddressUnit> units = null;
                int conflicts = 0;
                try
                {
                    List<MemoryImage> imgs = new List<MemoryImage>();
                    for (int i = 0; i < pathsArr.Length; i++)
                        imgs.Add(FirmwareLoader.Load(pathsArr[i], baseArr[i], i));
                    images = imgs.ToArray();
                    units = ConflictDetector.Compare(images);
                    conflicts = ConflictDetector.CountConflicts(units);
                }
                catch (Exception ex) { error = ex; }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (error != null)
                    {
                        Logger.Error("文件解析/比对失败", error);
                        ResultMessage = "解析失败，请检查文件格式是否正确。";
                        StatusMessage = "解析失败，请检查文件。";
                        return;
                    }
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("解析成功：");
                    for (int i = 0; i < images.Length; i++)
                    {
                        int segs = images[i].GetSegments().Count;
                        sb.AppendLine(string.Format("  文件{0}：{1} 字节 / {2} 段 / 0x{3:X}-0x{4:X}",
                            i + 1, images[i].Count, segs, images[i].MinAddress, images[i].MaxAddress));
                    }
                    sb.AppendLine(string.Format("冲突字节（值不同）：{0}", conflicts));
                    sb.AppendLine(string.Format("比对单元总数（并集）：{0}", units.Count));
                    ResultMessage = sb.ToString();
                    StatusMessage = string.Format("比较完成：{0} 个文件，冲突 {1} 字节。", images.Length, conflicts);
                    Logger.Info(string.Format("解析成功：{0} 个文件，冲突 {1} 字节，进入比较窗口", images.Length, conflicts));
                    if (CompareSucceeded != null) CompareSucceeded(pathsArr, baseArr, images);
                });
            });
        }

        /// <summary>从文件名解析基地址（如 DFLASH_0xE00000_xxx.dat -> 0xE00000），失败返回 0。</summary>
        private static uint ParseBaseAddressFromName(string fileName)
        {
            Match m = Regex.Match(fileName, "0x([0-9A-Fa-f]+)");
            if (m.Success)
            {
                try { return Convert.ToUInt32(m.Groups[1].Value, 16); }
                catch { }
            }
            return 0;
        }

        /// <summary>解析用户输入的基地址字符串（支持 0x 前缀，十六进制），失败返回 0。</summary>
        private static uint ParseBaseAddrUser(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            string t = s.Trim();
            if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            uint v;
            uint.TryParse(t, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v);
            return v;
        }

        /// <summary>路径变化时更新该槽位的 DAT 基地址可见性与预填值。</summary>
        private void UpdateBaseAddr(int n)
        {
            string p = GetPath(n);
            bool isDat = FirmwareLoader.IsDat(p);
            SetBaseVisible(n, isDat);
            SetBaseAddr(n, isDat ? "0x" + ParseBaseAddressFromName(Path.GetFileName(p)).ToString("X8") : "");
        }

        private void SetBaseAddr(int n, string v) { if (n == 1) BaseAddr1 = v; else if (n == 2) BaseAddr2 = v; else BaseAddr3 = v; }
        private void SetBaseVisible(int n, bool v) { if (n == 1) BaseAddrVisible1 = v; else if (n == 2) BaseAddrVisible2 = v; else BaseAddrVisible3 = v; }
    }
}

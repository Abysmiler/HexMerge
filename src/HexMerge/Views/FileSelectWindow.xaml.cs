using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HexMerge.Models;
using HexMerge.ViewModels;

namespace HexMerge.Views
{
    /// <summary>
    /// FileSelectWindow 的交互逻辑。处理拖拽（路径设置）；
    /// 其余通过绑定 + 命令交给 FileSelectViewModel。
    /// </summary>
    public partial class FileSelectWindow : Window
    {
        private readonly FileSelectViewModel _vm;

        public FileSelectWindow() : this(null, null) { }

        /// <summary>带预填路径/基地址的构造（"重新比较"返回时用，保留上次文件）。</summary>
        public FileSelectWindow(string[] paths, uint[] baseAddrs)
        {
            InitializeComponent();
            Title = AppInfo.Title; // 统一标题：HexMerge V{版本}
            _vm = new FileSelectViewModel();
            DataContext = _vm;
            _vm.CompareSucceeded += OnCompareSucceeded;
            RestorePaths(paths, baseAddrs);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); }; // Esc 关窗（取消）
        }

        /// <summary>取消：关闭文件选择窗（启动时即退出程序）。</summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>把上次的文件路径与 DAT 基地址恢复到各槽位。</summary>
        private void RestorePaths(string[] paths, uint[] baseAddrs)
        {
            if (paths == null) return;
            if (paths.Length > 0) SetSlot(1, paths[0], baseAddrs, 0);
            if (paths.Length > 1) SetSlot(2, paths[1], baseAddrs, 1);
            if (paths.Length > 2) SetSlot(3, paths[2], baseAddrs, 2);
        }

        /// <summary>设某槽位路径；.dat 时用传入基地址覆盖文件名预填值。</summary>
        private void SetSlot(int slot, string path, uint[] baseAddrs, int i)
        {
            if (slot == 1) _vm.Path1 = path;
            else if (slot == 2) _vm.Path2 = path;
            else _vm.Path3 = path;

            bool isDat = System.IO.Path.GetExtension(path).ToLowerInvariant() == ".dat";
            if (isDat && baseAddrs != null && i < baseAddrs.Length)
            {
                string v = "0x" + baseAddrs[i].ToString("X8");
                if (slot == 1) _vm.BaseAddr1 = v;
                else if (slot == 2) _vm.BaseAddr2 = v;
                else _vm.BaseAddr3 = v;
            }
        }

        /// <summary>比较成功：打开比较视图（带已解析结果，避免重复解析），关闭文件选择窗。</summary>
        private void OnCompareSucceeded(string[] paths, uint[] baseAddrs, MemoryImage[] images)
        {
            CompareWindow cw = new CompareWindow(paths, baseAddrs, images);
            cw.Show();
            Close();
        }

        /// <summary>拖拽悬停（窗口级）：仅接受文件拖入。</summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>拖拽释放（窗口级）：把拖入的文件按顺序填入 Path1/Path2/Path3（空槽优先，按序）。</summary>
        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;
            e.Handled = true;
            foreach (string f in files)
            {
                if (string.IsNullOrWhiteSpace(_vm.Path1)) _vm.Path1 = f;
                else if (string.IsNullOrWhiteSpace(_vm.Path2)) _vm.Path2 = f;
                else if (string.IsNullOrWhiteSpace(_vm.Path3)) _vm.Path3 = f;
                else break; // 三组都满，忽略剩余
            }
        }
    }
}

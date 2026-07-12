using System;
using System.Windows;
using System.Windows.Media;

namespace HexMerge.Views
{
    /// <summary>
    /// 模态遮罩：在弹出卡片对话框前，于其宿主窗口之上盖一层半透明暗色，
    /// 把宿主（含其系统标题栏的关闭 X）压暗遮住，让卡片对话框自身的 ✕ 成为
    /// 唯一、且位置正确的关闭入口——消除"卡片 ✕ 与宿主标题栏 X 两个关闭按钮"的困惑。
    /// 用法：using (ModalScrim.Over(owner)) { w.ShowDialog(); }
    /// </summary>
    public sealed class ModalScrim : IDisposable
    {
        private Window _scrim;

        private ModalScrim(Window owner)
        {
            if (owner == null) return;

            // 宿主外框（DIP）；最大化时 Left/Top/Width/Height 不可靠，退回工作区。
            Rect r = owner.WindowState == WindowState.Maximized
                ? SystemParameters.WorkArea
                : new Rect(owner.Left, owner.Top, owner.Width, owner.Height);
            if (double.IsNaN(r.Left) || r.Width <= 0 || r.Height <= 0) return;

            _scrim = new Window
            {
                Style = new Style(typeof(Window)), // 退出 ModernWpf 隐式窗口样式，避免遮罩自带标题栏
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(0x66, 0x0F, 0x19, 0x22)), // ~40% 深岩板
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = r.Left,
                Top = r.Top,
                Width = r.Width,
                Height = r.Height,
                Owner = owner,
                ShowActivated = false,
                IsHitTestVisible = false
            };
            _scrim.Show();
        }

        /// <summary>在 owner 之上铺遮罩；owner 为 null 时为空操作。</summary>
        public static ModalScrim Over(Window owner) => new ModalScrim(owner);

        public void Dispose()
        {
            if (_scrim != null) { _scrim.Close(); _scrim = null; }
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HexMerge.Views
{
    /// <summary>
    /// 通用卡片式对话框（无边框圆角卡片 + 主题色 + 标题栏可拖动 + 投影），
    /// 统一替代系统 MessageBox：<see cref="Show"/>（消息，单"确定"）、
    /// <see cref="Confirm"/>（"确定/取消"，返回是否确定）。
    /// 配色取自 Views/Theme.xaml（App.Resources 全局），与关于/浮窗一致。
    /// </summary>
    public static class CardDialog
    {
        /// <summary>消息框：显示消息与单个"确定"按钮。</summary>
        public static void Show(string title, string message, Window owner = null)
        {
            Build(title, message, owner, false);
        }

        /// <summary>确认框：显示消息与"确定/取消"，返回是否点了确定（Esc/取消/✕ 均返回 false）。</summary>
        public static bool Confirm(string title, string message, Window owner = null)
        {
            return Build(title, message, owner, true);
        }

        private static bool Build(string title, string message, Window owner, bool confirmMode)
        {
            Brush ink = (Brush)(Application.Current.TryFindResource("InkBrush") ?? Brushes.Black);
            Brush muted = (Brush)(Application.Current.TryFindResource("MutedBrush") ?? Brushes.Gray);
            Brush cardBorder = (Brush)(Application.Current.TryFindResource("CardBorderBrush") ?? Brushes.LightGray);
            Brush headerBg = (Brush)(Application.Current.TryFindResource("WindowBgBrush") ?? Brushes.WhiteSmoke);
            Brush iconHover = (Brush)(Application.Current.TryFindResource("IconHoverBrush") ?? Brushes.LightGray);
            Style primary = Application.Current.TryFindResource("PrimaryButton") as Style;
            Style secondary = Application.Current.TryFindResource("SecondaryButton") as Style;

            Window w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.Height,
                Width = 380,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterScreen // 屏幕中心，避免与浮窗位置绑定
            };

            // 卡片：白底 + 浅边框 + 圆角 + 投影
            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.22, Color = Color.FromRgb(0, 0, 0) }
            };

            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 标题栏（可拖动）+ 关闭
            Border header = new Border
            {
                Background = headerBg,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(14, 6, 6, 6)
            };
            header.MouseLeftButtonDown += (s, ev) => { try { if (ev.LeftButton == MouseButtonState.Pressed) w.DragMove(); } catch { } };
            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock titleTb = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
            Border closeBtn = MakeIconButton("✕", muted, iconHover);
            closeBtn.MouseLeftButtonDown += (s, ev) => { ev.Handled = true; w.Close(); };
            Grid.SetColumn(titleTb, 0); Grid.SetColumn(closeBtn, 1);
            hg.Children.Add(titleTb); hg.Children.Add(closeBtn);
            header.Child = hg;
            Grid.SetRow(header, 0);

            // 内容：消息 + 按钮行
            StackPanel body = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            body.Children.Add(new TextBlock { Text = message, FontSize = 12.5, Foreground = ink, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            if (confirmMode)
            {
                Button cancel = new Button { Content = "取消", Width = 90, Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(0, 0, 8, 0), Style = secondary };
                cancel.Click += (s, ev) => { w.DialogResult = false; w.Close(); };
                btns.Children.Add(cancel);
            }
            Button ok = new Button { Content = "确定", Width = 90, Padding = new Thickness(0, 6, 0, 6), Style = primary, IsDefault = true };
            ok.Click += (s, ev) => { if (confirmMode) w.DialogResult = true; w.Close(); };
            btns.Children.Add(ok);
            body.Children.Add(btns);

            Grid.SetRow(body, 1);
            cardGrid.Children.Add(header);
            cardGrid.Children.Add(body);
            card.Child = cardGrid;
            w.Content = card;

            // Esc：消息模式直接关，确认模式返回 false
            w.PreviewKeyDown += (s, ev) => { if (ev.Key == Key.Escape) { if (confirmMode) w.DialogResult = false; w.Close(); } };

            w.ShowDialog();
            return confirmMode && w.DialogResult == true;
        }

        /// <summary>小图标按钮（关闭等）—— 与 CompareWindow.MakeIconButton 同风格，此处自包含。</summary>
        private static Border MakeIconButton(string text, Brush fg, Brush hoverBg)
        {
            Border b = new Border
            {
                Width = 22, Height = 22,
                CornerRadius = new CornerRadius(4),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock { Text = text, Foreground = fg, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s, e) => b.Background = hoverBg;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            return b;
        }
    }
}

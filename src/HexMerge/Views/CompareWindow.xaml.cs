using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HexMerge.Models;
using HexMerge.ViewModels;

namespace HexMerge.Views
{
    /// <summary>
    /// 比较视图窗口：定位栏（鸟瞰，自绘）+ 自绘比较网格 <see cref="HexCompareView"/> + 滚动条。
    /// 渲染层全部自绘（零 DataGrid），滚动时只重画可见行，彻底消除卡顿。
    /// 着色决策与行绘制在 HexCompareView / HexCellPaint；本类只负责窗口骨架、菜单、定位栏跳转。
    /// </summary>
    public partial class CompareWindow : Window
    {
        private readonly CompareViewModel _vm;
        private readonly string[] _paths;       // 打开时的文件路径（供"重新比较"带回）
        private readonly uint[] _baseAddrs;     // 打开时的 DAT 基地址
        private HexCompareView _view;
        private LocationBar _locationBar;
        private Window _navigatorWindow;   // 冲突导航浮窗（避免重复弹出）

        // 行底配色统一到 HexPalette（与 HexCompareView 共用）；此处仅保留定位栏专属的视口指示器画笔
        private static readonly SolidColorBrush ThumbBrush = MakeAlphaBrush(80, 90, 90, 90); // 定位栏视口指示器（半透明灰）
        private static SolidColorBrush MakeAlphaBrush(byte a, int r, int g, int b)
        {
            SolidColorBrush br = new SolidColorBrush(Color.FromArgb(a, (byte)r, (byte)g, (byte)b));
            br.Freeze();
            return br;
        }

        public CompareWindow(string[] paths, uint[] baseAddrs, MemoryImage[] images)
        {
            InitializeComponent();
            Title = AppInfo.Title; // 统一标题：HexMerge V{版本}
            _vm = new CompareViewModel();
            DataContext = _vm;
            _paths = paths;
            _baseAddrs = baseAddrs;
            _vm.Loaded += OnVmLoaded;            // 数据就绪后再构建菜单、刷新布局
            BuildHost();                          // 创建控件（数据未就绪时渲染空白）
            _vm.Load(paths, baseAddrs, images);   // 后台比对（解析结果复用，避免重复解析）
            _vm.ConfirmMergePreview = ShowMergePreview;
            Loaded += (s, e) => { _locationBar?.InvalidateVisual(); WarmupDropDown(); };
            Closed += (s, e) => { _vm.Loaded -= OnVmLoaded; }; // 关窗后退订，防后台完成时操作已释放控件
            // 查找功能暂时屏蔽：
            // PreviewKeyDown += (s, e) => { if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { OpenGotoAddress(); e.Handled = true; } };
        }

        /// <summary>VM 数据加载完成（后台解析后）：构建依赖数据的菜单，并触发视图重新布局/渲染。</summary>
        private void OnVmLoaded()
        {
            BuildMenus();
            _view?.InvalidateTextCache(); // 数据就绪：失效首次（空数据）建的文本缓存，触发重建段信息/文件名
            _view?.InvalidateMeasure();   // 重跑 ArrangeOverride（按实际 FileCount 铺字节宽）+ OnRender
            _locationBar?.InvalidateVisual();
            ShowConflictNavigator();       // 比较完成后弹出冲突导航浮窗
        }

        /// <summary>冲突导航浮窗：显示冲突计数/当前序号、当前优先级，提供上/下一个冲突按钮；可拖动（ToolWindow 标题栏）。</summary>
        private void ShowConflictNavigator()
        {
            if (_navigatorWindow != null) { _navigatorWindow.Activate(); return; } // 已开则前置，不重复弹

            // 配色统一取自共享主题 Theme.xaml（App.Resources 全局合并）；改主题即全应用生效
            Brush accent = (Brush)FindResource("AccentBrush");
            Brush accentHover = (Brush)FindResource("AccentHoverBrush");
            Brush muted = (Brush)FindResource("MutedBrush");
            Brush ink = (Brush)FindResource("InkBrush");
            Brush neutralBg = (Brush)FindResource("NeutralBgBrush");
            Brush neutralHover = (Brush)FindResource("NeutralHoverBrush");
            Brush cardBorder = (Brush)FindResource("CardBorderBrush");
            Brush windowBg = (Brush)FindResource("WindowBgBrush");
            Brush subtleBg = (Brush)FindResource("SubtleBlueBgBrush");
            Brush subtleBorder = (Brush)FindResource("SubtleBlueBorderBrush");
            Brush iconHover = (Brush)FindResource("IconHoverBrush");

            Window w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                DataContext = _vm
            };

            // 卡片：圆角 + 细边 + 投影
            Border card = new Border
            {
                Width = 180,
                Background = Brushes.White,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.22, Color = Color.FromRgb(0, 0, 0) }
            };
            StackPanel root = new StackPanel();

            // 标题栏（可拖动）+ 关闭
            Border header = new Border
            {
                Background = windowBg,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(10, 5, 6, 5)
            };
            header.MouseLeftButtonDown += (s, e) => { try { if (e.LeftButton == MouseButtonState.Pressed) w.DragMove(); } catch { } };
            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = new TextBlock { Text = "冲突导航", FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
            Border closeBtn = MakeIconButton("✕", muted, iconHover);
            closeBtn.MouseLeftButtonDown += (s, e) => { e.Handled = true; w.Close(); };
            Grid.SetColumn(title, 0); Grid.SetColumn(closeBtn, 1);
            hg.Children.Add(title); hg.Children.Add(closeBtn);
            header.Child = hg;
            root.Children.Add(header);

            // 大字：当前 / 总数（绑定 ConflictInfoText）
            StackPanel countArea = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 3) };
            TextBlock bigCount = new TextBlock { FontSize = 16, FontWeight = FontWeights.Bold, Foreground = accent, HorizontalAlignment = HorizontalAlignment.Center };
            bigCount.SetBinding(TextBlock.TextProperty, new Binding("ConflictInfoText"));
            countArea.Children.Add(bigCount);
            countArea.Children.Add(new TextBlock { Text = "当前 / 总数", FontSize = 10, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) });
            root.Children.Add(countArea);

            // 优先级框（绑定 CurrentPriorityName）
            Border prioBox = new Border
            {
                Background = subtleBg,
                BorderBrush = subtleBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(10, 8, 10, 8),
                Padding = new Thickness(8, 5, 8, 6)
            };
            StackPanel prioSp = new StackPanel();
            prioSp.Children.Add(new TextBlock { Text = "当前优先级", FontSize = 10, Foreground = muted });
            TextBlock prioVal = new TextBlock { FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = ink, Margin = new Thickness(0, 1, 0, 0), TextWrapping = TextWrapping.Wrap };
            prioVal.SetBinding(TextBlock.TextProperty, new Binding("CurrentPriorityName"));
            prioSp.Children.Add(prioVal);
            prioBox.Child = prioSp;
            root.Children.Add(prioBox);

            // 按钮：上一个 / 下一个
            Grid btnGrid = new Grid { Margin = new Thickness(10, 0, 10, 10) };
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Pixel) });
            btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border prev = MakeNavButton("‹ 上一个", neutralBg, neutralHover, ink);
            prev.MouseLeftButtonUp += (s, e) => { int p = _vm.GoPrevConflict(_view.CurrentRowIndex); if (p >= 0) _view.GotoRow(p); };
            Border next = MakeNavButton("下一个 ›", accent, accentHover, Brushes.White);
            next.MouseLeftButtonUp += (s, e) => { int n = _vm.GoNextConflict(_view.CurrentRowIndex); if (n >= 0) _view.GotoRow(n); };
            Grid.SetColumn(prev, 0); Grid.SetColumn(next, 2);
            btnGrid.Children.Add(prev); btnGrid.Children.Add(next);
            root.Children.Add(btnGrid);

            card.Child = root;
            w.Content = card;
            w.Closed += (s, e) => { _navigatorWindow = null; MenuToggleNavigator.IsChecked = false; };
            _navigatorWindow = w;
            w.Show();
            MenuToggleNavigator.IsChecked = true;
        }

        /// <summary>菜单"冲突导航浮窗"：切换浮窗开/关。</summary>
        private void ToggleNavigator_Click(object sender, RoutedEventArgs e)
        {
            if (_navigatorWindow != null) _navigatorWindow.Close();
            else ShowConflictNavigator();
        }

        /// <summary>导航按钮（Border 模拟，圆角 + hover 变色）。</summary>
        private static Border MakeNavButton(string text, Brush bg, Brush bgHover, Brush fg)
        {
            Border b = new Border
            {
                Height = 26,
                CornerRadius = new CornerRadius(4),
                Background = bg,
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock { Text = text, Foreground = fg, FontSize = 11, FontWeight = FontWeights.Medium, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            b.MouseEnter += (s, e) => b.Background = bgHover;
            b.MouseLeave += (s, e) => b.Background = bg;
            return b;
        }

        /// <summary>小图标按钮（关闭等）。</summary>
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

        /// <summary>构建主区：定位栏(56) + 自绘比较视图(1*) + 垂直滚动条(16)。</summary>
        private void BuildHost()
        {
            Grid host = CompareHost;
            host.ColumnDefinitions.Clear();
            host.Children.Clear();

            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

            // 定位栏（鸟瞰，自绘：每行按状态显示同步颜色）
            _locationBar = new LocationBar();
            _locationBar.SetViewModel(_vm);
            _locationBar.MouseLeftButtonDown += Location_Click;
            _locationBar.MouseMove += Location_Drag;
            _locationBar.MouseLeftButtonUp += Location_Up;
            Grid.SetColumn(_locationBar, 0);
            host.Children.Add(_locationBar);

            // 自绘比较视图（承载所有文件列的地址 + 字节网格）
            _view = new HexCompareView();
            _view.SetViewModel(_vm);
            _view.Scrolled += (s, e) => _locationBar.InvalidateVisual(); // 滚动/缩放时刷新定位栏视口指示器
            Grid.SetColumn(_view, 1);
            host.Children.Add(_view);
            _locationBar.SetView(_view);

            // 滚动条（由 view 自管偏移与几何）
            ScrollBar sb = new ScrollBar { Orientation = Orientation.Vertical };
            _view.AttachScrollBar(sb);
            Grid.SetColumn(sb, 2);
            host.Children.Add(sb);
        }

        /// <summary>
        /// 构建顶部菜单栏的动态子项：优先级（随参与文件数变化）、空隙填充（固定三项）。
        /// 当前选择用勾选标记表示；点击即选定并关闭子菜单（单选语义）。
        /// </summary>
        private void BuildMenus()
        {
            // 优先级：直接读 VM 的 PriorityOptions（含末尾「无」项），单一数据源，与浮窗 CurrentPriorityName 一致
            MenuPriority.Items.Clear();
            for (int i = 0; i < _vm.PriorityOptions.Count; i++)
            {
                int sel = i;
                MenuItem mi = new MenuItem();
                mi.Header = _vm.PriorityOptions[i];
                mi.IsCheckable = true;
                mi.IsChecked = (_vm.PrioritySelection == i);
                mi.Click += (s, e) => { _vm.PrioritySelection = sel; RefreshMenuChecks(); MenuPriority.IsSubmenuOpen = false; };
                MenuPriority.Items.Add(mi);
            }

            // 空隙填充：不填充 / 0xFF / 0x00
            string[] fills = { "不填充", "0xFF", "0x00" };
            MenuFill.Items.Clear();
            for (int i = 0; i < fills.Length; i++)
            {
                int sel = i;
                MenuItem mi = new MenuItem();
                mi.Header = fills[i];
                mi.IsCheckable = true;
                mi.IsChecked = (_vm.FillSelection == i);
                mi.Click += (s, e) => { _vm.FillSelection = sel; RefreshMenuChecks(); MenuFill.IsSubmenuOpen = false; };
                MenuFill.Items.Add(mi);
            }
        }

        /// <summary>按当前 PrioritySelection / FillSelection 刷新菜单勾选标记。</summary>
        private void RefreshMenuChecks()
        {
            for (int i = 0; i < MenuPriority.Items.Count; i++)
                if (MenuPriority.Items[i] is MenuItem mp) mp.IsChecked = (_vm.PrioritySelection == i);
            for (int i = 0; i < MenuFill.Items.Count; i++)
                if (MenuFill.Items[i] is MenuItem mf) mf.IsChecked = (_vm.FillSelection == i);
        }

        /// <summary>定位栏：按住可像滚动条一样拖动。按下捕获鼠标并定位，移动持续跟随，抬起释放。</summary>
        private void Location_Click(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement loc = (FrameworkElement)sender;
            loc.CaptureMouse();
            LocationScroll(loc, e);
            e.Handled = true;
        }

        private void Location_Drag(object sender, MouseEventArgs e)
        {
            FrameworkElement loc = (FrameworkElement)sender;
            if (!loc.IsMouseCaptured) return;
            LocationScroll(loc, e);
        }

        private void Location_Up(object sender, MouseButtonEventArgs e)
        {
            FrameworkElement loc = (FrameworkElement)sender;
            if (loc.IsMouseCaptured) loc.ReleaseMouseCapture();
        }

        /// <summary>按定位栏内的 Y 比例滚动视图（0=顶部，1=底部）。</summary>
        private void LocationScroll(FrameworkElement loc, MouseEventArgs e)
        {
            if (loc.ActualHeight <= 0) return;
            double ratio = e.GetPosition(loc).Y / loc.ActualHeight;
            _view.ScrollToFraction(ratio);
        }

        /// <summary>"下一个冲突"：取当前行 → VM 找下一个冲突段 → view 跳转并选中。</summary>
        private void NextConflict_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null || _vm.Rows.Count == 0) return;
            int next = _vm.GoNextConflict(_view.CurrentRowIndex);
            if (next >= 0) _view.GotoRow(next);
        }

        /// <summary>"上一个冲突"：取当前行 → VM 找上一个冲突段 → view 跳转并选中。</summary>
        private void PrevConflict_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null || _vm.Rows.Count == 0) return;
            int prev = _vm.GoPrevConflict(_view.CurrentRowIndex);
            if (prev >= 0) _view.GotoRow(prev);
        }

        /* 查找功能暂时屏蔽（恢复时取消注释本块 + XAML 菜单项 + 构造里 Ctrl+F 监听即可）
        /// <summary>"转到地址"（导航菜单）：弹小框输入地址，跳到对应行。</summary>
        private void GotoAddress_Click(object sender, RoutedEventArgs e)
        {
            OpenGotoAddress();
        }

        /// <summary>转到地址对话框：输入地址（0x… 或 十六进制），确定后跳转居中。</summary>
        private void OpenGotoAddress()
        {
            if (_view == null || _vm == null || _vm.Rows.Count == 0) return;

            TextBox input = new TextBox { FontFamily = new FontFamily("Consolas") };
            Window dlg = new Window
            {
                Title = AppInfo.Title + " — 转到地址",
                Width = 340, Height = 165,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0)),
                FontSize = 12
            };
            StackPanel sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock { Text = "输入地址（十六进制，如 0x1000 或 1000）：", Margin = new Thickness(0, 0, 0, 8) });
            sp.Children.Add(input);
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Button ok = new Button { Content = "转到", Padding = new Thickness(18, 5, 18, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            Button cancel = new Button { Content = "取消", Padding = new Thickness(18, 5, 18, 5), IsCancel = true };
            ok.Click += (s, e) => dlg.DialogResult = true;
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            sp.Children.Add(btns);
            dlg.Content = sp;
            input.Focus();

            if (dlg.ShowDialog() != true) return;
            int idx = _vm.GoToAddress(input.Text);
            if (idx >= 0) _view.GotoRow(idx);
        }
        */

        /// <summary>"重新比较"：返回文件选择窗，并保留上次的文件路径与基地址。</summary>
        private void Recompare_Click(object sender, RoutedEventArgs e)
        {
            FileSelectWindow fs = new FileSelectWindow(_paths, _baseAddrs);
            fs.Show();
            Close();
        }

        /// <summary>"退出"：关闭窗口（最后一个窗口关闭即退出应用）。</summary>
        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>"关于"：弹出卡片式关于对话框（无边框圆角卡片，主题配色与浮窗一致）。</summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            Brush ink = (Brush)FindResource("InkBrush");
            Brush muted = (Brush)FindResource("MutedBrush");
            Brush accent = (Brush)FindResource("AccentBrush");
            Brush cardBorder = (Brush)FindResource("CardBorderBrush");
            Brush headerBg = (Brush)FindResource("WindowBgBrush");
            Brush iconHover = (Brush)FindResource("IconHoverBrush");

            Window w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.Height,
                Width = 300,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            w.PreviewKeyDown += (s, ev) => { if (ev.Key == Key.Escape) w.Close(); }; // Esc 关闭（无边框窗无系统 IsCancel 兜底）

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
            TextBlock title = new TextBlock { Text = "关于", FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
            Border closeBtn = MakeIconButton("✕", muted, iconHover);
            closeBtn.MouseLeftButtonDown += (s, ev) => { ev.Handled = true; w.Close(); };
            Grid.SetColumn(title, 0); Grid.SetColumn(closeBtn, 1);
            hg.Children.Add(title); hg.Children.Add(closeBtn);
            header.Child = hg;
            Grid.SetRow(header, 0);

            // 内容：程序名 + 版本 + 简述 + 确定
            StackPanel body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 16) };
            body.Children.Add(new TextBlock { Text = "HexMerge", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
            body.Children.Add(new TextBlock { Text = "版本 " + AppInfo.Version, FontSize = 11, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(new TextBlock { Text = "嵌入式芯片 HEX 文件合并工具", FontSize = 11, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });
            Button ok = new Button { Content = "确定", Width = 110, Padding = new Thickness(0, 6, 0, 6), Style = (Style)FindResource("PrimaryButton"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Center };
            ok.Click += (s, ev) => w.Close();
            body.Children.Add(ok);

            Grid.SetRow(body, 1);
            cardGrid.Children.Add(header);
            cardGrid.Children.Add(body);
            card.Child = cardGrid;
            w.Content = card;
            w.ShowDialog();
        }

        /// <summary>合并前预览弹窗：展示"输出段列表 + 摘要（段数/大小/范围/间隙）"，确认后返回 true。</summary>
        private bool ShowMergePreview(CompareViewModel.MergePreview p)
        {
            string fillName = _vm.FillSelection == 1 ? "0xFF" : (_vm.FillSelection == 2 ? "0x00" : "不填充");
            string range = p.TotalBytes == 0 ? "（无数据）"
                : string.Format("0x{0:X8} ~ 0x{1:X8}", p.MinAddress, p.MaxAddress);
            // 间隙摘要：只说明填充/超阈值情况。"到 DFlash 的间隙"由下方提示块单独详列，摘要不重复。
            string gapText;
            if (p.GapCount == 0)
            {
                gapText = "无";
            }
            else
            {
                string main = string.Format("{0} 处 / {1} 字节", p.GapCount, p.GapBytes.ToString("N0"));
                if (_vm.FillSelection == 0)
                {
                    main += "（未启用填充）";
                }
                else if (p.SkippedGapBytes > 0)
                {
                    main += string.Format("（同区填 {0}；超阈值 {1} 未填）",
                        p.FillBytes.ToString("N0"), p.SkippedGapBytes.ToString("N0"));
                }
                else if (p.FillBytes > 0)
                {
                    main += string.Format("（同区按 {0} 填充）", fillName);
                }
                // 否则（无可填、无超阈值，例如间隙全是到 DFlash）不追加括号，交由下方提示块说明
                gapText = main;
            }

            // 段列表：段与间隙各自成行，统一"地址~地址 + 字节"格式对齐；间隙行末标 (间隙)
            List<string> segStrs = new List<string>();
            for (int i = 0; i < p.Segments.Count; i++)
            {
                CompareViewModel.MergeSegmentInfo s = p.Segments[i];
                segStrs.Add(string.Format("0x{0:X8} ~ 0x{1:X8}   {2} 字节", s.Start, s.End, s.Length.ToString("N0").PadLeft(12)));
                if (i + 1 < p.Segments.Count)
                {
                    uint gStart = s.End + 1;
                    uint gEnd = p.Segments[i + 1].Start - 1;
                    if (gEnd >= gStart)
                    {
                        long gLen = (long)(p.Segments[i + 1].Start - s.End - 1);
                        segStrs.Add(string.Format("0x{0:X8} ~ 0x{1:X8}   {2} 字节(间隙)", gStart, gEnd, gLen.ToString("N0").PadLeft(12)));
                    }
                }
            }

            Style primaryBtn = (Style)FindResource("PrimaryButton");
            Style secondaryBtn = (Style)FindResource("SecondaryButton");

            Window w = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 480, Height = 460,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            w.PreviewKeyDown += (s, ev) => { if (ev.Key == Key.Escape) { w.DialogResult = false; w.Close(); } };

            // 卡片：白底 + 浅边框 + 圆角 + 投影
            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
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
                Background = (Brush)FindResource("WindowBgBrush"),
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(14, 6, 6, 6)
            };
            header.MouseLeftButtonDown += (s, ev) => { try { if (ev.LeftButton == MouseButtonState.Pressed) w.DragMove(); } catch { } };
            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock dlgTitle = new TextBlock { Text = "合并预览", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush"), VerticalAlignment = VerticalAlignment.Center };
            Border closeBtn = MakeIconButton("✕", (Brush)FindResource("MutedBrush"), (Brush)FindResource("IconHoverBrush"));
            closeBtn.MouseLeftButtonDown += (s, ev) => { ev.Handled = true; w.DialogResult = false; w.Close(); };
            Grid.SetColumn(dlgTitle, 0); Grid.SetColumn(closeBtn, 1);
            hg.Children.Add(dlgTitle); hg.Children.Add(closeBtn);
            header.Child = hg;
            Grid.SetRow(header, 0);
            cardGrid.Children.Add(header);

            DockPanel root = new DockPanel { Margin = new Thickness(20, 14, 20, 16), LastChildFill = true };
            Grid.SetRow(root, 1);

            // 底部按钮
            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            DockPanel.SetDock(btns, Dock.Bottom);
            Button cancel = new Button { Content = "取消", Width = 90, Padding = new Thickness(0, 6, 0, 6), Margin = new Thickness(0, 0, 8, 0), Style = secondaryBtn };
            cancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };
            Button ok = new Button { Content = "确定合并", Width = 110, Padding = new Thickness(0, 6, 0, 6), Style = primaryBtn, IsDefault = true };
            ok.Click += (s, e) => w.DialogResult = true;
            btns.Children.Add(cancel);
            btns.Children.Add(ok);
            root.Children.Add(btns);

            // 主体：标题 + 摘要 + 段列表
            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "合并预览 — 确认后选择保存路径", FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 12) });

            Grid sum = new Grid();
            sum.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sum.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddSummaryRow(sum, 0, "段个数", p.SegmentCount.ToString());
            AddSummaryRow(sum, 1, "合并后大小", p.FinalBytes.ToString("N0") + " 字节");
            AddSummaryRow(sum, 2, "覆盖地址范围", range);
            AddSummaryRow(sum, 3, "间隙", gapText);
            body.Children.Add(sum);

            // 到 DFlash 的跨区间隙提示块：默认忽略（不填充），逐条列出地址范围
            if (p.DFlashGaps.Count > 0)
            {
                Border dflash = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xE7, 0xF0, 0xF7)), // 浅蓝底
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x6A, 0x9A, 0xC8)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 10, 0, 0)
                };
                StackPanel dsp = new StackPanel();
                dsp.Children.Add(new TextBlock
                {
                    Text = "已忽略到 DFlash 的区间（通往数据 Flash，默认不填充）：",
                    FontWeight = FontWeights.Bold
                });
                foreach (CompareViewModel.SkippedGapInfo g in p.DFlashGaps)
                {
                    dsp.Children.Add(new TextBlock
                    {
                        Text = string.Format("  0x{0:X8} ~ 0x{1:X8}   {2} 字节", g.Start, g.End, g.Length.ToString("N0")),
                        FontFamily = new FontFamily("Consolas"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                dflash.Child = dsp;
                body.Children.Add(dflash);
            }

            // 跳过间隙提示块：仅当存在超阈值、未填充的间隙时显示，逐条列出地址范围并说明原因
            if (p.SkippedGaps.Count > 0)
            {
                long thrMb = (long)(CompareViewModel.GapFillMaxBytes >> 20);
                Border warn = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF1, 0xD6)), // 浅黄底，醒目但不刺眼
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xC2, 0x40)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 10, 0, 0)
                };
                StackPanel wsp = new StackPanel();
                wsp.Children.Add(new TextBlock
                {
                    Text = string.Format("有 {0} 处间隙超过 {1}MB 阈值，未填充（避免输出膨胀）：", p.SkippedGaps.Count, thrMb),
                    FontWeight = FontWeights.Bold
                });
                foreach (CompareViewModel.SkippedGapInfo g in p.SkippedGaps)
                {
                    wsp.Children.Add(new TextBlock
                    {
                        Text = string.Format("  0x{0:X8} ~ 0x{1:X8}   {2} 字节", g.Start, g.End, g.Length.ToString("N0")),
                        FontFamily = new FontFamily("Consolas"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                warn.Child = wsp;
                body.Children.Add(warn);
            }

            body.Children.Add(new TextBlock { Text = string.Format("输出段列表（{0} 段，间隙已标出）：", p.SegmentCount), FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 6) });

            ListBox list = new ListBox { Height = 210, FontFamily = new FontFamily("Consolas") };
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto); // 间隙括号可能较长，允许横向滚动
            foreach (string ss in segStrs) list.Items.Add(ss);
            body.Children.Add(list);

            root.Children.Add(body);
            cardGrid.Children.Add(root);
            card.Child = cardGrid;
            w.Content = card;
            return w.ShowDialog() == true;
        }

        /// <summary>给摘要 Grid 加一行（标签 + 值）。</summary>
        private static void AddSummaryRow(Grid g, int row, string label, string value)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock lab = new TextBlock { Text = label + "：", Foreground = Brushes.Gray, Margin = new Thickness(0, 3, 0, 3) };
            TextBlock val = new TextBlock { Text = value, Margin = new Thickness(14, 3, 0, 3) };
            Grid.SetRow(lab, row); Grid.SetColumn(lab, 0);
            Grid.SetRow(val, row); Grid.SetColumn(val, 1);
            g.Children.Add(lab);
            g.Children.Add(val);
        }

        /// <summary>
        /// 预热下拉框：WPF 首次打开任意下拉（Popup）需一次性创建弹出窗口 HWND、加载模板、
        /// JIT 布局/定位代码——这就是"第一次点下拉框会卡一下"的根因。这里在窗口加载后于后台
        /// 提前打开再关闭一个屏幕外的 Popup，把这笔一次性开销提前付掉。
        /// </summary>
        private void WarmupDropDown()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                Popup warm = new Popup
                {
                    PlacementTarget = this,
                    Placement = PlacementMode.Absolute,
                    HorizontalOffset = -10000,
                    VerticalOffset = -10000,
                    Child = new Border()
                };
                warm.IsOpen = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    warm.IsOpen = false;
                    warm.Child = null;
                }));
            }));
        }

        /// <summary>定位栏：自绘，每行按状态显示同步颜色（差异黄绿/缺失灰），高效渲染。</summary>
        private class LocationBar : FrameworkElement
        {
            private CompareViewModel _vm;
            private HexCompareView _view;

            public void SetViewModel(CompareViewModel vm)
            {
                _vm = vm;
                InvalidateVisual();
            }

            public void SetView(HexCompareView view) { _view = view; }

            protected override void OnRender(DrawingContext dc)
            {
                double w = ActualWidth;
                double h = ActualHeight > 0 ? ActualHeight : 400;
                dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0, 0, w, h));

                // 鸟瞰：每行颜色以第一个文件（file 0）为准，与主视图第一列行背景一致
                if (_vm != null && _vm.Rows.Count > 0)
                {
                    int n = _vm.Rows.Count;
                    double scale = h / n;
                    // 合并连续同色段绘制：DrawRectangle 次数从行数降到颜色变化数（大文件显著减少）
                    int i = 0;
                    while (i < n)
                    {
                        Brush b = LocationBrushFor(HexCellPaint.ResolveRowBg(_vm.Rows[i], 0));
                        int j = i + 1;
                        while (j < n && LocationBrushFor(HexCellPaint.ResolveRowBg(_vm.Rows[j], 0)) == b) j++;
                        if (b != null)
                            dc.DrawRectangle(b, null, new Rect(3, i * scale, w - 6, (j - i) * scale));
                        i = j;
                    }
                }

                // 视口指示器（拇指）：标记当前可见区在文档中的位置与范围，滚动时实时跟随
                if (_view != null)
                {
                    _view.GetViewportFraction(out double topFrac, out double hFrac);
                    double thumbH = Math.Max(10, hFrac * h);
                    double thumbTop = topFrac * h;
                    if (thumbTop + thumbH > h) thumbTop = h - thumbH;
                    if (thumbTop < 0) thumbTop = 0;
                    dc.DrawRectangle(ThumbBrush, null, new Rect(1, thumbTop, w - 2, thumbH));
                }
            }

            /// <summary>第一个文件行背景 → 定位栏画笔（White 不画）。</summary>
            private static Brush LocationBrushFor(HexCellPaint.RowBg bg)
            {
                switch (bg)
                {
                    case HexCellPaint.RowBg.Deleted: return HexPalette.Deleted;
                    case HexCellPaint.RowBg.Conflict: return HexPalette.Conflict;
                    case HexCellPaint.RowBg.Chosen: return HexPalette.Chosen;
                    default: return null;
                }
            }
        }
    }
}

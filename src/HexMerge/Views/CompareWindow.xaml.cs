using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HexMerge.Models;
using HexMerge.ViewModels;

namespace HexMerge.Views
{
    /// <summary>
    /// 比较视图窗口：命令条 + 内存地图标尺（LocationBar，自绘）+ 自绘比较网格
    /// <see cref="HexCompareView"/> + 滚动条 + 右侧停靠的冲突检查器。
    /// 渲染层全部自绘（零 DataGrid），滚动时只重画可见行，彻底消除卡顿。
    /// 优先级/空隙填充改用命令条 ComboBox 直接绑定 VM，取代旧的动态菜单构建。
    /// </summary>
    public partial class CompareWindow : Window
    {
        private readonly CompareViewModel _vm;
        private readonly string[] _paths;       // 打开时的文件路径（供"重新比较"带回）
        private readonly uint[] _baseAddrs;     // 打开时的 DAT 基地址
        private HexCompareView _view;
        private LocationBar _locationBar;

        public CompareWindow(string[] paths, uint[] baseAddrs, MemoryImage[] images)
        {
            InitializeComponent();
            Title = AppInfo.Title; // 统一标题：HexMerge V{版本}
            _vm = new CompareViewModel();
            DataContext = _vm;
            _paths = paths;
            _baseAddrs = baseAddrs;
            _vm.Loaded += OnVmLoaded;            // 数据就绪后刷新布局
            BuildHost();                          // 创建控件（数据未就绪时渲染空白）
            _vm.Load(paths, baseAddrs, images);   // 后台比对（解析结果复用，避免重复解析）
            _vm.ConfirmMergePreview = ShowMergePreview;
            Loaded += (s, e) => { _locationBar?.InvalidateVisual(); WarmupDropDown(); };
            Closed += (s, e) => { _vm.Loaded -= OnVmLoaded; }; // 关窗后退订，防后台完成时操作已释放控件
        }

        /// <summary>VM 数据加载完成（后台解析后）：触发视图重新布局/渲染。
        /// 命令条 ComboBox 与检查器均绑定 VM，数据变化自动刷新，无需手工构建。</summary>
        private void OnVmLoaded()
        {
            _view?.InvalidateTextCache(); // 数据就绪：失效首次（空数据）建的文本缓存，触发重建段信息/文件名
            _view?.InvalidateMeasure();   // 重跑 ArrangeOverride（按实际 FileCount 铺字节宽）+ OnRender
            _locationBar?.InvalidateVisual();
            // 下拉用 OneWay 绑定：PriorityOptions 是此刻才异步填好的，而 PrioritySelection 值未变过，
            // OneWay 不会重推 → 若不显式回选，下拉会空白。这里在数据就绪后按 VM 当前值回选。
            if (PriorityCombo != null) PriorityCombo.SelectedIndex = _vm.PrioritySelection;
            if (FillCombo != null) FillCombo.SelectedIndex = _vm.FillSelection;
        }

        /// <summary>命令条"检查器"：切换右侧停靠面板显隐。</summary>
        private void ToggleInspector_Click(object sender, RoutedEventArgs e)
        {
            Inspector.Visibility = Inspector.Visibility == Visibility.Visible
                ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>优先级下拉：用 SelectionChanged 写回 VM（SelectedIndex 仅 OneWay 绑定）。
        /// 因 PriorityOptions 是后台比对完成后才异步填充；若用默认 TwoWay，列表由空变满的瞬间
        /// WPF 会把 SelectedIndex=-1 写回 VM，静默破坏合并仲裁。OneWay + 事件回写可彻底避免。</summary>
        private void PriorityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm != null && PriorityCombo.SelectedIndex >= 0)
                _vm.PrioritySelection = PriorityCombo.SelectedIndex;
        }

        /// <summary>空隙填充下拉：同上，SelectionChanged 写回 VM，避免异步/重建时的假写回。</summary>
        private void FillCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm != null && FillCombo.SelectedIndex >= 0)
                _vm.FillSelection = FillCombo.SelectedIndex;
        }

        /// <summary>小图标按钮（对话框关闭）：Segoe MDL2 ChromeClose(0xE711)，运行时构造避免源码私用区字符。</summary>
        private static Border MakeIconButton(Brush fg, Brush hoverBg)
        {
            Border b = new Border
            {
                Width = 28, Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand
            };
            b.Child = new TextBlock
            {
                Text = char.ConvertFromUtf32(0xE711),
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Foreground = fg, FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            b.MouseEnter += (s, e) => b.Background = hoverBg;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            return b;
        }

        /// <summary>构建主区：内存地图标尺(72) + 自绘比较视图(1*) + 垂直滚动条(14)。</summary>
        private void BuildHost()
        {
            Grid host = CompareHost;
            host.ColumnDefinitions.Clear();
            host.Children.Clear();

            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });

            // 内存地图标尺（鸟瞰 + 地址刻度 + 冲突段带，自绘）
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
            _view.Scrolled += (s, e) => _locationBar.InvalidateVisual(); // 滚动/缩放时刷新标尺视口指示器
            Grid.SetColumn(_view, 1);
            host.Children.Add(_view);
            _locationBar.SetView(_view);

            // 滚动条（由 view 自管偏移与几何）
            ScrollBar sb = new ScrollBar { Orientation = Orientation.Vertical };
            _view.AttachScrollBar(sb);
            Grid.SetColumn(sb, 2);
            host.Children.Add(sb);
        }

        /// <summary>标尺：按住可像滚动条一样拖动。按下捕获鼠标并定位，移动持续跟随，抬起释放。</summary>
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

        /// <summary>按标尺内的 Y 比例滚动视图（0=顶部，1=底部）。</summary>
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

        /// <summary>"重新比较"：返回文件选择窗，并保留上次的文件路径与基地址。</summary>
        private void Recompare_Click(object sender, RoutedEventArgs e)
        {
            FileSelectWindow fs = new FileSelectWindow(_paths, _baseAddrs);
            fs.Show();
            Close();
        }

        /// <summary>"关于"：弹出卡片式关于对话框（无边框圆角卡片，主题配色一致）。</summary>
        private void About_Click(object sender, RoutedEventArgs e)
        {
            Brush ink = (Brush)FindResource("InkBrush");
            Brush muted = (Brush)FindResource("MutedBrush");
            Brush cardBorder = (Brush)FindResource("CardBorderBrush");
            Brush headerBg = (Brush)FindResource("WindowBgBrush");
            Brush iconHover = (Brush)FindResource("IconHoverBrush");

            Window w = new Window
            {
                Style = new Style(typeof(Window)), // 退出 ModernWpf 隐式窗口样式，避免卡片外再套一层标题栏+关闭按钮
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.Height,
                Width = 320,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            w.PreviewKeyDown += (s, ev) => { if (ev.Key == Key.Escape) w.Close(); };

            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 3, Opacity = 0.20, Color = Color.FromRgb(0, 0, 0) }
            };

            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border header = new Border
            {
                Background = headerBg,
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Padding = new Thickness(16, 8, 8, 8)
            };
            header.MouseLeftButtonDown += (s, ev) => { try { if (ev.LeftButton == MouseButtonState.Pressed) w.DragMove(); } catch { } };
            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock title = new TextBlock { Text = "关于", FontWeight = FontWeights.SemiBold, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
            Border closeBtn = MakeIconButton(muted, iconHover);
            closeBtn.MouseLeftButtonDown += (s, ev) => { ev.Handled = true; w.Close(); };
            Grid.SetColumn(title, 0); Grid.SetColumn(closeBtn, 1);
            hg.Children.Add(title); hg.Children.Add(closeBtn);
            header.Child = hg;
            Grid.SetRow(header, 0);

            StackPanel body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 18) };
            body.Children.Add(new TextBlock { Text = "HexMerge", FontSize = 20, FontWeight = FontWeights.Bold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) });
            body.Children.Add(new TextBlock { Text = "版本 " + AppInfo.Version, FontSize = 11, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
            body.Children.Add(new TextBlock { Text = "嵌入式芯片 HEX 文件合并工具", FontSize = 12, Foreground = muted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) });
            Button ok = new Button { Content = "确定", Width = 120, Padding = new Thickness(0, 7, 0, 7), Style = (Style)FindResource("PrimaryButton"), IsDefault = true, HorizontalAlignment = HorizontalAlignment.Center };
            ok.Click += (s, ev) => w.Close();
            body.Children.Add(ok);

            Grid.SetRow(body, 1);
            cardGrid.Children.Add(header);
            cardGrid.Children.Add(body);
            card.Child = cardGrid;
            w.Content = card;
            using (ModalScrim.Over(this)) { w.ShowDialog(); }
        }

        /// <summary>合并前预览弹窗：展示"输出段列表 + 摘要（段数/大小/范围/间隙）"，确认后返回 true。</summary>
        private bool ShowMergePreview(CompareViewModel.MergePreview p)
        {
            string fillName = _vm.FillSelection == 1 ? "0xFF" : (_vm.FillSelection == 2 ? "0x00" : "不填充");
            string range = p.TotalBytes == 0 ? "（无数据）"
                : string.Format("0x{0:X8} ~ 0x{1:X8}", p.MinAddress, p.MaxAddress);
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
                gapText = main;
            }

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
                Style = new Style(typeof(Window)), // 退出 ModernWpf 隐式窗口样式，避免卡片外再套一层标题栏+关闭按钮
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                ResizeMode = ResizeMode.NoResize,
                Width = 500, Height = 480,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            w.PreviewKeyDown += (s, ev) => { if (ev.Key == Key.Escape) { w.DialogResult = false; w.Close(); } };

            Border card = new Border
            {
                Background = Brushes.White,
                BorderBrush = (Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 3, Opacity = 0.20, Color = Color.FromRgb(0, 0, 0) }
            };
            Grid cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border header = new Border
            {
                Background = (Brush)FindResource("WindowBgBrush"),
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Padding = new Thickness(16, 8, 8, 8)
            };
            header.MouseLeftButtonDown += (s, ev) => { try { if (ev.LeftButton == MouseButtonState.Pressed) w.DragMove(); } catch { } };
            Grid hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock dlgTitle = new TextBlock { Text = "合并预览", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush"), VerticalAlignment = VerticalAlignment.Center };
            Border closeBtn = MakeIconButton((Brush)FindResource("MutedBrush"), (Brush)FindResource("IconHoverBrush"));
            closeBtn.MouseLeftButtonDown += (s, ev) => { ev.Handled = true; w.DialogResult = false; w.Close(); };
            Grid.SetColumn(dlgTitle, 0); Grid.SetColumn(closeBtn, 1);
            hg.Children.Add(dlgTitle); hg.Children.Add(closeBtn);
            header.Child = hg;
            Grid.SetRow(header, 0);
            cardGrid.Children.Add(header);

            DockPanel root = new DockPanel { Margin = new Thickness(22, 16, 22, 18), LastChildFill = true };
            Grid.SetRow(root, 1);

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            DockPanel.SetDock(btns, Dock.Bottom);
            Button cancel = new Button { Content = "取消", Width = 92, Padding = new Thickness(0, 7, 0, 7), Margin = new Thickness(0, 0, 10, 0), Style = secondaryBtn };
            cancel.Click += (s, e) => { w.DialogResult = false; w.Close(); };
            Button ok = new Button { Content = "确定合并", Width = 116, Padding = new Thickness(0, 7, 0, 7), Style = primaryBtn, IsDefault = true };
            ok.Click += (s, e) => w.DialogResult = true;
            btns.Children.Add(cancel);
            btns.Children.Add(ok);
            root.Children.Add(btns);

            StackPanel body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "确认后选择保存路径", Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 0, 0, 14) });

            Grid sum = new Grid();
            sum.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sum.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            AddSummaryRow(sum, 0, "段个数", p.SegmentCount.ToString());
            AddSummaryRow(sum, 1, "合并后大小", p.FinalBytes.ToString("N0") + " 字节");
            AddSummaryRow(sum, 2, "覆盖地址范围", range);
            AddSummaryRow(sum, 3, "间隙", gapText);
            body.Children.Add(sum);

            if (p.DFlashGaps.Count > 0)
            {
                Border dflash = new Border
                {
                    Background = (Brush)FindResource("InfoSoftBrush"),
                    BorderBrush = (Brush)FindResource("InfoSoftBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 12, 0, 0)
                };
                StackPanel dsp = new StackPanel();
                dsp.Children.Add(new TextBlock
                {
                    Text = "已忽略到 DFlash 的区间（通往数据 Flash，默认不填充）：",
                    FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush")
                });
                foreach (CompareViewModel.SkippedGapInfo g in p.DFlashGaps)
                {
                    dsp.Children.Add(new TextBlock
                    {
                        Text = string.Format("  0x{0:X8} ~ 0x{1:X8}   {2} 字节", g.Start, g.End, g.Length.ToString("N0")),
                        FontFamily = (FontFamily)FindResource("MonoFont"),
                        Foreground = (Brush)FindResource("InkBrush"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                dflash.Child = dsp;
                body.Children.Add(dflash);
            }

            if (p.SkippedGaps.Count > 0)
            {
                long thrMb = (long)(CompareViewModel.GapFillMaxBytes >> 20);
                Border warn = new Border
                {
                    Background = (Brush)FindResource("WarningSoftBrush"),
                    BorderBrush = (Brush)FindResource("WarningBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 12, 0, 0)
                };
                StackPanel wsp = new StackPanel();
                wsp.Children.Add(new TextBlock
                {
                    Text = string.Format("有 {0} 处间隙超过 {1}MB 阈值，未填充（避免输出膨胀）：", p.SkippedGaps.Count, thrMb),
                    FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush")
                });
                foreach (CompareViewModel.SkippedGapInfo g in p.SkippedGaps)
                {
                    wsp.Children.Add(new TextBlock
                    {
                        Text = string.Format("  0x{0:X8} ~ 0x{1:X8}   {2} 字节", g.Start, g.End, g.Length.ToString("N0")),
                        FontFamily = (FontFamily)FindResource("MonoFont"),
                        Foreground = (Brush)FindResource("InkBrush"),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }
                warn.Child = wsp;
                body.Children.Add(warn);
            }

            body.Children.Add(new TextBlock { Text = string.Format("输出段列表（{0} 段，间隙已标出）", p.SegmentCount), Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 16, 0, 7) });

            Border listCard = new Border
            {
                BorderBrush = (Brush)FindResource("HairlineBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true
            };
            ListBox list = new ListBox { Height = 200, FontFamily = (FontFamily)FindResource("MonoFont"), BorderThickness = new Thickness(0), Background = (Brush)FindResource("SurfaceAltBrush") };
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            foreach (string ss in segStrs) list.Items.Add(ss);
            listCard.Child = list;
            body.Children.Add(listCard);

            // 主体可滚动：DFlash / 超阈值间隙提示块数量不定，避免内容超出固定高度被裁切
            ScrollViewer bodyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body
            };
            root.Children.Add(bodyScroll);
            cardGrid.Children.Add(root);
            card.Child = cardGrid;
            w.Content = card;
            using (ModalScrim.Over(this)) { return w.ShowDialog() == true; }
        }

        /// <summary>给摘要 Grid 加一行（标签 + 值）。</summary>
        private void AddSummaryRow(Grid g, int row, string label, string value)
        {
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock lab = new TextBlock { Text = label, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 4) };
            TextBlock val = new TextBlock { Text = value, Foreground = (Brush)FindResource("InkBrush"), Margin = new Thickness(16, 4, 0, 4) };
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

        /// <summary>
        /// 内存地图标尺（自绘，签名元素）：把整份文档压缩成一条竖直标尺——
        /// 右侧色带按行状态显示同步颜色（冲突黄 / 缺失灰 / 选定红）；
        /// 左侧按等分位置标出真实 hex 地址刻度；叠加视口指示器随滚动实时跟随。
        /// </summary>
        private class LocationBar : FrameworkElement
        {
            private CompareViewModel _vm;
            private HexCompareView _view;

            // 标尺专属画笔（冻结复用）
            private static readonly Brush RulerBg = Freeze(new SolidColorBrush(Color.FromRgb(0xE7, 0xEC, 0xEF)));   // 下沉底
            private static readonly Brush AxisBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC7, 0xCF, 0xD8))); // 刻度轴
            private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x5A, 0x66, 0x73)));// 地址标签
            private static readonly Brush ThumbBrush = Freeze(new SolidColorBrush(Color.FromArgb(64, 0x0E, 0x7C, 0x86))); // 视口指示器（青，半透明）
            private static readonly Brush ThumbBorder = Freeze(new SolidColorBrush(Color.FromArgb(150, 0x0E, 0x7C, 0x86)));
            private static readonly Typeface RulerFace = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            private static Brush Freeze(Brush b) { b.Freeze(); return b; }

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
                dc.DrawRectangle(RulerBg, null, new Rect(0, 0, w, h));

                double bandW = 12;
                double bandX = w - bandW - 1;   // 右侧色带
                double axisX = bandX - 4;       // 刻度轴（色带与标签之间）

                int n = _vm != null ? _vm.Rows.Count : 0;
                if (n > 0)
                {
                    double scale = h / n;
                    // 色带：合并连续同色段绘制，DrawRectangle 次数从行数降到颜色变化数
                    int i = 0;
                    while (i < n)
                    {
                        Brush b = LocationBrushFor(HexCellPaint.ResolveRowBg(_vm.Rows[i], 0));
                        int j = i + 1;
                        while (j < n && LocationBrushFor(HexCellPaint.ResolveRowBg(_vm.Rows[j], 0)) == b) j++;
                        if (b != null)
                            dc.DrawRectangle(b, null, new Rect(bandX, i * scale, bandW, Math.Max(1, (j - i) * scale)));
                        i = j;
                    }

                    // 刻度轴
                    dc.DrawRectangle(AxisBrush, null, new Rect(axisX, 0, 1, h));

                    // 地址刻度标签：按高度等分，标出该位置行的真实地址
                    double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                    if (dpi <= 0) dpi = 1.0;
                    int divs = Math.Max(2, Math.Min(10, (int)(h / 68)));
                    for (int d = 0; d <= divs; d++)
                    {
                        double frac = (double)d / divs;
                        int rowIdx = (int)Math.Round(frac * (n - 1));
                        if (rowIdx < 0) rowIdx = 0; else if (rowIdx > n - 1) rowIdx = n - 1;
                        double y = frac * h;

                        // 刻度短线
                        dc.DrawRectangle(AxisBrush, null, new Rect(axisX - 3, Clamp(y, 0, h - 1), 3, 1));

                        FormattedText ft = new FormattedText(
                            _vm.Rows[rowIdx].Address.ToString("X8"),
                            CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            RulerFace, 9.5, LabelBrush, dpi);
                        double ty = y - ft.Height / 2;
                        if (d == 0) ty = 1;                        // 顶部标签贴顶
                        if (d == divs) ty = h - ft.Height - 1;     // 底部标签贴底
                        dc.DrawText(ft, new Point(4, ty));
                    }
                }

                // 视口指示器（拇指）：标记当前可见区在文档中的位置与范围
                if (_view != null)
                {
                    _view.GetViewportFraction(out double topFrac, out double hFrac);
                    double thumbH = Math.Max(12, hFrac * h);
                    double thumbTop = topFrac * h;
                    if (thumbTop + thumbH > h) thumbTop = h - thumbH;
                    if (thumbTop < 0) thumbTop = 0;
                    dc.DrawRectangle(ThumbBrush, new Pen(ThumbBorder, 1), new Rect(1.5, thumbTop + 0.5, w - 3, thumbH - 1));
                }
            }

            private static double Clamp(double v, double lo, double hi) { return v < lo ? lo : (v > hi ? hi : v); }

            /// <summary>第一个文件行背景 → 标尺色带画笔（White 不画）。</summary>
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

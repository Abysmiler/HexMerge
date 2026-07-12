using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HexMerge.Views
{
    /// <summary>
    /// 状态栏文本 → 语义画笔：命中「通过/成功」判为成功（绿），
    /// 「失败/异常/无法/取消」判为警示（琥珀），其余为中性次色。
    /// 颜色取自 Theme.xaml，改主题即同步。
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string ?? "";
            string key = "MutedBrush";
            if (s.Contains("通过") || s.Contains("成功")) key = "SuccessBrush";
            else if (s.Contains("失败") || s.Contains("异常") || s.Contains("无法") || s.Contains("取消")) key = "WarningBrush";
            return (Application.Current.TryFindResource(key) as Brush) ?? Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    /// <summary>状态栏文本 → Segoe MDL2 状态字形：成功=Completed / 警示=Warning / 其余=Info。</summary>
    public class StatusToGlyphConverter : IValueConverter
    {
        // Segoe MDL2 Assets 码位（运行时构造，避免源码里出现不可见私用区字符）
        private static readonly string GlyphCompleted = char.ConvertFromUtf32(0xE930);
        private static readonly string GlyphWarning = char.ConvertFromUtf32(0xE7BA);
        private static readonly string GlyphInfo = char.ConvertFromUtf32(0xE946);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value as string ?? "";
            if (s.Contains("通过") || s.Contains("成功")) return GlyphCompleted;
            if (s.Contains("失败") || s.Contains("异常") || s.Contains("无法") || s.Contains("取消")) return GlyphWarning;
            return GlyphInfo;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }
}

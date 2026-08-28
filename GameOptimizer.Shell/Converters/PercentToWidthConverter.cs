using System;
using System.Globalization;
using System.Windows.Data;

namespace GameOptimizer.Shell.Converters;

/// <summary>
/// 将百分比值转换为进度条宽度（基于父容器宽度的比例）
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    /// <summary>
    /// 最大宽度（像素），默认为100
    /// </summary>
    public double MaxWidth { get; set; } = 100;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            var maxWidth = MaxWidth;
            if (parameter is double paramWidth)
            {
                maxWidth = paramWidth;
            }
            else if (parameter is string paramStr && double.TryParse(paramStr, out var parsed))
            {
                maxWidth = parsed;
            }
            
            return Math.Clamp(percent / 100.0 * maxWidth, 0, maxWidth);
        }
        return 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

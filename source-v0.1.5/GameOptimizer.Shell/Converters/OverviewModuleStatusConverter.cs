using System;
using System.Globalization;
using System.Windows.Data;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Converters;

public sealed class OverviewModuleStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not OptimizationModuleDefinition module)
        {
            return "模块未加载";
        }

        var total = module.Toggles.Count;
        if (total == 0)
        {
            return "暂无可用优化项";
        }

        var applied = module.AppliedCount;
        var classification = applied switch
        {
            0 => "未应用",
            _ when applied == total => "全部已应用",
            _ => "部分已应用"
        };
        return $"{classification} {applied}/{total}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

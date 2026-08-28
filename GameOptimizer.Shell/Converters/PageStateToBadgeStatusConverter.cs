using System;
using System.Globalization;
using System.Windows.Data;
using GameOptimizer.Shell.Controls;

namespace GameOptimizer.Shell.Converters;

public sealed class PageStateToBadgeStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() switch
        {
            "Ready" or "CompensatedVerified" => BadgeStatus.Success,
            "Partial" or "RebootRequired" or "InterruptedManual" => BadgeStatus.Warning,
            "Error" => BadgeStatus.Error,
            "Unauthorized" => BadgeStatus.Unauthorized,
            "Recovering" => BadgeStatus.Recovering,
            "Loading" => BadgeStatus.Loading,
            _ => BadgeStatus.Info
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

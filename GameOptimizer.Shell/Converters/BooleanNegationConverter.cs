using System;
using System.Globalization;
using System.Windows.Data;

namespace GameOptimizer.Shell.Converters;

public class BooleanNegationConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is bool flag)
		{
			return !flag;
		}
		return true;
	}

	public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is bool flag)
		{
			return !flag;
		}
		return Binding.DoNothing;
	}
}

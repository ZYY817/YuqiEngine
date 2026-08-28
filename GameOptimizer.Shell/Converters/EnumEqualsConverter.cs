using System;
using System.Globalization;
using System.Windows.Data;

namespace GameOptimizer.Shell.Converters;

public class EnumEqualsConverter : IValueConverter
{
	public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value == null || parameter == null)
		{
			return false;
		}
		return string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (parameter == null || !(value is bool flag) || !flag)
		{
			return Binding.DoNothing;
		}

		var parameterText = parameter.ToString();
		if (string.IsNullOrWhiteSpace(parameterText))
		{
			return Binding.DoNothing;
		}

		if (targetType.IsEnum && Enum.TryParse(targetType, parameterText, ignoreCase: true, out object? result))
		{
			return result;
		}
		return Binding.DoNothing;
	}
}

using System;
using Microsoft.Win32;

namespace GameOptimizer.Core.Optimization.Operations;

internal static class InputRegistryValueHelper
{
    public static string? ReadStringValue(RegistryKey? key, string valueName)
    {
        return key?.GetValue(valueName)?.ToString();
    }

    public static int? ReadDwordValue(RegistryKey? key, string valueName)
    {
        var value = key?.GetValue(valueName);
        if (value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return null;
        }
    }

    public static bool MatchesStringValue(RegistryKey? key, string valueName, string? expectedValue)
    {
        return string.Equals(ReadStringValue(key, valueName), expectedValue, StringComparison.Ordinal);
    }

    public static bool MatchesDwordValue(RegistryKey? key, string valueName, int? expectedValue)
    {
        return ReadDwordValue(key, valueName) == expectedValue;
    }

    public static bool WriteStringValue(RegistryKey key, string valueName, string value)
    {
        key.SetValue(valueName, value, RegistryValueKind.String);
        return string.Equals(ReadStringValue(key, valueName), value, StringComparison.Ordinal);
    }

    public static bool RestoreStringValue(RegistryKey key, string valueName, string? originalValue)
    {
        if (originalValue is null)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return key.GetValue(valueName) is null;
        }

        key.SetValue(valueName, originalValue, RegistryValueKind.String);
        return string.Equals(ReadStringValue(key, valueName), originalValue, StringComparison.Ordinal);
    }

    public static bool WriteDwordValue(RegistryKey key, string valueName, int value)
    {
        key.SetValue(valueName, value, RegistryValueKind.DWord);
        var currentValue = ReadDwordValue(key, valueName);
        return currentValue.HasValue && currentValue.Value == value;
    }

    public static bool RestoreDwordValue(RegistryKey key, string valueName, int? originalValue)
    {
        if (!originalValue.HasValue)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return key.GetValue(valueName) is null;
        }

        key.SetValue(valueName, originalValue.Value, RegistryValueKind.DWord);
        var currentValue = ReadDwordValue(key, valueName);
        return currentValue.HasValue && currentValue.Value == originalValue.Value;
    }
}
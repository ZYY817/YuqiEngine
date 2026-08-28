// File: GameOptimizer.Shell/Helpers/MessageBoxHelper.cs
using System.Windows;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Constants;
using GameOptimizer.Shell.Views;

namespace GameOptimizer.Shell.Helpers;

public static class MessageBoxHelper
{
    private static LocalizationService L => LocalizationService.Instance;
    private static Window? Owner => Application.Current.MainWindow;

    public static void Info(string messageKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        InfoDialog.Show(Owner, L.Get("Common.Info"), message, IconGlyphs.Lightbulb);
    }

    public static void Info(string messageKey, string titleKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        InfoDialog.Show(Owner, L.Get(titleKey), message, IconGlyphs.Lightbulb);
    }

    public static void Success(string messageKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        InfoDialog.Show(Owner, L.Get("Common.Success"), message, IconGlyphs.Check);
    }

    public static void Error(string messageKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        InfoDialog.Show(Owner, L.Get("Common.Error"), message, IconGlyphs.Warning);
    }

    public static void Warning(string messageKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        InfoDialog.Show(Owner, L.Get("Common.Warning"), message, IconGlyphs.Warning);
    }

    public static bool Confirm(string messageKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        return ConfirmDialog.Show(Owner, L.Get("Common.Confirm"), message, IconGlyphs.Question);
    }

    public static bool Confirm(string messageKey, string titleKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        return ConfirmDialog.Show(Owner, L.Get(titleKey), message, IconGlyphs.Question);
    }

    public static bool ConfirmWarning(string messageKey, params object[] args)
    {
        var message = L.Get(messageKey, args);
        return ConfirmDialog.Show(Owner, L.Get("Common.Warning"), message, IconGlyphs.Warning);
    }
}

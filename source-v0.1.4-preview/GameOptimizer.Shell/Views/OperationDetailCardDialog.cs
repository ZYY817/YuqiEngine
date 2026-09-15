using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Views;

public sealed class OperationDetailCardDialog : Window
{
    private readonly OperationDetailCardModel _model;

    private OperationDetailCardDialog(OperationDetailCardModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        BuildUi();
    }

    public static void Show(Window? owner, OperationDetailCardModel model)
    {
        var dialog = new OperationDetailCardDialog(model);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }

    private void BuildUi()
    {
        Title = L("优化详情", "Optimization Details");
        Width = 940;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = 760;
        MinHeight = 560;

        var root = new Grid
        {
            Margin = new Thickness(16)
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = _model.Title,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var bodyBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Text = BuildContent(_model)
        };
        Grid.SetRow(bodyBox, 1);
        root.Children.Add(bodyBox);

        var footer = new DockPanel
        {
            Margin = new Thickness(0, 12, 0, 0)
        };

        var closeButton = new Button
        {
            Content = L("关闭", "Close"),
            MinWidth = 96,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        closeButton.Click += (_, _) => Close();
        DockPanel.SetDock(closeButton, Dock.Right);
        footer.Children.Add(closeButton);

        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    private static string BuildContent(OperationDetailCardModel model)
    {
        var lines = new List<string>
        {
            $"[{L("基础信息", "Basics")}]",
            $"{L("标题", "Title")}: {Normalize(model.Title)}",
            $"{L("描述", "Description")}: {Normalize(model.Description)}",
            $"{L("类型", "Type")}: {Normalize(model.TypeLabel)}",
            $"{L("风险", "Risk")}: {Normalize(model.RiskLabel)}",
            $"{L("完整度", "Completeness")}: {Normalize(model.CompletenessSummary)}",
            string.Empty,
            $"[{L("变更说明", "Change Overview")}]",
            model.WhatChanged,
            string.Empty,
            model.WhereChanged,
            string.Empty,
            model.BeforeAfter,
            string.Empty,
            model.ImpactAndConsequence,
            string.Empty,
            model.RollbackAndVerify,
            string.Empty,
            $"[{L("命令", "Commands")}]",
            $"{L("应用", "Apply")}: {Normalize(model.ApplyCommand, "(none)")}",
            $"{L("校验", "Verify")}: {Normalize(model.VerifyCommand, "(none)")}",
            $"{L("回滚", "Rollback")}: {Normalize(model.RollbackCommand, "(none)")}",
            $"{L("默认恢复", "Default restore")}: {Normalize(model.DefaultRestoreCommand, "(none)")}",
            string.Empty,
            $"[{L("安全提示", "Safety")}]",
            $"{L("需管理员", "Requires admin")}: {(model.RequiresAdmin ? "Yes" : "No")}",
            $"{L("需二次确认", "Requires confirmation")}: {(model.RequiresExplicitConfirmation ? "Yes" : "No")}",
            $"{L("建议还原点", "Restore point recommended")}: {(model.RequiresRestorePoint ? "Yes" : "No")}",
            $"{L("影响手感", "Affects interaction feel")}: {(model.AffectsUserFeeling ? "Yes" : "No")}",
            $"{L("需要重启", "Requires reboot")}: {(model.RequiresReboot ? "Yes" : "No")}"
        };

        if (model.DisclosureFields is { Count: > 0 })
        {
            lines.Add(string.Empty);
            lines.Add($"[{L("字段公开", "Disclosure Fields")}]");
            foreach (var field in model.DisclosureFields)
            {
                lines.Add($"{Normalize(field.Label)}: {Normalize(field.Value)}");
            }
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    private static string Normalize(string? value, string fallback = "")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string L(string zhCn, string enUs)
    {
        return LocalizationService.Instance.CurrentLanguage == "en-US" ? enUs : zhCn;
    }
}

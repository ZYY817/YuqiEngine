using System;
using System.Collections.Generic;
using System.Linq;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public static class OperationDetailCardFactory
{
    public static OperationDetailCompletenessModel GetCompleteness(OptimizationToggleModel toggle)
    {
        ArgumentNullException.ThrowIfNull(toggle);

        var checks = BuildCompletenessChecks(toggle);
        var total = checks.Count;
        var available = checks.Count(item => item.IsAvailable);
        var level = ResolveLevel(available, total);
        var summary = $"公开字段完整度：{available}/{total}（{level}）";

        var missing = checks
            .Where(item => !item.IsAvailable)
            .Select(item => $"- {item.Label}")
            .ToArray();

        var missingItems = missing.Length == 0
            ? "当前优化项关键字段已齐全。"
            : string.Join(Environment.NewLine, missing);

        return new OperationDetailCompletenessModel(level, summary, missingItems);
    }

    public static OperationDetailCardModel Create(OptimizationToggleModel toggle, string? moduleTitle = null)
    {
        ArgumentNullException.ThrowIfNull(toggle);

        var completeness = GetCompleteness(toggle);
        var normalizedModuleTitle = Normalize(moduleTitle, "未分类模块");
        var operationTitle = Normalize(toggle.Title, "未命名优化项");
        var description = Normalize(toggle.Description, "该优化项暂无详细描述。");
        var typeLabel = ResolveTypeLabel(toggle);
        var riskLabel = Normalize(toggle.RiskLevelText, toggle.RiskLevel.ToString());

        var registryFullPath = BuildRegistryFullPath(toggle);
        var applyCommand = BuildApplyCommand(toggle, registryFullPath);
        var verifyCommand = BuildVerifyCommand(toggle, registryFullPath);
        var rollbackCommand = BuildRollbackCommand(toggle, registryFullPath);
        var commandGuide = BuildCommandGuide(toggle, applyCommand, verifyCommand, rollbackCommand);
        var defaultRestoreCommand = rollbackCommand;
        var emergencyRecoveryGuide = BuildEmergencyRecoveryGuide(toggle, rollbackCommand);

        var beforeValue = Normalize(toggle.DefaultValue, "(未提供)");
        var currentValue = Normalize(toggle.RuntimeStatusDetails ?? toggle.ValuePreview, "(未采集)");
        var targetValue = Normalize(toggle.TargetValue ?? toggle.OptimizedValue, "(未提供)");
        var windowsDefault = Normalize(toggle.DefaultValue, "(未提供)");
        var rollbackValue = windowsDefault;

        var whatChanged = $"模块：{normalizedModuleTitle}{Environment.NewLine}操作：{Normalize(toggle.OperationId, "(none)")}";
        var whereChanged = BuildWhereChanged(toggle, registryFullPath);
        var beforeAfter = BuildBeforeAfter(beforeValue, currentValue, targetValue, windowsDefault, rollbackValue);
        var impactAndConsequence = BuildImpact(toggle);
        var rollbackAndVerify = BuildRollbackAndVerify(verifyCommand, rollbackCommand, emergencyRecoveryGuide);
        var fullDisclosure = string.Join(
            Environment.NewLine + Environment.NewLine,
            whatChanged,
            whereChanged,
            beforeAfter,
            impactAndConsequence,
            rollbackAndVerify);

        var disclosureFields = BuildDisclosureFields(
            toggle,
            normalizedModuleTitle,
            registryFullPath,
            beforeValue,
            currentValue,
            targetValue,
            windowsDefault,
            rollbackValue);

        return new OperationDetailCardModel(
            Title: operationTitle,
            Description: description,
            TypeLabel: typeLabel,
            RiskLabel: riskLabel,
            CompletenessLevel: completeness.Level,
            CompletenessSummary: completeness.Summary,
            CompletenessMissingItems: completeness.MissingItems,
            RequiresReboot: toggle.RequiresReboot,
            IsHardwareIncompatible: toggle.IsHardwareIncompatible,
            HardwareIncompatibleHint: TextDisplayNormalizer.NormalizeNullable(toggle.HardwareIncompatibleHint),
            WhatChanged: whatChanged,
            WhereChanged: whereChanged,
            BeforeAfter: beforeAfter,
            ImpactAndConsequence: impactAndConsequence,
            RollbackAndVerify: rollbackAndVerify,
            ManualPath: TextDisplayNormalizer.NormalizeNullable(toggle.ManualPath),
            RegistryFullPath: registryFullPath,
            ApplyCommand: applyCommand,
            BeforeProbeCommand: verifyCommand,
            VerifyCommand: verifyCommand,
            RollbackCommand: rollbackCommand,
            DefaultRestoreCommand: defaultRestoreCommand,
            EmergencyRecoveryGuide: emergencyRecoveryGuide,
            CommandGuide: commandGuide,
            FullDisclosure: fullDisclosure,
            RequiresAdmin: toggle.RequiresAdmin,
            RequiresExplicitConfirmation: toggle.RequiresExplicitConfirmation,
            RequiresRestorePoint: toggle.RequiresRestorePoint,
            AffectsUserFeeling: toggle.AffectsUserFeeling,
            DisclosureFields: disclosureFields);
    }

    private static List<(string Label, bool IsAvailable)> BuildCompletenessChecks(OptimizationToggleModel toggle)
    {
        var checks = new List<(string Label, bool IsAvailable)>
        {
            ("标题", !string.IsNullOrWhiteSpace(toggle.Title)),
            ("说明", !string.IsNullOrWhiteSpace(toggle.Description)),
            ("风险等级", !string.IsNullOrWhiteSpace(toggle.RiskLevelText)),
            ("变更位置", !string.IsNullOrWhiteSpace(toggle.RegistryPath) || !string.IsNullOrWhiteSpace(toggle.ManualPath)),
            ("目标值", !string.IsNullOrWhiteSpace(toggle.TargetValue) || !string.IsNullOrWhiteSpace(toggle.OptimizedValue)),
            ("回滚值", !string.IsNullOrWhiteSpace(toggle.DefaultValue)),
            ("验证信息", !string.IsNullOrWhiteSpace(toggle.RuntimeStatusDetails) || !string.IsNullOrWhiteSpace(toggle.ValuePreview)),
            ("风险说明", !string.IsNullOrWhiteSpace(toggle.ImpactWarning) || !string.IsNullOrWhiteSpace(toggle.Drawback))
        };

        return checks;
    }

    private static string ResolveLevel(int available, int total)
    {
        if (total <= 0)
        {
            return "L0";
        }

        var ratio = (double)available / total;
        if (ratio >= 0.9)
        {
            return "L3";
        }

        if (ratio >= 0.7)
        {
            return "L2";
        }

        if (ratio >= 0.45)
        {
            return "L1";
        }

        return "L0";
    }

    private static string ResolveTypeLabel(OptimizationToggleModel toggle)
    {
        return toggle.OperationType switch
        {
            GameOptimizer.Core.Optimization.OperationType.Registry => "注册表",
            GameOptimizer.Core.Optimization.OperationType.Service => "服务",
            GameOptimizer.Core.Optimization.OperationType.ScheduledTask => "计划任务",
            GameOptimizer.Core.Optimization.OperationType.PowerPlan => "电源计划",
            GameOptimizer.Core.Optimization.OperationType.Command => "命令执行",
            GameOptimizer.Core.Optimization.OperationType.SystemApi => "系统 API",
            _ => "其他"
        };
    }

    private static string? BuildRegistryFullPath(OptimizationToggleModel toggle)
    {
        var path = TextDisplayNormalizer.NormalizeNullable(toggle.RegistryPath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var hive = Normalize(toggle.RegistryHiveText, "HKLM");
        return $"{hive}\\{path}";
    }

    private static string? BuildApplyCommand(OptimizationToggleModel toggle, string? registryFullPath)
    {
        if (string.IsNullOrWhiteSpace(registryFullPath) || string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            return TextDisplayNormalizer.NormalizeNullable(toggle.ManualSteps);
        }

        var valueType = Normalize(toggle.ValueType, "REG_SZ");
        var targetValue = Normalize(toggle.TargetValue ?? toggle.OptimizedValue, string.Empty);
        return $"reg add \"{registryFullPath}\" /v \"{toggle.RegistryValueName}\" /t {valueType} /d \"{targetValue}\" /f";
    }

    private static string? BuildVerifyCommand(OptimizationToggleModel toggle, string? registryFullPath)
    {
        if (string.IsNullOrWhiteSpace(registryFullPath))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            return $"reg query \"{registryFullPath}\"";
        }

        return $"reg query \"{registryFullPath}\" /v \"{toggle.RegistryValueName}\"";
    }

    private static string? BuildRollbackCommand(OptimizationToggleModel toggle, string? registryFullPath)
    {
        if (string.IsNullOrWhiteSpace(registryFullPath) || string.IsNullOrWhiteSpace(toggle.RegistryValueName))
        {
            return null;
        }

        var valueType = Normalize(toggle.ValueType, "REG_SZ");
        var restoreValue = Normalize(toggle.DefaultValue, string.Empty);
        return $"reg add \"{registryFullPath}\" /v \"{toggle.RegistryValueName}\" /t {valueType} /d \"{restoreValue}\" /f";
    }

    private static string BuildWhereChanged(OptimizationToggleModel toggle, string? registryFullPath)
    {
        if (!string.IsNullOrWhiteSpace(registryFullPath))
        {
            return $"注册表位置：{registryFullPath}";
        }

        if (!string.IsNullOrWhiteSpace(toggle.ManualPath))
        {
            return $"系统入口：{Normalize(toggle.ManualPath, "(未提供)")}";
        }

        return "未提供明确变更位置。";
    }

    private static string BuildBeforeAfter(string beforeValue, string currentValue, string targetValue, string windowsDefault, string rollbackValue)
    {
        return string.Join(
            Environment.NewLine,
            $"改前参考值：{beforeValue}",
            $"当前实时值：{currentValue}",
            $"目标值：{targetValue}",
            $"Windows 默认值：{windowsDefault}",
            $"回滚目标值：{rollbackValue}");
    }

    private static string BuildImpact(OptimizationToggleModel toggle)
    {
        var benefit = Normalize(toggle.Benefit, "未提供");
        var drawback = Normalize(toggle.Drawback, "未提供");
        var warning = Normalize(toggle.ImpactWarning, "未提供");
        return string.Join(
            Environment.NewLine,
            $"预期收益：{benefit}",
            $"潜在代价：{drawback}",
            $"风险提示：{warning}");
    }

    private static string BuildRollbackAndVerify(string? verifyCommand, string? rollbackCommand, string emergencyRecoveryGuide)
    {
        return string.Join(
            Environment.NewLine,
            $"验证命令：{Normalize(verifyCommand, "未提供")}",
            $"回滚命令：{Normalize(rollbackCommand, "未提供")}",
            emergencyRecoveryGuide);
    }

    private static string BuildCommandGuide(OptimizationToggleModel toggle, string? applyCommand, string? verifyCommand, string? rollbackCommand)
    {
        var manualSteps = TextDisplayNormalizer.NormalizeNullable(toggle.ManualSteps);
        if (!string.IsNullOrWhiteSpace(manualSteps))
        {
            return manualSteps;
        }

        return string.Join(
            Environment.NewLine,
            "1) 先读取当前值并记录。",
            $"2) 应用命令：{Normalize(applyCommand, "未提供")}",
            $"3) 验证命令：{Normalize(verifyCommand, "未提供")}",
            $"4) 如需恢复：{Normalize(rollbackCommand, "未提供")}");
    }

    private static string BuildEmergencyRecoveryGuide(OptimizationToggleModel toggle, string? rollbackCommand)
    {
        if (!string.IsNullOrWhiteSpace(rollbackCommand))
        {
            return $"紧急恢复：先执行回滚命令，再重启系统。命令：{rollbackCommand}";
        }

        return toggle.RequiresRestorePoint
            ? "紧急恢复：建议通过系统还原点恢复。"
            : "紧急恢复：若出现异常，请撤销变更或恢复备份。";
    }

    private static IReadOnlyList<OperationDetailDisclosureField> BuildDisclosureFields(
        OptimizationToggleModel toggle,
        string moduleTitle,
        string? registryFullPath,
        string beforeValue,
        string currentValue,
        string targetValue,
        string windowsDefault,
        string rollbackValue)
    {
        var fields = new List<OperationDetailDisclosureField>
        {
            new("OperationId", Normalize(toggle.OperationId, "(none)")),
            new("Module", moduleTitle),
            new("Type", toggle.OperationType.ToString()),
            new("Risk", Normalize(toggle.RiskLevelText, toggle.RiskLevel.ToString())),
            new("RegistryPath", Normalize(registryFullPath, "(none)")),
            new("RegistryValueName", Normalize(toggle.RegistryValueName, "(none)")),
            new("ValueType", Normalize(toggle.ValueType, "(none)")),
            new("BeforeValue", beforeValue),
            new("CurrentValue", currentValue),
            new("TargetValue", targetValue),
            new("DefaultValue", windowsDefault),
            new("RollbackValue", rollbackValue),
            new("RequiresAdmin", toggle.RequiresAdmin ? "Yes" : "No"),
            new("RequiresExplicitConfirmation", toggle.RequiresExplicitConfirmation ? "Yes" : "No"),
            new("RequiresRestorePoint", toggle.RequiresRestorePoint ? "Yes" : "No"),
            new("AffectsUserFeeling", toggle.AffectsUserFeeling ? "Yes" : "No")
        };

        AddIfPresent(fields, "CompatibilityVariant", toggle.CompatibilityVariantLabel);
        AddIfPresent(fields, "CompatibilityVariantId", toggle.CompatibilityVariantId);
        AddIfPresent(fields, "CompatibilityMethod", toggle.CompatibilityMethod);
        AddIfPresent(fields, "CompatibilityInformation", toggle.CompatibilityInformation);
        AddIfPresent(fields, "CompatibilityAlternative", toggle.CompatibilityAlternative);

        return fields;
    }

    private static void AddIfPresent(
        ICollection<OperationDetailDisclosureField> fields,
        string label,
        string? value)
    {
        var normalized = TextDisplayNormalizer.NormalizeNullable(value);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            fields.Add(new OperationDetailDisclosureField(label, normalized));
        }
    }

    private static string Normalize(string? value, string fallback)
    {
        var normalized = TextDisplayNormalizer.NormalizeNullable(value);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }
}

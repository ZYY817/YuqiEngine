// File: GameOptimizer.Shell/Data/GuideDefinitions.cs
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public static class GuideDefinitions
{
    private static readonly List<GuideSequence> _allGuides = new();

    static GuideDefinitions()
    {
        InitializeGuides();
    }

    private static void InitializeGuides()
    {
        _allGuides.Add(new GuideSequence
        {
            Id = "main-onboarding",
            PageKey = "main-onboarding",
            TitleKey = "Guide.Main.Title",
            DescriptionKey = "Guide.Main.Description",
            IconGlyph = "\uE80F",
            Steps = new List<GuideStep>
            {
                new() { Id = "main-1", TargetElementName = "SidebarScrollViewer", TitleKey = "Guide.Main.Step1.Title", ContentKey = "Guide.Main.Step1.Content", Position = GuideTooltipPosition.Right, HighlightPadding = 8 },
                new() { Id = "main-2", TargetElementName = "OverviewPageHost", TitleKey = "Guide.Main.Step2.Title", ContentKey = "Guide.Main.Step2.Content", Position = GuideTooltipPosition.Auto, HighlightPadding = 8 },
                new() { Id = "main-3", TargetElementName = "SettingsButton", TitleKey = "Guide.Main.Step4.Title", ContentKey = "Guide.Main.Step4.Content", Position = GuideTooltipPosition.Left, HighlightPadding = 12 },
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "overview-guide",
            PageKey = "overview",
            TitleKey = "Guide.Overview.Title",
            DescriptionKey = "Guide.Overview.Description",
            IconGlyph = "\uE80F",
            Steps = new List<GuideStep>
            {
                new() { Id = "overview-1", TargetElementName = "OptimizationStatusTitle", TitleKey = "Guide.Overview.Step1.Title", ContentKey = "Guide.Overview.Step1.Content", Position = GuideTooltipPosition.Bottom, HighlightPadding = 16 },
                new() { Id = "overview-2", TargetElementName = "CpuUsageLabel", TitleKey = "Guide.Overview.Step2.Title", ContentKey = "Guide.Overview.Step2.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "overview-3", TargetElementName = "FeatureMatrixLabel", TitleKey = "Guide.Overview.Step3.Title", ContentKey = "Guide.Overview.Step3.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "overview-4", TargetElementName = "FMMemoryTitle", TitleKey = "Guide.Overview.Step4.Title", ContentKey = "Guide.Overview.Step4.Content", Position = GuideTooltipPosition.Bottom }
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "memory-guide",
            PageKey = "memory-optimization",
            TitleKey = "Guide.Memory.Title",
            DescriptionKey = "Guide.Memory.Description",
            IconGlyph = "\uE950",
            Steps = new List<GuideStep>
            {
                new() { Id = "memory-1", TargetElementName = "TitleText", TitleKey = "Guide.Memory.Step1.Title", ContentKey = "Guide.Memory.Step1.Content", Position = GuideTooltipPosition.Bottom, HighlightPadding = 8 },
                new() { Id = "memory-2", TargetElementName = "ToggleListScrollViewer", TitleKey = "Guide.Memory.Step2.Title", ContentKey = "Guide.Memory.Step2.Content", Position = GuideTooltipPosition.Auto }
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "repair-guide",
            PageKey = "repair-center",
            TitleKey = "Guide.Repair.Title",
            DescriptionKey = "Guide.Repair.Description",
            IconGlyph = "\uE90F",
            Steps = new List<GuideStep>
            {
                new() { Id = "repair-1", TargetElementName = "BtnSmartScan", TitleKey = "Guide.Repair.Step1.Title", ContentKey = "Guide.Repair.Step1.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "repair-2", TargetElementName = "RepairCategoryBar", TitleKey = "Guide.Repair.Step2.Title", ContentKey = "Guide.Repair.Step2.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "repair-3", TargetElementName = "RepairItemsList", TitleKey = "Guide.Repair.Step3.Title", ContentKey = "Guide.Repair.Step3.Content", Position = GuideTooltipPosition.Auto }
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "cleanup-guide",
            PageKey = "system-cleanup",
            TitleKey = "Guide.Cleanup.Title",
            DescriptionKey = "Guide.Cleanup.Description",
            IconGlyph = "\uE74D",
            Steps = new List<GuideStep>
            {
                new() { Id = "cleanup-1", TargetElementName = "PageTitle", TitleKey = "Guide.Cleanup.Step1.Title", ContentKey = "Guide.Cleanup.Step1.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "cleanup-2", TargetElementName = "CleanupItemsList", TitleKey = "Guide.Cleanup.Step2.Title", ContentKey = "Guide.Cleanup.Step2.Content", Position = GuideTooltipPosition.Auto },
                new() { Id = "cleanup-3", TargetElementName = "RecoverableLabel", TitleKey = "Guide.Cleanup.Step3.Title", ContentKey = "Guide.Cleanup.Step3.Content", Position = GuideTooltipPosition.Right }
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "backup-guide",
            PageKey = "backup-manager",
            TitleKey = "Guide.Backup.Title",
            DescriptionKey = "Guide.Backup.Description",
            IconGlyph = "\uE81E",
            Steps = new List<GuideStep>
            {
                new() { Id = "backup-1", TargetElementName = "SnapshotListArea", TitleKey = "Guide.Backup.Step1.Title", ContentKey = "Guide.Backup.Step1.Content", Position = GuideTooltipPosition.Auto },
                new() { Id = "backup-2", TargetElementName = "CreateSnapshotBtn", TitleKey = "Guide.Backup.Step2.Title", ContentKey = "Guide.Backup.Step2.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "backup-3", TargetElementName = "BackupExplainTitle", TitleKey = "Guide.Backup.Step3.Title", ContentKey = "Guide.Backup.Step3.Content", Position = GuideTooltipPosition.Bottom }
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "core-accelerator-guide",
            PageKey = "core-accelerator",
            TitleKey = "Guide.CoreAccelerator.Title",
            DescriptionKey = "Guide.CoreAccelerator.Description",
            IconGlyph = "\uE950",
            Steps = new List<GuideStep>
            {
                new() { Id = "core-1", TargetElementName = "CoreTopologyTitle", TitleKey = "Guide.CoreAccelerator.Step1.Title", ContentKey = "Guide.CoreAccelerator.Step1.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "core-2", TargetElementName = "QuickPresetTitle", TitleKey = "Guide.CoreAccelerator.Step2.Title", ContentKey = "Guide.CoreAccelerator.Step2.Content", Position = GuideTooltipPosition.Left },
                new() { Id = "core-3", TargetElementName = "SaveConfigBtn", TitleKey = "Guide.CoreAccelerator.Step3.Title", ContentKey = "Guide.CoreAccelerator.Step3.Content", Position = GuideTooltipPosition.Left },
                new() { Id = "core-4", TargetElementName = "PreferredCoreTitle", TitleKey = "Guide.CoreAccelerator.Step4.Title", ContentKey = "Guide.CoreAccelerator.Step4.Content", Position = GuideTooltipPosition.Left }
            }
        });

        _allGuides.Add(new GuideSequence
        {
            Id = "power-guide",
            PageKey = "power-plan-editor",
            TitleKey = "Guide.Power.Title",
            DescriptionKey = "Guide.Power.Description",
            IconGlyph = "\uE945",
            Steps = new List<GuideStep>
            {
                new() { Id = "power-1", TargetElementName = "TxtCurrentPlanName", TitleKey = "Guide.Power.Step1.Title", ContentKey = "Guide.Power.Step1.Content", Position = GuideTooltipPosition.Bottom },
                new() { Id = "power-2", TargetElementName = "LblRecoveryTools", TitleKey = "Guide.Power.Step2.Title", ContentKey = "Guide.Power.Step2.Content", Position = GuideTooltipPosition.Bottom }
            }
        });
    }

    public static GuideSequence? GetGuideForPage(string pageKey)
    {
        return _allGuides.FirstOrDefault(g => g.PageKey == pageKey);
    }
}

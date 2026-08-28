using System;
using System.IO;
using System.Runtime.CompilerServices;
using GameOptimizer.Core.Models;
using GameOptimizer.Core.Services;

namespace GameOptimizer.Tests.Telemetry;

public sealed class HomeTelemetryContractTests
{
    [Fact]
    public void RealSnapshotValuesAndFieldStatusesArePreserved()
    {
        var expected = new SystemResourceSnapshot(
            CpuUsage: 12.5,
            GpuUsage: 34.5,
            MemoryUsageGb: 4.2,
            MemoryTotalGb: 16,
            NetworkLatencyMs: 8,
            TemperatureGpu: 55,
            TemperatureCpu: 48,
            DiskUsedGb: 100,
            DiskTotalGb: 512,
            DpcLatencyUs: 75);
        var capturedAt = new DateTimeOffset(2026, 8, 14, 12, 30, 0, TimeSpan.Zero);

        var telemetry = HomeTelemetrySnapshot.FromSnapshot(
            expected,
            capturedAt,
            "test-real-source");

        Assert.Equal(expected, telemetry.Snapshot);
        Assert.Equal(capturedAt, telemetry.CapturedAtUtc);
        Assert.True(telemetry.HasRealCpuSample);
        Assert.True(telemetry.HasRealGpuSample);
        Assert.True(telemetry.HasRealMemorySample);
        Assert.True(telemetry.HasRealDiskSample);
        Assert.All(
            new[] { telemetry.Cpu, telemetry.Gpu, telemetry.Memory, telemetry.Disk },
            status => Assert.Equal(TelemetryAvailability.Live, status.Availability));
    }

    [Fact]
    public void UnsupportedPermissionAndErrorStatusesRemainDistinct()
    {
        var snapshot = new SystemResourceSnapshot(0, 0, 0, 0, 0, 0, 0);
        var telemetry = new HomeTelemetrySnapshot(
            snapshot,
            DateTimeOffset.UtcNow,
            TelemetryFieldStatus.Unsupported("gpu", "counter missing"),
            TelemetryFieldStatus.PermissionDenied("cpu", "access denied"),
            TelemetryFieldStatus.Error("memory", "native call failed"),
            TelemetryFieldStatus.Unavailable("disk", "drive not ready"));

        Assert.Equal(TelemetryAvailability.Unsupported, telemetry.Cpu.Availability);
        Assert.Equal(TelemetryAvailability.PermissionDenied, telemetry.Gpu.Availability);
        Assert.Equal(TelemetryAvailability.Error, telemetry.Memory.Availability);
        Assert.Equal(TelemetryAvailability.Unavailable, telemetry.Disk.Availability);
        Assert.False(telemetry.HasRealCpuSample);
        Assert.False(telemetry.HasRealGpuSample);
        Assert.False(telemetry.HasRealMemorySample);
        Assert.False(telemetry.HasRealDiskSample);
    }

    [Fact]
    public void ExceptionFallbackReturnsEmptyValuesWithTheOriginalFailureStatus()
    {
        var telemetry = HomeTelemetrySnapshot.Unavailable(
            DateTimeOffset.UtcNow,
            "test-source",
            TelemetryAvailability.Error,
            "sample failed");

        Assert.Equal(0, telemetry.Snapshot.CpuUsage);
        Assert.Equal(0, telemetry.Snapshot.MemoryTotalGb);
        Assert.Equal(0, telemetry.Snapshot.DiskTotalGb);
        Assert.All(
            new[] { telemetry.Cpu, telemetry.Gpu, telemetry.Memory, telemetry.Disk },
            status =>
            {
                Assert.Equal(TelemetryAvailability.Error, status.Availability);
                Assert.Equal("sample failed", status.Detail);
            });
    }

    [Fact]
    public void TrendBufferDoesNotCreateHistoryFromUnavailableSamples()
    {
        var buffer = new TelemetryTrendBuffer(capacity: 3);
        var unavailable = TelemetryFieldStatus.Unavailable("test", "no sample");
        var live = TelemetryFieldStatus.Live("test");

        Assert.False(buffer.TryAdd(42, unavailable));
        Assert.Empty(buffer.Values);
        Assert.False(buffer.HasHistory);

        Assert.True(buffer.TryAdd(42, live));
        Assert.Equal(new[] { 42d }, buffer.Values);
        Assert.False(buffer.HasHistory);

        Assert.False(buffer.TryAdd(99, unavailable));
        Assert.Equal(new[] { 42d }, buffer.Values);

        Assert.False(buffer.TryAdd(double.NaN, live));
        Assert.Equal(new[] { 42d }, buffer.Values);

        Assert.True(buffer.TryAdd(43, live));
        Assert.True(buffer.HasHistory);
        Assert.Equal(new[] { 42d, 43d }, buffer.Values);
    }

    [Fact]
    public void DashboardUsesUnifiedSourceAndHasNoRandomTrendSeed()
    {
        var source = File.ReadAllText(RepoPath("GameOptimizer.Shell/ViewModels/DashboardViewModel.cs"));

        Assert.Contains("IHomeTelemetrySource", source, StringComparison.Ordinal);
        Assert.Contains("TelemetryTrendBuffer", source, StringComparison.Ordinal);
        Assert.Contains("ReadHomeTelemetrySnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Random.Shared", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplySeedJitter", source, StringComparison.Ordinal);
        Assert.Contains("ApplyHomeTelemetrySnapshot", source, StringComparison.Ordinal);
        Assert.Contains("HasRealMemorySample", source, StringComparison.Ordinal);
        Assert.Contains("MemoryAvailableDisplay", source, StringComparison.Ordinal);
        Assert.Contains("DiskAvailableDisplay", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableAcceptanceStateIsExplicitAndCannotLeakIntoProduction()
    {
        var appSource = File.ReadAllText(RepoPath("GameOptimizer.Shell/App.xaml.cs"));

        Assert.Contains("isAcceptanceUiInstance && e.Args.Any", appSource, StringComparison.Ordinal);
        Assert.Contains("--acceptance-telemetry=unavailable", appSource, StringComparison.Ordinal);
        Assert.Contains("AcceptanceUnavailableHomeTelemetrySource", appSource, StringComparison.Ordinal);
        Assert.Contains("TelemetryAvailability.Unavailable", appSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewMemoryHeadingMatchesUsedCapacitySemanticsInBothLanguages()
    {
        var zh = File.ReadAllText(RepoPath("GameOptimizer.Core/Localization/LocalizationService.ZhCN.cs"));
        var en = File.ReadAllText(RepoPath("GameOptimizer.Core/Localization/LocalizationService.EnUS.cs"));
        var overview = File.ReadAllText(RepoPath("GameOptimizer.Shell/Views/OverviewPage.xaml"));

        Assert.Contains("[\"Overview.AvailableMemory\"] = \"内存占用\"", zh, StringComparison.Ordinal);
        Assert.Contains("[\"Overview.AvailableMemory\"] = \"Memory usage\"", en, StringComparison.Ordinal);
        Assert.Contains("MemoryUsagePercentDisplay", overview, StringComparison.Ordinal);
        Assert.Contains("MemoryAvailableDisplay", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewDoesNotPresentNonDeviceSpecificThermalDataAsCpuOrGpuTemperature()
    {
        var overview = File.ReadAllText(RepoPath("GameOptimizer.Shell/Views/OverviewPage.xaml"));

        Assert.DoesNotContain("CpuTemperatureDisplay", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("GpuTemperatureDisplay", overview, StringComparison.Ordinal);
    }

    [Fact]
    public void CpuAndGpuProgressUseRealTelemetryAndHideWhenUnavailable()
    {
        var overview = File.ReadAllText(RepoPath("GameOptimizer.Shell/Views/OverviewPage.xaml"));
        var dashboard = File.ReadAllText(RepoPath("GameOptimizer.Shell/ViewModels/DashboardViewModel.cs"));

        Assert.Contains("CpuUsagePercent", overview, StringComparison.Ordinal);
        Assert.Contains("GpuUsagePercent", overview, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding HasRealCpuSample", overview, StringComparison.Ordinal);
        Assert.Contains("Visibility=\"{Binding HasRealGpuSample", overview, StringComparison.Ordinal);
        Assert.Contains("public double CpuUsagePercent", dashboard, StringComparison.Ordinal);
        Assert.Contains("public double GpuUsagePercent", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuLoadAttributionKeepsUnmappedLoadExplicit()
    {
        var attribution = GpuLoadAttribution.Unmapped(
            42.5,
            "GPU Engine",
            "hardware identity is not verifiable");

        Assert.False(attribution.IsMapped);
        Assert.Equal(42.5, attribution.UtilizationPercent);
        Assert.Null(attribution.AdapterName);
        Assert.Contains("not verifiable", attribution.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void GpuIdentityAndDashboardPresentationUseTheCentralModelWithoutFallbackGuessing()
    {
        var inventory = File.ReadAllText(RepoPath("GameOptimizer.Core/Services/GpuInventoryService.cs"));
        var hardware = File.ReadAllText(RepoPath("GameOptimizer.Shell/Models/HardwareInfo.cs"));
        var telemetry = File.ReadAllText(RepoPath("GameOptimizer.Core/Services/LiveSystemInsightService.cs"));
        var overview = File.ReadAllText(RepoPath("GameOptimizer.Shell/Views/OverviewPage.xaml"));

        Assert.Contains("interface IGpuInventoryService", inventory, StringComparison.Ordinal);
        Assert.Contains("IGpuInventoryService gpuInventory", hardware, StringComparison.Ordinal);
        Assert.DoesNotContain("new ManagementObjectSearcher(\"SELECT * FROM Win32_VideoController\")", hardware, StringComparison.Ordinal);
        Assert.Contains("GpuLoadAttribution.Unmapped", telemetry, StringComparison.Ordinal);
        Assert.Contains("candidate.DxgiLuid", telemetry, StringComparison.Ordinal);
        Assert.Contains("DxgiGpuAdapterDiscovery.TryEnumerate", inventory, StringComparison.Ordinal);
        Assert.Contains("FormatLuid", File.ReadAllText(RepoPath("GameOptimizer.Core/Services/DxgiGpuAdapterDiscovery.cs")), StringComparison.Ordinal);
        Assert.Contains("GpuLoadAdapterDisplay", overview, StringComparison.Ordinal);
        Assert.Contains("GpuAdapterListPanel", File.ReadAllText(RepoPath("GameOptimizer.Shell/Views/HardwareInfoPage.xaml")), StringComparison.Ordinal);
    }

    private static string RepoPath(string relativePath, [CallerFilePath] string sourcePath = "")
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GameOptimizer.sln")))
            current = current.Parent;

        return Path.Combine(current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), relativePath);
    }
}

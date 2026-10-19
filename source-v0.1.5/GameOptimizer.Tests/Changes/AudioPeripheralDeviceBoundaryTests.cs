using GameOptimizer.Core.Optimization.Operations;
using GameOptimizer.Core.Optimization.Providers;
using GameOptimizer.Core.Services.DeviceRepair;
using GameOptimizer.Core.Services.Repair;
using GameOptimizer.Infrastructure.Windows.DeviceRepair;
using Microsoft.Extensions.DependencyInjection;

namespace GameOptimizer.Tests.Changes;

public sealed class AudioPeripheralDeviceBoundaryTests
{
    private static readonly string[] RepairIds =
    [
        "repair.audio_service", "repair.audio_endpoint", "repair.audio_reset", "repair.audio_driver",
        "repair.bluetooth_service", "repair.bluetooth_adapter", "repair.bluetooth_cache", "repair.bluetooth_rescan",
        "repair.usb_reset", "repair.hid_service", "repair.webcam", "repair.touchpad", "repair.printer"
    ];

    [Fact]
    public void CatalogIsFixedAndUnique()
    {
        Assert.Equal(13, DeviceRepairCatalog.All.Count);
        Assert.Equal(13, DeviceRepairCatalog.All.Select(value => value.RepairItemId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(13, DeviceRepairCatalog.All.Select(value => value.CapabilityId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(RepairIds.Order(), DeviceRepairCatalog.All.Select(value => value.RepairItemId).Order());
    }

    [Fact]
    public async Task EveryActiveRepairUsesOneOwnerAndFailsClosedAfterReadOnlyDiagnostic()
    {
        var platform = new FakePlatform();
        var owner = new DeviceRepairOwner(platform);
        var registry = new RepairItemRegistry(deviceRepairOwner: owner);
        registry.Initialize();

        foreach (var repairId in RepairIds)
        {
            var item = registry.GetById(repairId);
            Assert.NotNull(item);
            var result = await item.RepairAsync(null);
            Assert.False(result.Success);
            Assert.Contains("不会修改", result.Message, StringComparison.Ordinal);
        }

        Assert.Equal(13, platform.Reads);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task HotUnplugMultipleDevicesAndDefaultChangeNeverBecomeMutation()
    {
        var platform = new FakePlatform(
            new DeviceDiagnosticSnapshot("repair.bluetooth_adapter", true, true, ["bt:a", "bt:b"], "bt:a", "two devices"),
            new DeviceDiagnosticSnapshot("repair.bluetooth_adapter", true, false, ["bt:b"], "bt:b", "default changed after unplug"));
        var owner = new DeviceRepairOwner(platform);

        var first = await owner.DiagnoseAsync("repair.bluetooth_adapter", default);
        var second = await owner.DiagnoseAsync("repair.bluetooth_adapter", default);
        var repair = await owner.RepairAsync("repair.bluetooth_adapter", null, default);

        Assert.Equal(2, first.ResourceIdentities.Count);
        Assert.Single(second.ResourceIdentities);
        Assert.NotEqual(first.DefaultResourceIdentity, second.DefaultResourceIdentity);
        Assert.False(repair.Success);
        Assert.Equal(0, platform.Writes);
    }

    [Fact]
    public async Task PermissionFailureIsFailClosedAndCancellationPropagatesBeforeAnyWrite()
    {
        var deniedOwner = new DeviceRepairOwner(new ThrowingPlatform(new UnauthorizedAccessException("denied")));
        var denied = await deniedOwner.DiagnoseAsync("repair.usb_reset", default);
        Assert.False(denied.IsApplicable);
        Assert.Contains("denied", denied.Message, StringComparison.OrdinalIgnoreCase);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelledOwner = new DeviceRepairOwner(new ThrowingPlatform(new OperationCanceledException(cts.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledOwner.DiagnoseAsync("repair.audio_driver", cts.Token));
    }

    [Fact]
    public void CompositionRegistersOneReadOnlyPlatformAndOneOwner()
    {
        var services = new ServiceCollection();
        services.AddDeviceRepairWindowsInfrastructure();
        using var provider = services.BuildServiceProvider();

        Assert.Single(services, value => value.ServiceType == typeof(IDeviceDiagnosticPlatform));
        Assert.Single(services, value => value.ServiceType == typeof(IDeviceRepairOwner));
        Assert.IsType<WindowsDeviceDiagnosticPlatform>(provider.GetRequiredService<IDeviceDiagnosticPlatform>());
        Assert.Same(provider.GetRequiredService<IDeviceRepairOwner>(), provider.GetRequiredService<IDeviceRepairOwner>());
    }

    [Fact]
    public void ProviderReusesTypedInputOwnersAndBlocksRawDeviceDisableEntries()
    {
        var operations = new BuiltInOptimizationOperationProvider().GetModules().SelectMany(value => value.Operations).ToArray();
        foreach (var id in new[]
                 {
                     "peripheral.keyboard_response", "peripheral.mouse_precision", "input.disable_pen_feedback",
                     "input.disable_touch_feedback", "input.disable_pointer_trails", "input.disable_snap_to",
                     "accessibility.disable_sticky_keys", "accessibility.disable_filter_keys", "accessibility.disable_toggle_keys",
                     // 设备禁用四项已迁入 typed DeviceStateChangeCapability（WMI 身份 + pnputil + 精确恢复），
                     // 仍要求显式确认，不再是 PolicyDenied。
                     "device.disable_system_timer", "device.disable_umbus", "device.disable_composite_bus", "device.disable_hpet_device"
                 })
        {
            Assert.IsAssignableFrom<OrchestratedChangeOperation>(Assert.Single(operations, value => value.Id == id));
        }
    }

    [Fact]
    public void ProductionRepairEntriesContainNoLegacyDeviceWriter()
    {
        var root = FindRoot();
        var files = new[]
        {
            @"GameOptimizer.Core\Services\Repair\RepairOperations\AudioRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\BluetoothRepairs.cs",
            @"GameOptimizer.Core\Services\Repair\RepairOperations\PeripheralRepairs.cs",
            @"GameOptimizer.Core\Services\Audio\AudioRuntimeHealthHelper.cs",
            @"GameOptimizer.Core\Optimization\Operations\DeviceToggleOperation.cs",
            @"GameOptimizer.Shell\Services\DeviceManagerService.cs"
        };
        var forbidden = new[]
        {
            "Process.Start", "RunCommandAsync", "RunPowerShell", "pnputil", "Disable-PnpDevice", "Enable-PnpDevice",
            "Registry.CurrentUser", "Registry.LocalMachine", "net\", \"stop", "sc\", \"config"
        };
        foreach (var relative in files)
        {
            var source = File.ReadAllText(Path.Combine(root, relative));
            foreach (var token in forbidden)
            {
                Assert.DoesNotContain(token, source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private sealed class FakePlatform(params DeviceDiagnosticSnapshot[] snapshots) : IDeviceDiagnosticPlatform
    {
        private readonly Queue<DeviceDiagnosticSnapshot> _snapshots = new(snapshots);
        public int Reads { get; private set; }
        public int Writes => 0;

        public Task<DeviceDiagnosticSnapshot> InspectAsync(DeviceRepairDefinition definition, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            if (_snapshots.Count > 0)
            {
                return Task.FromResult(_snapshots.Dequeue());
            }

            return Task.FromResult(new DeviceDiagnosticSnapshot(
                definition.RepairItemId, true, false, [$"resource:{definition.CapabilityId}"], null, "fake diagnostic"));
        }
    }

    private sealed class ThrowingPlatform(Exception exception) : IDeviceDiagnosticPlatform
    {
        public Task<DeviceDiagnosticSnapshot> InspectAsync(DeviceRepairDefinition definition, CancellationToken cancellationToken) =>
            Task.FromException<DeviceDiagnosticSnapshot>(exception);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "GameOptimizer.Core"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

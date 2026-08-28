using GameOptimizer.Core.Services.DeviceRepair;

namespace GameOptimizer.Core.Services.Repair.RepairOperations;

public static class AudioRepairs
{
    public static IEnumerable<RepairItem> GetAll(IDeviceRepairOwner? owner = null)
    {
        owner ??= DeviceRepairOwnerFallback.Instance;
        yield return Create(owner, "repair.audio_service", "Ensure Windows Audio Is Running", "Repair.AudioService.Name",
            "Ensure Windows Audio (Audiosrv) is running without restarting it.", "Repair.AudioService.Desc", "🔊");
        yield return Create(owner, "repair.audio_endpoint", "Repair Audio Endpoint", "Repair.AudioEndpoint.Name",
            "Inspect the audio endpoint pipeline.", "Repair.AudioEndpoint.Desc", "🎧");
        yield return Create(owner, "repair.audio_reset", "Reset Audio Settings", "Repair.AudioReset.Name",
            "Inspect audio state before a controlled repair.", "Repair.AudioReset.Desc", "🔄");
        yield return Create(owner, "repair.audio_driver", "Rescan Audio Devices", "Repair.AudioDriver.Name",
            "Inspect audio device health without changing the device topology.", "Repair.AudioDriver.Desc", "🧩");
    }

    private static RepairItem Create(
        IDeviceRepairOwner owner,
        string id,
        string name,
        string nameKey,
        string description,
        string descriptionKey,
        string icon) => new()
    {
        Id = id,
        Name = name,
        NameKey = nameKey,
        Description = description,
        DescriptionKey = descriptionKey,
        Icon = icon,
        Category = RepairCategory.Audio,
        CheckHealthAsync = async () => (await owner.DiagnoseAsync(id, CancellationToken.None).ConfigureAwait(false)).IsHealthy,
        RepairAsync = progress => owner.RepairAsync(id, progress, CancellationToken.None)
    };
}

using GameOptimizer.Core.Contracts.Changes;

namespace GameOptimizer.Infrastructure.Windows.Registry;

public sealed class ShowFileExtensionsCapability : RegistryDwordCapability
{
    public const string CapabilityId = "registry.show_file_extensions";

    public ShowFileExtensionsCapability(IRegistryDwordAccessor registry, TimeProvider? timeProvider = null)
        : base(
            new RegistryDwordCapabilityDefinition(
                CapabilityId,
                "1.0.0",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
                "HideFileExt",
                0,
                ChangeRisk.LowReversible,
                RequiresExplicitConfirmation: true),
            registry,
            timeProvider)
    {
    }
}

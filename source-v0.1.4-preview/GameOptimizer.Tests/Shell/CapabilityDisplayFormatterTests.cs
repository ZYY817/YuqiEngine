using GameOptimizer.Shell.Services;

namespace GameOptimizer.Tests.Shell;

public sealed class CapabilityDisplayFormatterTests
{
    [Fact]
    public void KnownCapability_UsesLocalizedOperationTitleInsteadOfInternalIdentifier()
    {
        const string capabilityId = "audio.disable_enhancements";

        var displayName = CapabilityDisplayFormatter.GetDisplayName(capabilityId);

        Assert.NotEqual(capabilityId, displayName);
        Assert.False(string.IsNullOrWhiteSpace(displayName));
    }

    [Fact]
    public void UnknownCapability_DoesNotExposeInternalIdentifierToTheUser()
    {
        const string capabilityId = "internal.unpublished_capability";

        var displayName = CapabilityDisplayFormatter.GetDisplayName(capabilityId);

        Assert.NotEqual(capabilityId, displayName);
        Assert.DoesNotContain("internal.", displayName, StringComparison.OrdinalIgnoreCase);
    }
}

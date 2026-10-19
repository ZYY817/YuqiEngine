using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Baseline;

public sealed class BaselineEnvironmentBindingPolicyTests
{
    [Fact]
    public void MatchingBindingIsAllowedWithoutPersistingRawIdentity()
    {
        var binding = new BaselineEnvironmentBinding
        {
            MachineFingerprintSha256 = "MACHINE-HASH",
            UserSidSha256 = "USER-HASH",
            OsBuild = "10.0.26100.1",
            OsArchitecture = "x64",
            AppDataRootSha256 = "ROOT-HASH"
        };

        var result = BaselineEnvironmentBindingPolicy.Evaluate(binding, binding);

        Assert.True(result.IsCompatible);
        Assert.Equal("baseline.environment_compatible", result.ReasonCode);
    }

    [Theory]
    [InlineData("machine")]
    [InlineData("user")]
    [InlineData("os")]
    [InlineData("root")]
    public void MismatchedOrMissingBindingFailsClosed(string change)
    {
        var captured = new BaselineEnvironmentBinding
        {
            MachineFingerprintSha256 = "MACHINE-HASH",
            UserSidSha256 = "USER-HASH",
            OsBuild = "10.0.26100.1",
            OsArchitecture = "x64",
            AppDataRootSha256 = "ROOT-HASH"
        };
        var current = new BaselineEnvironmentBinding
        {
            MachineFingerprintSha256 = change == "machine" ? "OTHER" : captured.MachineFingerprintSha256,
            UserSidSha256 = change == "user" ? "OTHER" : captured.UserSidSha256,
            OsBuild = change == "os" ? "10.0.26200.1" : captured.OsBuild,
            OsArchitecture = captured.OsArchitecture,
            AppDataRootSha256 = change == "root" ? "OTHER" : captured.AppDataRootSha256
        };

        var mismatch = BaselineEnvironmentBindingPolicy.Evaluate(captured, current);
        var missing = BaselineEnvironmentBindingPolicy.Evaluate(null, current);

        Assert.False(mismatch.IsCompatible);
        Assert.False(missing.IsCompatible);
        Assert.Equal("baseline.environment_binding_missing", missing.ReasonCode);
    }
}

// File: GameOptimizer.Tests/Baseline/BaselineRecoveryCoverageClassifierTests.cs
using GameOptimizer.Shell.Services.Baseline;

namespace GameOptimizer.Tests.Baseline;

public sealed class BaselineRecoveryCoverageClassifierTests
{
    [Theory]
    [InlineData("D03", true)]
    [InlineData("D04", true)]
    [InlineData("D05", true)]
    [InlineData("D06", true)]
    [InlineData("D13", true)]
    [InlineData("D09", false)]
    [InlineData("D15", false)]
    [InlineData("D16", false)]
    public void OnlyVerifiedCoreDomainsAreRequiredForBaselineAdmission(string domainId, bool expected)
    {
        Assert.Equal(expected, BaselineRecoveryCoverageClassifier.IsRequiredForBaselineAdmission(domainId));
    }

    [Theory]
    [InlineData("D09", "电源和电池")]
    [InlineData("D15", "系统还原点")]
    [InlineData("D16", "Windows 安全中心")]
    public void HighRiskDomainsExposeDefaultRecoveryGuidanceWithoutClaimingAutomaticRecovery(string domainId, string expectedGuidance)
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain(domainId),
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = "ManualAction" }],
            readFailure: null);

        Assert.Equal("仅手动恢复", assessment.RecoveryStatus);
        Assert.False(assessment.IsAutomaticRecovery);
        Assert.Contains(expectedGuidance, assessment.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("D01")]
    [InlineData("D02")]
    public void FileCopyDomainsRemainRestrictedUntilTheirRestoreOwnerProvidesAtomicReadback(string domainId)
    {
        var domain = SuccessfulDomain(domainId);
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            domain,
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = "CopyFile" }],
            readFailure: null);

        Assert.Equal("已捕获", assessment.CaptureStatus);
        Assert.Equal("受限自动恢复", assessment.RecoveryStatus);
        Assert.False(assessment.IsAutomaticRecovery);
        Assert.Contains("回读验证", assessment.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("D04")]
    [InlineData("D05")]
    [InlineData("D06")]
    public void ConfigurationDomainsUseTheDedicatedAtomicFileRecoveryOwner(string domainId)
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain(domainId),
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = "OwnedFileDomain" }],
            readFailure: null);

        Assert.Equal("可自动恢复", assessment.RecoveryStatus);
        Assert.True(assessment.IsAutomaticRecovery);
        Assert.Contains("同机", assessment.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedSettingsSnapshotRequiresTheExistingConfirmationAndReadbackChain()
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain("D03"),
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = "TypedSnapshot" }],
            readFailure: null);

        Assert.Equal("确认后可恢复", assessment.RecoveryStatus);
        Assert.True(assessment.IsAutomaticRecovery);
        Assert.Contains("确认", assessment.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("D07", "仅保留记录")]
    [InlineData("D08", "仅保留记录")]
    [InlineData("D09", "仅手动恢复")]
    [InlineData("D10", "仅手动恢复")]
    [InlineData("D11", "仅手动恢复")]
    [InlineData("D12", "仅手动恢复")]
    [InlineData("D13", "仅手动恢复")]
    [InlineData("D14", "仅保留记录")]
    [InlineData("D15", "仅手动恢复")]
    [InlineData("D16", "仅手动恢复")]
    [InlineData("D17", "仅手动恢复")]
    [InlineData("D18", "仅保留记录")]
    [InlineData("D19", "仅手动恢复")]
    [InlineData("D20", "仅手动恢复")]
    [InlineData("D21", "仅手动恢复")]
    [InlineData("D22", "仅手动恢复")]
    public void SystemAndDiagnosticDomainsNeverClaimAutomaticRecovery(string domainId, string expectedStatus)
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain(domainId),
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = "CopyFile" }],
            readFailure: null);

        Assert.Equal(expectedStatus, assessment.RecoveryStatus);
        Assert.False(assessment.IsAutomaticRecovery);
    }

    [Fact]
    public void UnknownDomainIsExplicitlyUnsupportedEvenWhenItContainsCopySteps()
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain("D99"),
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = "CopyFile" }],
            readFailure: null);

        Assert.Equal("不支持", assessment.RecoveryStatus);
        Assert.False(assessment.IsAutomaticRecovery);
    }

    [Theory]
    [InlineData("ImportRegistry")]
    [InlineData("RunCommand")]
    [InlineData("ManualAction")]
    public void UnexpectedLegacyOrManualStepsBlockFileDomainAutomaticRecovery(string stepType)
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain(),
            packageVerified: true,
            [new BaselineDomainRestoreStep { StepType = stepType }],
            readFailure: null);

        Assert.Equal("不可用", assessment.RecoveryStatus);
        Assert.False(assessment.IsAutomaticRecovery);
    }

    [Fact]
    public void MissingArtifactIsCapturedButUnavailableInsteadOfClaimingRecovery()
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            SuccessfulDomain(),
            packageVerified: true,
            restoreSteps: null,
            readFailure: "恢复资产文件缺失，已阻断自动恢复。");

        Assert.Equal("已捕获", assessment.CaptureStatus);
        Assert.Equal("不可用", assessment.RecoveryStatus);
        Assert.False(assessment.IsAutomaticRecovery);
    }

    [Fact]
    public void FailedCollectionIsNotReportedAsCaptured()
    {
        var assessment = BaselineRecoveryCoverageClassifier.Evaluate(
            new BaselineDomainArtifact { Status = BaselineDomainStatus.Failed, ErrorMessage = "拒绝访问" },
            packageVerified: true,
            restoreSteps: null,
            readFailure: null);

        Assert.Equal("未捕获", assessment.CaptureStatus);
        Assert.Equal("不可用", assessment.RecoveryStatus);
    }

    private static BaselineDomainArtifact SuccessfulDomain(string domainId = "D01") => new()
    {
        DomainId = domainId,
        DomainName = "测试域",
        Status = BaselineDomainStatus.Succeeded
    };
}

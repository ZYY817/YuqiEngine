using GameOptimizer.Core.Optimization;
using GameOptimizer.Shell.Helpers;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Tests.Esports;

/// <summary>
/// Small in-memory scene for the user-reported stack-guard crash:
/// enter Game Modes, detect an applied but policy-denied row, then keep the UI stable.
/// </summary>
public sealed class EsportsAppliedIncompatibleScenarioTests
{
    [Theory]
    [InlineData(true, false, true, CheckStateConflict.Action.RejectIncompatible)]
    [InlineData(false, false, true, CheckStateConflict.Action.RestoreApplied)]
    [InlineData(true, true, false, CheckStateConflict.Action.Accept)]
    [InlineData(false, true, false, CheckStateConflict.Action.Accept)]
    [InlineData(true, true, true, CheckStateConflict.Action.Accept)]
    [InlineData(false, false, false, CheckStateConflict.Action.Accept)]
    public void UserCheckIntent_MatchesAppliedAndCompatibilityConstraints(
        bool requestedChecked,
        bool isCompatible,
        bool isApplied,
        CheckStateConflict.Action expected)
    {
        var action = CheckStateConflict.ResolveUserCheck(requestedChecked, isCompatible, isApplied);
        Assert.Equal(expected, action);
    }

    [Fact]
    public void AppliedIncompatibleRow_ProgrammaticSyncDoesNotBounce()
    {
        var scene = new RowScene(isCompatible: false, isApplied: true);

        scene.SyncAppliedToggleSilently();

        Assert.True(scene.IsChecked);
        Assert.Equal(0, scene.HandlerInvocations);
        Assert.Equal(0, scene.MaxDepth);
    }

    [Fact]
    public void AppliedIncompatibleRow_UserCheckIsRejectedOnceWithoutRecursion()
    {
        var scene = new RowScene(isCompatible: false, isApplied: true);

        scene.UserSetChecked(true);

        Assert.False(scene.IsChecked);
        Assert.Equal(1, scene.HandlerInvocations);
        Assert.True(scene.MaxDepth <= 1);
        Assert.Equal(CheckStateConflict.Action.RejectIncompatible, scene.LastAction);
    }

    [Fact]
    public void AppliedCompatibleRow_UserUncheckIsRestoredOnceWithoutRecursion()
    {
        var scene = new RowScene(isCompatible: true, isApplied: true) { IsChecked = true };

        scene.UserSetChecked(false);

        Assert.True(scene.IsChecked);
        Assert.Equal(1, scene.HandlerInvocations);
        Assert.True(scene.MaxDepth <= 1);
        Assert.Equal(CheckStateConflict.Action.RestoreApplied, scene.LastAction);
    }

    [Fact]
    public void SecurityItemForceSelect_DoesNotThrowAndStaysUnselected()
    {
        var item = new GameModeOptimizationItem("security.disable_vbs", "安全缓解", OperationRiskLevel.Extreme)
        {
            IsCompatible = false,
            IncompatibleReason = "产品安全策略禁止为了性能关闭系统保护。"
        };

        var ex = Record.Exception(() => item.IsSelected = true);

        Assert.Null(ex);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void PresetFilter_NeverSelectsIncompatibleSecurityItems()
    {
        Assert.False(CheckStateConflict.ComputePresetSelected("all", OperationRiskLevel.Extreme, isCompatible: false));
        Assert.False(CheckStateConflict.ComputePresetSelected("performance", OperationRiskLevel.Safe, isCompatible: false));
        Assert.True(CheckStateConflict.ComputePresetSelected("all", OperationRiskLevel.Safe, isCompatible: true));
        Assert.False(CheckStateConflict.ComputePresetSelected("safe", OperationRiskLevel.Extreme, isCompatible: true));
        Assert.True(CheckStateConflict.ComputePresetSelected("balanced", OperationRiskLevel.Balanced, isCompatible: true));
    }

    private sealed class RowScene
    {
        private int _depth;

        public RowScene(bool isCompatible, bool isApplied)
        {
            IsCompatible = isCompatible;
            IsApplied = isApplied;
        }

        public bool IsCompatible { get; }
        public bool IsApplied { get; }
        public bool IsChecked { get; set; }
        public int HandlerInvocations { get; private set; }
        public int MaxDepth { get; private set; }
        public CheckStateConflict.Action? LastAction { get; private set; }

        public void SyncAppliedToggleSilently()
        {
            if (IsApplied)
                IsChecked = true;
        }

        public void UserSetChecked(bool value)
        {
            IsChecked = value;
            HandleUserCheck();
        }

        private void HandleUserCheck()
        {
            HandlerInvocations++;
            _depth++;
            MaxDepth = Math.Max(MaxDepth, _depth);
            if (_depth > 32)
                throw new InvalidOperationException("Applied/incompatible check handling re-entered until stack overflow.");

            try
            {
                LastAction = CheckStateConflict.ResolveUserCheck(IsChecked, IsCompatible, IsApplied);
                switch (LastAction)
                {
                    case CheckStateConflict.Action.RejectIncompatible:
                        IsChecked = false;
                        break;
                    case CheckStateConflict.Action.RestoreApplied:
                        IsChecked = true;
                        break;
                }
            }
            finally
            {
                _depth--;
            }
        }
    }
}

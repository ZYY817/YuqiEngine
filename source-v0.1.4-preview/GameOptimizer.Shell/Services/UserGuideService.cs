// File: GameOptimizer.Shell/Services/UserGuideService.cs
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using GameOptimizer.Core.Constants;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Models;

namespace GameOptimizer.Shell.Services;

public class UserGuideService
{
    private static readonly Lazy<UserGuideService> _instance = new(() => new UserGuideService());
    public static UserGuideService Instance => _instance.Value;

    private const int TargetResolveMaxAttempts = 12;
    private const int TargetResolveRetryDelayMs = 180;

    private static readonly IReadOnlyDictionary<string, string> TargetNameAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OptimizationModeTitle"] = "OptimizationStatusTitle",
            ["MemoryHeroCard"] = "TitleText",
            ["ToggleListBorder"] = "ToggleListScrollViewer",
            ["SnapshotListArea"] = "SnapshotListArea"
        };

    private static readonly IReadOnlyDictionary<string, string> GuidePageHostNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["overview"] = "OverviewPageHost",
            ["memory-optimization"] = "MemoryOptimizationPageHost",
            ["repair-center"] = "RepairCenterPageHost",
            ["system-cleanup"] = "SystemCleanupPageHost",
            ["backup-manager"] = "BackupManagerPageHost",
            ["core-accelerator"] = "CoreAcceleratorPageHost",
            ["power-plan-editor"] = "PowerPlanEditorPageHost"
        };

    private GuideOverlayAdorner? _currentAdorner;
    private GuideTooltip? _currentTooltip;
    private GuideSequence? _currentSequence;
    private string? _currentPageKey;
    private int _currentStepIndex;
    private FrameworkElement? _currentRootElement;
    private GuideProgress _progress;
    private CancellationTokenSource? _resolveTargetCts;

    private static string ProgressFilePath => AppPaths.GuideProgressFilePath;

    private UserGuideService()
    {
        _progress = LoadProgress();
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public async Task StartGuideAsync(string pageKey, FrameworkElement rootElement)
    {
        if (_currentAdorner != null)
        {
            StopGuide();
        }

        var sequence = GuideDefinitions.GetGuideForPage(pageKey);
        if (sequence == null || sequence.Steps.Count == 0)
        {
            return;
        }

        if (IsGuideCompleted(pageKey))
        {
            return;
        }

        _currentSequence = sequence;
        _currentPageKey = pageKey;
        _currentStepIndex = 0;
        _currentRootElement = rootElement;

        await Task.Delay(500);
        ShowCurrentStep();
    }

    public async Task StartMainOnboardingAsync(FrameworkElement mainWindow)
    {
        if (_currentAdorner != null)
        {
            StopGuide();
        }

        var sequence = GuideDefinitions.GetGuideForPage("main-onboarding");
        if (sequence == null || sequence.Steps.Count == 0)
        {
            return;
        }

        if (IsGuideCompleted("main-onboarding"))
        {
            return;
        }

        _currentSequence = sequence;
        _currentPageKey = "main-onboarding";
        _currentStepIndex = 0;
        _currentRootElement = mainWindow;

        await Task.Delay(1200);
        ShowCurrentStep();
    }

    private void ShowCurrentStep()
    {
        _ = ShowCurrentStepAsync();
    }

    private async Task ShowCurrentStepAsync()
    {
        if (_currentSequence == null || _currentRootElement == null || _currentStepIndex >= _currentSequence.Steps.Count)
        {
            return;
        }

        var stepIndex = _currentStepIndex;
        var sequenceId = _currentSequence.Id;
        var step = _currentSequence.Steps[stepIndex];
        var loc = LocalizationService.Instance;

        EnsureTooltipAndAdorner();
        if (_currentTooltip == null)
        {
            return;
        }

        _currentTooltip.UpdateContent(
            loc.Get(step.TitleKey),
            loc.Get(step.ContentKey),
            stepIndex + 1,
            _currentSequence.Steps.Count,
            stepIndex == _currentSequence.Steps.Count - 1);

        _resolveTargetCts?.Cancel();
        _resolveTargetCts?.Dispose();
        _resolveTargetCts = new CancellationTokenSource();

        var searchRoot = ResolveSearchRoot();
        FrameworkElement? targetElement;
        try
        {
            targetElement = await ResolveTargetElementAsync(
                _currentRootElement,
                searchRoot,
                step.TargetElementName,
                _resolveTargetCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_resolveTargetCts.IsCancellationRequested ||
            _currentSequence == null ||
            _currentRootElement == null ||
            _currentSequence.Id != sequenceId ||
            _currentStepIndex != stepIndex)
        {
            return;
        }

        if (targetElement != null)
        {
            _currentAdorner?.SetHighlightTarget(targetElement, step.HighlightPadding, step.Position);
        }
        else
        {
            Debug.WriteLine($"[UserGuideService] Target not found: '{step.TargetElementName}'");
            _currentAdorner?.SetHighlightTarget(null);
        }
    }

    private void EnsureTooltipAndAdorner()
    {
        if (_currentRootElement == null)
        {
            return;
        }

        if (_currentTooltip == null)
        {
            _currentTooltip = new GuideTooltip();
            _currentTooltip.NextClicked += OnNextClicked;
            _currentTooltip.PrevClicked += OnPrevClicked;
            _currentTooltip.SkipClicked += OnSkipClicked;
            _currentTooltip.CloseClicked += OnCloseClicked;
        }

        if (_currentAdorner != null)
        {
            return;
        }

        var adornerLayer = AdornerLayer.GetAdornerLayer(_currentRootElement);
        if (adornerLayer == null)
        {
            Debug.WriteLine("[UserGuideService] AdornerLayer is null, cannot show guide.");
            return;
        }

        _currentAdorner = new GuideOverlayAdorner(_currentRootElement, _currentTooltip);
        adornerLayer.Add(_currentAdorner);
    }

    private async Task<FrameworkElement?> ResolveTargetElementAsync(
        FrameworkElement rootElement,
        FrameworkElement? searchRoot,
        string requestedName,
        CancellationToken cancellationToken)
    {
        var resolvedName = ResolveTargetName(requestedName);
        FrameworkElement? lastCandidate = null;

        for (var attempt = 0; attempt < TargetResolveMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate = searchRoot != null
                ? FindBestElementByName(searchRoot, resolvedName, rootElement)
                : null;
            if (candidate == null)
            {
                candidate = FindBestElementByName(rootElement, resolvedName, rootElement);
            }

            if (candidate != null)
            {
                lastCandidate = candidate;
                if (IsElementReady(candidate, rootElement))
                {
                    return candidate;
                }

                if (attempt == 0)
                {
                    candidate.BringIntoView();
                }
            }

            if (attempt < TargetResolveMaxAttempts - 1)
            {
                await Task.Delay(TargetResolveRetryDelayMs, cancellationToken);
            }
        }

        return lastCandidate;
    }

    private static string ResolveTargetName(string requestedName)
    {
        if (TargetNameAliases.TryGetValue(requestedName, out var alias))
        {
            return alias;
        }

        return requestedName;
    }

    private FrameworkElement? ResolveSearchRoot()
    {
        if (_currentRootElement == null || string.IsNullOrWhiteSpace(_currentPageKey))
        {
            return _currentRootElement;
        }

        if (string.Equals(_currentPageKey, "main-onboarding", StringComparison.OrdinalIgnoreCase))
        {
            return _currentRootElement;
        }

        if (!GuidePageHostNames.TryGetValue(_currentPageKey, out var hostName))
        {
            return _currentRootElement;
        }

        var hostElement = FindBestElementByName(_currentRootElement, hostName, _currentRootElement);
        if (hostElement == null || !hostElement.IsVisible || hostElement.Visibility != Visibility.Visible)
        {
            return _currentRootElement;
        }

        if (hostElement is ContentControl cc && cc.Content is FrameworkElement contentElement)
        {
            return contentElement;
        }

        return hostElement;
    }

    private FrameworkElement? FindBestElementByName(DependencyObject root, string name, FrameworkElement visibilityRoot)
    {
        var candidates = new List<FrameworkElement>();
        CollectElementsByName(root, name, candidates, new HashSet<DependencyObject>());
        if (candidates.Count == 0)
        {
            return null;
        }

        var visibleReady = candidates
            .Where(candidate => IsElementReady(candidate, visibilityRoot))
            .OrderByDescending(GetCandidateScore)
            .ToList();
        if (visibleReady.Count > 0)
        {
            return visibleReady[0];
        }

        var visibleOnly = candidates
            .Where(candidate => candidate.IsVisible && candidate.Visibility == Visibility.Visible)
            .OrderByDescending(GetCandidateScore)
            .ToList();
        if (visibleOnly.Count > 0)
        {
            return visibleOnly[0];
        }

        return candidates
            .OrderByDescending(GetCandidateScore)
            .FirstOrDefault();
    }

    private static void CollectElementsByName(
        DependencyObject current,
        string name,
        List<FrameworkElement> results,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(current))
        {
            return;
        }

        if (current is FrameworkElement fe &&
            string.Equals(fe.Name, name, StringComparison.Ordinal))
        {
            results.Add(fe);
        }

        if (current is Visual || current is Visual3D)
        {
            var visualChildCount = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < visualChildCount; i++)
            {
                CollectElementsByName(VisualTreeHelper.GetChild(current, i), name, results, visited);
            }
        }

        foreach (var logicalChild in LogicalTreeHelper.GetChildren(current))
        {
            if (logicalChild is DependencyObject childObj)
            {
                CollectElementsByName(childObj, name, results, visited);
            }
        }
    }

    private bool IsElementReady(FrameworkElement element, FrameworkElement root)
    {
        if (!element.IsLoaded ||
            !element.IsVisible ||
            element.Visibility != Visibility.Visible ||
            element.ActualWidth < 1 ||
            element.ActualHeight < 1)
        {
            return false;
        }

        if (PresentationSource.FromVisual(element) == null)
        {
            return false;
        }

        if (!IsVisibilityChainVisible(element, root))
        {
            return false;
        }

        if (root is not Visual rootVisual)
        {
            return true;
        }

        try
        {
            var bounds = element.TransformToVisual(rootVisual).TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight));

            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return false;
            }

            var rootRect = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            return rootRect.Width > 0 && rootRect.Height > 0 && rootRect.IntersectsWith(bounds);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVisibilityChainVisible(DependencyObject element, DependencyObject? stopAt)
    {
        var current = element;
        while (current != null)
        {
            if (current is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
            {
                return false;
            }

            if (ReferenceEquals(current, stopAt))
            {
                return true;
            }

            current = GetParentObject(current);
        }

        return true;
    }

    private static DependencyObject? GetParentObject(DependencyObject child)
    {
        if (child is Visual || child is Visual3D)
        {
            return VisualTreeHelper.GetParent(child);
        }

        if (child is FrameworkContentElement fce)
        {
            return fce.Parent;
        }

        return (child as FrameworkElement)?.Parent;
    }

    private static int GetCandidateScore(FrameworkElement element)
    {
        var score = 0;
        if (element.IsLoaded)
        {
            score += 25;
        }

        if (element.Visibility == Visibility.Visible)
        {
            score += 25;
        }

        if (element.IsVisible)
        {
            score += 35;
        }

        var area = element.ActualWidth * element.ActualHeight;
        if (area > 0)
        {
            score += Math.Min(200, (int)(area / 250));
        }

        return score;
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        if (_currentSequence == null)
        {
            return;
        }

        _currentStepIndex++;
        if (_currentStepIndex >= _currentSequence.Steps.Count)
        {
            CompleteGuide();
        }
        else
        {
            ShowCurrentStep();
        }
    }

    private void OnPrevClicked(object? sender, EventArgs e)
    {
        if (_currentStepIndex <= 0)
        {
            return;
        }

        _currentStepIndex--;
        ShowCurrentStep();
    }

    private void OnSkipClicked(object? sender, EventArgs e)
    {
        StopGuide();
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        StopGuide();
    }

    private void CompleteGuide()
    {
        if (_currentSequence != null)
        {
            _progress.CompletedSequences[_currentSequence.Id] = true;
            _progress.CompletionDates[_currentSequence.Id] = DateTime.Now;
            _progress.LastUpdated = DateTime.Now;
            SaveProgress();
        }

        StopGuide();
    }

    public void StopGuide()
    {
        _resolveTargetCts?.Cancel();
        _resolveTargetCts?.Dispose();
        _resolveTargetCts = null;

        if (_currentAdorner != null && _currentRootElement != null)
        {
            _currentAdorner.StopTracking();
            var adornerLayer = AdornerLayer.GetAdornerLayer(_currentRootElement);
            adornerLayer?.Remove(_currentAdorner);
            _currentAdorner = null;
        }

        if (_currentTooltip != null)
        {
            _currentTooltip.NextClicked -= OnNextClicked;
            _currentTooltip.PrevClicked -= OnPrevClicked;
            _currentTooltip.SkipClicked -= OnSkipClicked;
            _currentTooltip.CloseClicked -= OnCloseClicked;
            _currentTooltip = null;
        }

        _currentSequence = null;
        _currentPageKey = null;
        _currentStepIndex = 0;
        _currentRootElement = null;
    }

    public bool IsGuideCompleted(string pageKey)
    {
        var sequence = GuideDefinitions.GetGuideForPage(pageKey);
        return sequence != null && _progress.CompletedSequences.GetValueOrDefault(sequence.Id, false);
    }

    private GuideProgress LoadProgress()
    {
        try
        {
            if (File.Exists(ProgressFilePath))
            {
                var (content, error) = GameOptimizer.Core.Services.SafeFileHelper.ReadAllText(ProgressFilePath);
                if (string.IsNullOrEmpty(error) && !string.IsNullOrEmpty(content))
                {
                    return JsonSerializer.Deserialize<GuideProgress>(content) ?? new GuideProgress();
                }
            }
        }
        catch
        {
            Debug.WriteLine("[UserGuideService] Failed to load guide progress");
        }

        return new GuideProgress();
    }

    private void SaveProgress()
    {
        try
        {
            var directory = Path.GetDirectoryName(ProgressFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
            _ = GameOptimizer.Core.Utilities.SafeFileOperations.WriteTextWithRetryAsync(
                ProgressFilePath,
                json,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UserGuideService] Failed to save guide progress: {ex.Message}");
        }
    }

    private void OnLanguageChanged(object? sender, string languageCode)
    {
        if (_currentTooltip == null ||
            _currentSequence == null ||
            _currentStepIndex >= _currentSequence.Steps.Count)
        {
            return;
        }

        var step = _currentSequence.Steps[_currentStepIndex];
        var loc = LocalizationService.Instance;

        _currentTooltip.UpdateContent(
            loc.Get(step.TitleKey),
            loc.Get(step.ContentKey),
            _currentStepIndex + 1,
            _currentSequence.Steps.Count,
            _currentStepIndex == _currentSequence.Steps.Count - 1);
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Core.Services.ProcessTuning;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views
{
    public partial class CpuAffinityPage : UserControl
    {
        private bool _isInitialized;
        private CpuAffinityViewModel? _viewModel;
        private readonly IProcessAffinityService _profileService;
        private readonly IProcessTuningOwner _processTuningOwner;
        
        public CpuAffinityPage(IProcessAffinityService profileService, IProcessTuningOwner processTuningOwner)
        {
            _profileService = profileService ?? throw new ArgumentNullException(nameof(profileService));
            _processTuningOwner = processTuningOwner ?? throw new ArgumentNullException(nameof(processTuningOwner));
            PageDiagnosticService.Log("CpuAffinityPage", "Constructor called");
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            IsVisibleChanged += OnVisibleChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            if (IsVisible && !_isInitialized)
            {
                InitializeViewModel();
            }
            UpdateLocalizedTexts();
        }

        private async void OnUnloaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            try
            {
                if (_viewModel is not null)
                {
                    await _viewModel.DeactivateAsync();
                }
            }
            catch (Exception ex)
            {
                PageDiagnosticService.LogError("CpuAffinityPage", "Receipt restore on unload failed", ex);
            }
        }
        
        private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && !_isInitialized)
            {
                InitializeViewModel();
            }
        }

        private void OnLanguageChanged(object? sender, string e)
        {
            _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
        }

        private void UpdateLocalizedTexts()
        {
            var loc = LocalizationService.Instance;
            if (FindName("PageTitle") is TextBlock title)
                title.Text = loc.Get("CpuAffinity.Title");
            if (FindName("PageSubtitle") is TextBlock subtitle)
                subtitle.Text = loc.Get("CpuAffinity.Subtitle");
            if (FindName("FeatureDescTitle") is TextBlock featureDesc)
                featureDesc.Text = loc.Get("CpuAffinity.FeatureDesc");
            if (FindName("UsageTipsTitle") is TextBlock usageTips)
                usageTips.Text = loc.Get("CpuAffinity.UsageTips");
            if (FindName("CoreMatrixTitle") is TextBlock coreMatrix)
                coreMatrix.Text = loc.Get("CpuAffinity.CoreMatrix");
            if (FindName("QuickActionsTitle") is TextBlock quickActions)
                quickActions.Text = loc.Get("CpuAffinity.QuickActions");
            if (FindName("AddProgramText") is TextBlock addProgram)
                addProgram.Text = loc.Get("CpuAffinity.AddProgram");
            if (FindName("ApplyAllText") is TextBlock applyAll)
                applyAll.Text = loc.Get("CpuAffinity.ApplyAll");
            if (FindName("ClearAllText") is TextBlock clearAll)
                clearAll.Text = loc.Get("CpuAffinity.ClearAll");
            if (FindName("RefreshStatusText") is TextBlock refreshStatus)
                refreshStatus.Text = loc.Get("CpuAffinity.RefreshStatus");
            if (FindName("ConfiguredProgramsTitle") is TextBlock configuredPrograms)
                configuredPrograms.Text = loc.Get("CpuAffinity.ConfiguredPrograms");
            if (FindName("ConfigHintText") is TextBlock configHint)
                configHint.Text = loc.Get("CpuAffinity.ConfigHint");
            if (FindName("NoConfigTitle") is TextBlock noConfigTitle)
                noConfigTitle.Text = loc.Get("CpuAffinity.NoConfig");
            if (FindName("NoConfigHint") is TextBlock noConfigHint)
                noConfigHint.Text = loc.Get("CpuAffinity.NoConfigHint");
            if (FindName("ProcessingText") is TextBlock processing)
                processing.Text = loc.Get("Common.Processing");
            
            // 功能说明内容
            if (FindName("FeatureDesc1") is System.Windows.Documents.Run fd1)
                fd1.Text = loc.Get("CpuAffinity.FeatureDesc1");
            if (FindName("FeatureDesc2") is System.Windows.Documents.Run fd2)
                fd2.Text = loc.Get("CpuAffinity.FeatureDesc2");
            if (FindName("FeatureDesc3") is System.Windows.Documents.Run fd3)
                fd3.Text = loc.Get("CpuAffinity.FeatureDesc3");
            
            // 使用提示内容
            if (FindName("UsageTip1") is System.Windows.Documents.Run ut1)
                ut1.Text = loc.Get("CpuAffinity.UsageTip1");
            if (FindName("UsageTip2") is System.Windows.Documents.Run ut2)
                ut2.Text = loc.Get("CpuAffinity.UsageTip2");
            if (FindName("UsageTip3") is System.Windows.Documents.Run ut3)
                ut3.Text = loc.Get("CpuAffinity.UsageTip3");
        }
        
        private void InitializeViewModel()
        {
            if (_isInitialized) return;
            _isInitialized = true;
            
            try
            {
                PageDiagnosticService.Log("CpuAffinityPage", "InitializeViewModel - using injected profile reader and ProcessTuningOwner");
                _viewModel = new CpuAffinityViewModel(_profileService, _processTuningOwner);
                DataContext = _viewModel;
                _viewModel.Initialize();
                PageDiagnosticService.LogLoaded(
                    "CpuAffinityPage",
                    true,
                    "ViewModel initialized with the shared ProcessTuningOwner");
            }
            catch (Exception ex)
            {
                PageDiagnosticService.LogError("CpuAffinityPage", "Init error", ex);
            }
        }
    }
}

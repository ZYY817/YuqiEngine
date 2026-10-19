using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.CpuAffinity;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views
{
    public partial class AffinityEditDialog : Window
    {
        private readonly ProcessAffinityProfile _profile;
        private readonly CpuInfo _cpuInfo;
        private readonly List<CpuThread> _threads;
        private readonly bool _isHybrid;
        private readonly List<int> _pCoreIndices;
        private readonly List<int> _eCoreIndices;

        public IEnumerable<int> SelectedThreads => _threads.Where(t => t.IsSelected).Select(t => t.Index);

        public AffinityEditDialog(ProcessAffinityProfile profile, CpuInfo cpuInfo)
        {
            _profile = profile;
            _cpuInfo = cpuInfo;
            _isHybrid = cpuInfo.HasHybridArchitecture;
            
            _threads = cpuInfo.Threads.Select(t => new CpuThread
            {
                Index = t.Index,
                CoreType = t.CoreType,
                PhysicalCoreId = t.PhysicalCoreId,
                IsSelected = profile.AssignedThreads.Contains(t.Index)
            }).ToList();

            _pCoreIndices = _threads
                .Where(t => t.CoreType == CpuCoreType.PerformanceCore || t.CoreType == CpuCoreType.CacheCore)
                .Select(t => t.Index).ToList();
            _eCoreIndices = _threads
                .Where(t => t.CoreType == CpuCoreType.EfficiencyCore || t.CoreType == CpuCoreType.ComputeCore)
                .Select(t => t.Index).ToList();

            InitializeComponent();

            var loc = LocalizationService.Instance;
            TitleText.Text = loc.Get("AffinityEdit.Title", profile.DisplayName);
            SubtitleText.Text = loc.Get("AffinityEdit.Current", profile.AffinityDescription);

            SetupModeDescriptions();
            
            if (_isHybrid)
            {
                PCoreButton.Visibility = Visibility.Visible;
                ECoreButton.Visibility = Visibility.Visible;
                CoreTypeInfo.Visibility = Visibility.Visible;
            }

            ThreadList.ItemsSource = _threads;
            UpdateSelectionInfo();
            UpdateModeSelection();
            UpdateLocalizedTexts();

            Owner = Application.Current.MainWindow;
            MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
            KeyDown += Window_KeyDown;
            
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        }
        
        // P2-003: 键盘导航支持
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && SelectedThreads.Any())
            {
                DialogResult = true;
                Close();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
            }
        }
        
        private void OnLanguageChanged(object? sender, string e)
        {
            _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
        }
        
        private void UpdateLocalizedTexts()
        {
            var loc = LocalizationService.Instance;
            
            LblPresetModes.Text = IconGlyphs.Target + " " + loc.Get("AffinityEdit.PresetModes");
            GameModeTitle.Text = loc.Get("AffinityEdit.GameMode");
            BalancedModeTitle.Text = loc.Get("AffinityEdit.BalancedMode");
            BackgroundModeTitle.Text = loc.Get("AffinityEdit.BackgroundMode");
            
            LblCustomSelect.Text = "🔧 " + loc.Get("AffinityEdit.CustomSelect");
            SelectAllButton.Content = loc.Get("AffinityEdit.SelectAll");
            ClearAllButton.Content = loc.Get("AffinityEdit.ClearAll");
            PCoreButton.Content = loc.Get("AffinityEdit.SelectPCores");
            ECoreButton.Content = loc.Get("AffinityEdit.SelectECores");
            
            CancelButton.Content = loc.Get("Common.Cancel");
            ConfirmButton.Content = loc.Get("Common.Save");
            
            SetupModeDescriptions();
            UpdateSelectionInfo();
        }

        private void SetupModeDescriptions()
        {
            var loc = LocalizationService.Instance;
            if (_isHybrid)
            {
                GameModeDesc.Text = loc.Get("AffinityEdit.GameModeDescHybrid", _pCoreIndices.Count);
                BalancedModeDesc.Text = loc.Get("AffinityEdit.BalancedModeDescHybrid", _threads.Count);
                BackgroundModeDesc.Text = loc.Get("AffinityEdit.BackgroundModeDescHybrid", _eCoreIndices.Count);
            }
            else
            {
                var half = _threads.Count / 2;
                GameModeDesc.Text = loc.Get("AffinityEdit.GameModeDesc", half);
                BalancedModeDesc.Text = loc.Get("AffinityEdit.BalancedModeDesc", _threads.Count);
                BackgroundModeDesc.Text = loc.Get("AffinityEdit.BackgroundModeDesc", _threads.Count - half);
            }
        }

        private void GameMode_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isHybrid)
            {
                foreach (var t in _threads)
                    t.IsSelected = _pCoreIndices.Contains(t.Index);
            }
            else
            {
                var half = _threads.Count / 2;
                foreach (var t in _threads)
                    t.IsSelected = t.Index < half;
            }
            RefreshThreadList();
        }

        private void BalancedMode_Click(object sender, MouseButtonEventArgs e)
        {
            foreach (var t in _threads) t.IsSelected = true;
            RefreshThreadList();
        }

        private void BackgroundMode_Click(object sender, MouseButtonEventArgs e)
        {
            if (_isHybrid)
            {
                foreach (var t in _threads)
                    t.IsSelected = _eCoreIndices.Contains(t.Index);
            }
            else
            {
                var half = _threads.Count / 2;
                foreach (var t in _threads)
                    t.IsSelected = t.Index >= half;
            }
            RefreshThreadList();
        }

        private void Thread_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is CpuThread thread)
            {
                thread.IsSelected = !thread.IsSelected;
                UpdateSelectionInfo();
                UpdateModeSelection();
            }
        }

        private void RefreshThreadList()
        {
            ThreadList.ItemsSource = null;
            ThreadList.ItemsSource = _threads;
            UpdateSelectionInfo();
            UpdateModeSelection();
        }

        private void UpdateSelectionInfo()
        {
            var loc = LocalizationService.Instance;
            var count = _threads.Count(t => t.IsSelected);
            SelectionInfo.Text = loc.Get("AffinityEdit.SelectedCount", count, _threads.Count);
            ConfirmButton.IsEnabled = count > 0;

            if (_isHybrid)
            {
                var pCount = _threads.Count(t => t.IsSelected && _pCoreIndices.Contains(t.Index));
                var eCount = _threads.Count(t => t.IsSelected && _eCoreIndices.Contains(t.Index));
                PCoreInfo.Text = $"{pCount}P";
                ECoreInfo.Text = $"{eCount}E";
            }
        }

        private void UpdateModeSelection()
        {
            var selectedIndices = _threads.Where(t => t.IsSelected).Select(t => t.Index).ToHashSet();
            
            GameModeCheck.Visibility = Visibility.Collapsed;
            BalancedModeCheck.Visibility = Visibility.Collapsed;
            BackgroundModeCheck.Visibility = Visibility.Collapsed;

            GameModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, ThemeColors.SkyBlue.R, ThemeColors.SkyBlue.G, ThemeColors.SkyBlue.B));
            BalancedModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, ThemeColors.Success.R, ThemeColors.Success.G, ThemeColors.Success.B));
            BackgroundModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, ThemeColors.Purple.R, ThemeColors.Purple.G, ThemeColors.Purple.B));

            if (selectedIndices.Count == _threads.Count)
            {
                BalancedModeCheck.Visibility = Visibility.Visible;
                BalancedModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, ThemeColors.Success.R, ThemeColors.Success.G, ThemeColors.Success.B));
            }
            else if (_isHybrid)
            {
                if (_pCoreIndices.Count > 0 && selectedIndices.SetEquals(_pCoreIndices))
                {
                    GameModeCheck.Visibility = Visibility.Visible;
                    GameModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, ThemeColors.SkyBlue.R, ThemeColors.SkyBlue.G, ThemeColors.SkyBlue.B));
                }
                else if (_eCoreIndices.Count > 0 && selectedIndices.SetEquals(_eCoreIndices))
                {
                    BackgroundModeCheck.Visibility = Visibility.Visible;
                    BackgroundModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, ThemeColors.Purple.R, ThemeColors.Purple.G, ThemeColors.Purple.B));
                }
            }
            else
            {
                var half = _threads.Count / 2;
                var firstHalf = Enumerable.Range(0, half).ToHashSet();
                var secondHalf = Enumerable.Range(half, _threads.Count - half).ToHashSet();

                if (selectedIndices.SetEquals(firstHalf))
                {
                    GameModeCheck.Visibility = Visibility.Visible;
                    GameModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, ThemeColors.SkyBlue.R, ThemeColors.SkyBlue.G, ThemeColors.SkyBlue.B));
                }
                else if (selectedIndices.SetEquals(secondHalf))
                {
                    BackgroundModeCheck.Visibility = Visibility.Visible;
                    BackgroundModeCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, ThemeColors.Purple.R, ThemeColors.Purple.G, ThemeColors.Purple.B));
                }
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _threads) t.IsSelected = true;
            RefreshThreadList();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _threads) t.IsSelected = false;
            RefreshThreadList();
        }

        private void SelectPCores_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _threads)
                t.IsSelected = t.CoreType == CpuCoreType.PerformanceCore || t.CoreType == CpuCoreType.CacheCore;
            RefreshThreadList();
        }

        private void SelectECores_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in _threads)
                t.IsSelected = t.CoreType == CpuCoreType.EfficiencyCore || t.CoreType == CpuCoreType.ComputeCore;
            RefreshThreadList();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedThreads.Any())
            {
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

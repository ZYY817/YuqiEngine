using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GameOptimizer.Core.Localization;
using GameOptimizer.Core.Services.CpuAffinity;

namespace GameOptimizer.Shell.Views
{
    public partial class ProcessSelectorDialog : Window
    {
        private readonly List<RunningProcess> _allProcesses;
        
        public RunningProcess? SelectedProcess { get; private set; }

        public ProcessSelectorDialog(List<RunningProcess> processes)
        {
            _allProcesses = processes;
            InitializeComponent();
            ProcessList.ItemsSource = _allProcesses;
            
            Owner = Application.Current.MainWindow;
            MouseLeftButtonDown += (s, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
            
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            
            UpdateLocalizedTexts();
        }
        
        private void OnLanguageChanged(object? sender, string e)
        {
            _ = Dispatcher.InvokeAsync(UpdateLocalizedTexts);
        }
        
        private void UpdateLocalizedTexts()
        {
            var loc = LocalizationService.Instance;
            Title = loc.Get("ProcessSelector.Title");
            TitleText.Text = loc.Get("ProcessSelector.Title");
            SubtitleText.Text = LocalizationService.Instance.CurrentLanguage == "en-US"
                ? "Choose an app whose CPU cores you want to set"
                : "选择要设置可用 CPU 核心的应用";
            CancelButton.Content = loc.Get("Common.Cancel");
            ConfirmButton.Content = loc.Get("Common.Confirm");
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var filter = SearchBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(filter))
            {
                ProcessList.ItemsSource = _allProcesses;
            }
            else
            {
                ProcessList.ItemsSource = _allProcesses
                    .Where(p => p.Name.ToLowerInvariant().Contains(filter))
                    .ToList();
            }
        }

        private void ProcessList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedProcess = ProcessList.SelectedItem as RunningProcess;
            ConfirmButton.IsEnabled = SelectedProcess != null;
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedProcess != null)
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

    public class MemorySizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                if (bytes < 1024 * 1024)
                    return $"{bytes / 1024.0:F0} KB";
                if (bytes < 1024 * 1024 * 1024)
                    return $"{bytes / (1024.0 * 1024.0):F1} MB";
                return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
            return "0 KB";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}

using System;
using System.Windows.Controls;
using GameOptimizer.Core.Services.Latency;
using GameOptimizer.Shell.ViewModels;

namespace GameOptimizer.Shell.Views
{
    public partial class LatencyTestPage : UserControl
    {
        public LatencyTestPage()
        {
            InitializeComponent();
            try
            {
                var service = new LatencyTestService();
                var viewModel = new LatencyTestViewModel(service);
                DataContext = viewModel;
                viewModel.Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LatencyTestPage] Init error: {ex.Message}");
            }
        }
    }
}

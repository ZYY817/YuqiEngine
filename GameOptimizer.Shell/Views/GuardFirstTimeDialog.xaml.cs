// File: GameOptimizer.Shell/Views/GuardFirstTimeDialog.xaml.cs
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace GameOptimizer.Shell.Views;

public partial class GuardFirstTimeDialog : Window
{
    public bool DontShowAgain { get; private set; }

    public GuardFirstTimeDialog(int monitorIntervalSeconds)
    {
        InitializeComponent();
        
        var loc = GameOptimizer.Core.Localization.LocalizationService.Instance;
        
        Title = loc.Get("Guard.FirstTime.Title") ?? "优化守护服务";
        TitleText.Text = loc.Get("Guard.FirstTime.MainTitle") ?? "优化守护服务已启用";
        SubtitleText.Text = loc.Get("Guard.FirstTime.Subtitle") ?? "自动保护您的优化设置";
        
        Feature1Title.Text = loc.Get("Guard.FirstTime.Feature1.Title") ?? "自动监控";
        Feature1Desc.Text = loc.Get("Guard.FirstTime.Feature1.Desc") ?? "定期检查优化项状态，防止被系统自动恢复";
        
        Feature2Title.Text = loc.Get("Guard.FirstTime.Feature2.Title") ?? "智能恢复";
        Feature2Desc.Text = loc.Get("Guard.FirstTime.Feature2.Desc") ?? "检测到优化被恢复时，自动重新应用（最多重试3次）";
        
        Feature3Title.Text = loc.Get("Guard.FirstTime.Feature3.Title") ?? "实时通知";
        Feature3Desc.Text = loc.Get("Guard.FirstTime.Feature3.Desc") ?? "检测到异常时及时通知您，让您掌握优化状态";
        
        SettingsTitle.Text = loc.Get("Guard.FirstTime.Settings") ?? "当前设置";
        IntervalLabel.Text = loc.Get("Guard.FirstTime.Interval") ?? "监控间隔：";
        IntervalValue.Text = $"{monitorIntervalSeconds} " + (loc.Get("Common.Seconds") ?? "秒");
        
        DontShowText.Text = loc.Get("Guard.FirstTime.DontShow") ?? "不再显示";
        LearnMoreText.Text = loc.Get("Common.LearnMore") ?? "了解更多";
        ConfirmText.Text = loc.Get("Common.GotIt") ?? "我知道了";
    }

    private void LearnMore_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/yourusername/gameoptimizer/wiki/Guard-Service",
                UseShellExecute = true
            });
        }
        catch
        {
            // 静默失败
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        DontShowAgain = DontShowAgainCheckbox.IsChecked == true;
        DialogResult = true;
        Close();
    }

    public static bool? Show(Window owner, int monitorIntervalSeconds)
    {
        var dialog = new GuardFirstTimeDialog(monitorIntervalSeconds)
        {
            Owner = owner
        };
        return dialog.ShowDialog();
    }
}

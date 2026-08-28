using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameOptimizer.Core.Localization;
using GameOptimizer.Shell.Controls;
using GameOptimizer.Shell.Services;
using GameOptimizer.Shell.Constants;

namespace GameOptimizer.Shell.Views;

public partial class AdvancedPowerDialog : Window
{
    private readonly PowerSettingsQueryService _queryService = new();
    private List<PowerSubgroupItem> _subgroups = new();
    private readonly Dictionary<string, int> _pendingChanges = new();
    private readonly Dictionary<string, int> _originalValues = new();
    private bool _showAcOnly = true;

    public AdvancedPowerDialog()
    {
        InitializeComponent();
        UpdateLocalizedTexts();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        KeyDown += Window_KeyDown;
    }
    
    // P2-003: 键盘导航支持
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        TxtLoading.Visibility = Visibility.Visible;
        TxtSubtitle.Text = LocalizationService.Instance.Get("AdvancedPower.Loading");
        await Task.Run(() => _subgroups = _queryService.QueryAllSettings());
        TxtLoading.Visibility = Visibility.Collapsed;
        UpdateSubgroupList();
        UpdateStatusTexts();
        if (_subgroups.Count > 0) SubgroupList.SelectedIndex = 0;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, string e) => _ = Dispatcher.InvokeAsync(() =>
    {
        UpdateLocalizedTexts();
        UpdateSubgroupList();
        UpdateStatusTexts();
        if (SubgroupList.SelectedItem is SubgroupVm vm) LoadSettingsForSubgroup(vm.Item);
    });

    private void UpdateLocalizedTexts()
    {
        var loc = LocalizationService.Instance;
        DialogTitleText.Text = loc.Get("AdvancedPower.Title");
        LblSettingGroups.Text = loc.Get("AdvancedPower.SettingGroups");
        TxtCurrentSubgroup.Text = loc.Get("AdvancedPower.SelectGroup");
        TxtLoading.Text = loc.Get("AdvancedPower.Loading");
        ChkShowAcOnly.Content = loc.Get("AdvancedPower.ShowAcOnly");
        BtnResetGroup.Content = loc.Get("AdvancedPower.ResetGroup");
        BtnCancel.Content = loc.Get("Common.Cancel");
        BtnApply.Content = loc.Get("AdvancedPower.ApplyChanges");
    }

    private void UpdateSubgroupList()
    {
        var idx = SubgroupList.SelectedIndex;
        SubgroupList.ItemsSource = _subgroups.Select(s => new SubgroupVm(s)).ToList();
        if (idx >= 0 && idx < _subgroups.Count) SubgroupList.SelectedIndex = idx;
    }

    private void UpdateStatusTexts()
    {
        var loc = LocalizationService.Instance;
        TxtSubtitle.Text = loc.Get("AdvancedPower.GroupCount", _subgroups.Count);
        TxtTotalSettings.Text = loc.Get("AdvancedPower.TotalSettings", _subgroups.Sum(s => s.Settings.Count));
        TxtChangeCount.Text = loc.Get("AdvancedPower.ChangedCount", _pendingChanges.Count);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 1) DragMove(); }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingChanges.Count > 0)
        {
            var loc = LocalizationService.Instance;
            if (!ConfirmDialog.Show(this, loc.Get("Dialog.Confirm"), loc.Get("AdvancedPower.UnsavedChanges"), Constants.IconGlyphs.Question)) return;
        }
        DialogResult = false;
        Close();
    }

    private void SubgroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SubgroupList.SelectedItem is SubgroupVm vm)
        {
            TxtCurrentSubgroup.Text = vm.Name;
            TxtSettingCount.Text = LocalizationService.Instance.Get("AdvancedPower.SettingCount", vm.Item.Settings.Count);
            LoadSettingsForSubgroup(vm.Item);
        }
    }

    private void LoadSettingsForSubgroup(PowerSubgroupItem subgroup)
    {
        SettingsList.ItemsSource = subgroup.Settings.Select(s => new SettingVm(s, _showAcOnly, OnSettingValueChanged)).ToList();
        foreach (var s in subgroup.Settings)
        {
            var key = $"{s.SubgroupGuid}|{s.Definition.Guid}";
            if (!_originalValues.ContainsKey(key))
                _originalValues[key] = _showAcOnly ? s.CurrentAcValue : s.CurrentDcValue;
        }
    }

    private void OnSettingValueChanged(PowerSettingItem setting, int newValue)
    {
        var key = $"{setting.SubgroupGuid}|{setting.Definition.Guid}";
        var orig = _originalValues.GetValueOrDefault(key, _showAcOnly ? setting.CurrentAcValue : setting.CurrentDcValue);
        if (newValue != orig) _pendingChanges[key] = newValue;
        else _pendingChanges.Remove(key);
        UpdateChangeCount();
    }

    private void UpdateChangeCount()
    {
        TxtChangeCount.Text = LocalizationService.Instance.Get("AdvancedPower.ModifiedCount", _pendingChanges.Count);
        BtnApply.IsEnabled = _pendingChanges.Count > 0;
    }

    private void ChkShowAcOnly_Changed(object sender, RoutedEventArgs e)
    {
        _showAcOnly = ChkShowAcOnly.IsChecked == true;
        if (SubgroupList.SelectedItem is SubgroupVm vm) LoadSettingsForSubgroup(vm.Item);
    }

    private void BtnResetGroup_Click(object sender, RoutedEventArgs e)
    {
        if (SubgroupList.SelectedItem is not SubgroupVm vm) return;
        foreach (var s in vm.Item.Settings) _pendingChanges.Remove($"{s.SubgroupGuid}|{s.Definition.Guid}");
        LoadSettingsForSubgroup(vm.Item);
        UpdateChangeCount();
    }

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingChanges.Count == 0) return;
        var loc = LocalizationService.Instance;
        BtnApply.IsEnabled = false;
        BtnApply.Content = loc.Get("AdvancedPower.Applying");

        int success = 0, fail = 0;
        await Task.Run(() =>
        {
            foreach (var kvp in _pendingChanges.ToList())
            {
                var parts = kvp.Key.Split('|');
                if (parts.Length != 2) continue;
                var subGuid = Guid.Parse(parts[0]);
                var setGuid = Guid.Parse(parts[1]);
                var ok = _showAcOnly ? _queryService.SetSettingValue(subGuid, setGuid, kvp.Value, true) : _queryService.SetSettingValueBoth(subGuid, setGuid, kvp.Value);
                if (ok) success++; else fail++;
            }
        });

        _pendingChanges.Clear();
        UpdateChangeCount();
        BtnApply.Content = loc.Get("AdvancedPower.ApplyChanges");

        if (fail == 0) ToastService.Instance.Show(loc.Get("AdvancedPower.Title"), loc.Get("AdvancedPower.ApplySuccess", success), ToastType.Success);
        else ToastService.Instance.Show(loc.Get("AdvancedPower.Title"), loc.Get("AdvancedPower.ApplyPartial", success, fail), ToastType.Warning);

        _subgroups = await Task.Run(() => _queryService.QueryAllSettings());
        _originalValues.Clear();
        UpdateSubgroupList();
        if (SubgroupList.Items.Count > 0) SubgroupList.SelectedIndex = 0;
    }
}

public class SubgroupVm
{
    public PowerSubgroupItem Item { get; }
    public string Name => Item.LocalizedName;
    public string SettingCountText => LocalizationService.Instance.Get("Power.SettingCount", Item.Settings.Count);
    public SubgroupVm(PowerSubgroupItem item) => Item = item;
}

public class SettingVm
{
    private readonly PowerSettingItem _s;
    private readonly bool _acOnly;
    private readonly Action<PowerSettingItem, int> _onChange;

    public string Name => _s.LocalizedName;
    public string GuidDisplay => _s.Definition.Guid.ToString().ToUpper();
    public string AcValueDisplay => Fmt(_s.CurrentAcValue);
    public string DcValueDisplay => Fmt(_s.CurrentDcValue);
    public Visibility DcVisibility => _acOnly ? Visibility.Collapsed : Visibility.Visible;
    public string AcLabel => LocalizationService.Instance.Get("AdvancedPower.AcLabel");
    public string DcLabel => LocalizationService.Instance.Get("AdvancedPower.DcLabel");
    public string AcTooltip => LocalizationService.Instance.Get("AdvancedPower.AcTooltip");
    public string DcTooltip => LocalizationService.Instance.Get("AdvancedPower.DcTooltip");
    public FrameworkElement Control { get; }

    public SettingVm(PowerSettingItem s, bool acOnly, Action<PowerSettingItem, int> onChange)
    {
        _s = s; _acOnly = acOnly; _onChange = onChange;
        Control = CreateCtrl();
    }

    private string Fmt(int v)
    {
        if (_s.Definition.Type == DynamicSettingType.Enum && _s.Definition.EnumOptions != null)
        {
            var opt = _s.GetLocalizedEnumOptions().FirstOrDefault(o => o.Value == v);
            return opt?.LocalizedName ?? v.ToString();
        }
        var u = _s.LocalizedUnit;
        return string.IsNullOrEmpty(u) ? v.ToString() : $"{v} {u}";
    }

    private FrameworkElement CreateCtrl()
    {
        var cur = _acOnly ? _s.CurrentAcValue : _s.CurrentDcValue;
        if (_s.Definition.Type == DynamicSettingType.Enum && _s.Definition.EnumOptions?.Count > 0) return MakeCombo(cur);
        return MakeSlider(cur);
    }

    private ComboBox MakeCombo(int cur)
    {
        var cb = new ComboBox { Width = 200, MinHeight = 36, VerticalContentAlignment = VerticalAlignment.Center };
        foreach (var o in _s.GetLocalizedEnumOptions())
        {
            var it = new ComboBoxItem { Content = o.LocalizedName, Tag = o.Value };
            cb.Items.Add(it);
            if (o.Value == cur) cb.SelectedItem = it;
        }
        cb.SelectionChanged += (_, _) => { if (cb.SelectedItem is ComboBoxItem sel && sel.Tag is int idx) _onChange(_s, idx); };
        return cb;
    }

    private StackPanel MakeSlider(int cur)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        var d = _s.Definition;
        var sl = new Slider { Width = 160, Minimum = d.Min, Maximum = Math.Min(d.Max > 0 ? d.Max : 100, 100000), Value = cur, TickFrequency = d.Increment > 0 ? d.Increment : 1, IsSnapToTickEnabled = d.Increment > 1, VerticalAlignment = VerticalAlignment.Center };
        var bd = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(12, 0, 0, 0), MinWidth = 80 };
        bd.SetResourceReference(Border.BackgroundProperty, "Theme.Brush.InputBackground");
        bd.SetResourceReference(Border.BorderBrushProperty, "Theme.Brush.ControlAccentBorder");
        var tx = new TextBlock { Text = Fmt(cur), FontSize = 13, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        tx.SetResourceReference(TextBlock.ForegroundProperty, "Theme.Brush.ControlAccent");
        bd.Child = tx;
        sl.ValueChanged += (_, _) => { var nv = (int)sl.Value; tx.Text = Fmt(nv); _onChange(_s, nv); };
        p.Children.Add(sl); p.Children.Add(bd);
        return p;
    }
}

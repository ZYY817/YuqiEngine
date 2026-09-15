using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameOptimizer.Core.Localization;

namespace GameOptimizer.Shell.Controls;

public partial class EmptyState : UserControl
{
    private static string DefaultTitle => LocalizationService.Instance.Get("Common.NoData");

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState), new PropertyMetadata("\uE7BA"));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty, OnActionTextChanged));

    public static readonly DependencyProperty ActionCommandProperty =
        DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null));

    public static readonly DependencyProperty HasActionProperty =
        DependencyProperty.Register(nameof(HasAction), typeof(bool), typeof(EmptyState), new PropertyMetadata(false));

    public static readonly DependencyProperty VisualStateProperty =
        DependencyProperty.Register(nameof(VisualState), typeof(ControlVisualState), typeof(EmptyState), new PropertyMetadata(ControlVisualState.Default));

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title
    {
        get
        {
            var value = (string)GetValue(TitleProperty);
            return string.IsNullOrEmpty(value) ? DefaultTitle : value;
        }
        set => SetValue(TitleProperty, value);
    }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public bool HasAction { get => (bool)GetValue(HasActionProperty); set => SetValue(HasActionProperty, value); }
    public ControlVisualState VisualState { get => (ControlVisualState)GetValue(VisualStateProperty); set => SetValue(VisualStateProperty, value); }

    public event RoutedEventHandler? ActionClicked;

    public EmptyState() => InitializeComponent();

    private static void OnActionTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EmptyState state)
            state.HasAction = !string.IsNullOrWhiteSpace(e.NewValue as string);
    }

    private void ActionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ActionCommand?.CanExecute(null) == false)
            return;

        ActionCommand?.Execute(null);
        ActionClicked?.Invoke(this, e);
    }
}

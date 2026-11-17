using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using ClaudeUsageMeter.Native;
using ClaudeUsageMeter.ViewModels;
using static ClaudeUsageMeter.Native.Win32;

namespace ClaudeUsageMeter.Views;

public partial class PopupWindow : Window
{
    private readonly MeterViewModel _vm;
    private Win32.RECT _anchor;
    private bool _alignToCorner = true;
    private bool _hiding;
    private bool _backdrop;
    private bool _menuOpen;

    /// <summary>Keeps the popup open while a menu anchored to it is showing; closes it afterwards if focus went elsewhere.</summary>
    internal void HoldOpenFor(System.Windows.Controls.ContextMenu menu)
    {
        _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            Dispatcher.BeginInvoke(() => { if (!IsActive) HideAnimated(); },
                System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    public event Action? RefreshRequested;
    public event Action<UIElement>? MenuRequested;
    public event Action? SignInRequested;
    public event Action? OpenSettingsRequested;

    /// <summary>When the popup last closed; lets a tray click that caused the close not reopen it.</summary>
    public DateTime LastHidden { get; private set; } = DateTime.MinValue;

    internal PopupWindow(MeterViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmChanged;
        SourceInitialized += (_, _) =>
        {
            WindowEffects.MakeToolWindow(this, noActivate: false);
            ApplyTheme(App.Current.Theme.AppsDark);
        };
        // While the "..." menu is open the popup stays: the menu is its own window, so opening it
        // can deactivate the popup, and hiding then would take the menu (anchored here) down with it.
        Deactivated += (_, _) => { if (!_menuOpen) HideAnimated(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { HideAnimated(); e.Handled = true; } };
        // The HWND takes the new size after SizeChanged, so re-anchor once that has happened;
        // otherwise a taller popup (e.g. with the "Couldn't refresh" note) grows over the taskbar.
        SizeChanged += (_, _) =>
        {
            if (!IsVisible || _hiding) return;
            Reposition();
            Dispatcher.BeginInvoke(() => { if (IsVisible && !_hiding) Reposition(); },
                System.Windows.Threading.DispatcherPriority.Background);
        };
    }

    public void ApplyTheme(bool dark)
    {
        _backdrop = WindowEffects.Apply(this, dark);
        Root.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, _backdrop ? "Brush.WindowTint" : "Brush.WindowFallback");
    }

    internal void ShowAt(Win32.RECT anchor, bool alignToCorner)
    {
        _anchor = anchor;
        _alignToCorner = alignToCorner;
        _hiding = false;
        Opacity = 0;
        if (!IsVisible)
        {
            // Show off-screen first so WPF can lay out (and adopt the target monitor's DPI) before we place it.
            Left = -32000; Top = -32000;
            Show();
        }
        var hwnd = new WindowInteropHelper(this).Handle;
        // Move onto the target monitor so WM_DPICHANGED fires before measuring.
        SetWindowPos(hwnd, IntPtr.Zero, anchor.Left, anchor.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        UpdateLayout();
        Dispatcher.BeginInvoke(() =>
        {
            Reposition();
            Activate();
            SetForegroundWindow(hwnd);
            AnimateIn();
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
        var p = Taskbar.FlyoutPosition(_anchor, r.Width, r.Height, _alignToCorner);
        SetWindowPos(hwnd, HWND_TOPMOST, p.X, p.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void AnimateIn()
    {
        if (WindowEffects.ReducedMotion)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }

    public void HideAnimated()
    {
        if (!IsVisible || _hiding) return;
        _hiding = true;
        LastHidden = DateTime.UtcNow;
        if (WindowEffects.ReducedMotion)
        {
            FinishHide();
            return;
        }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) => { if (_hiding) FinishHide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    private void FinishHide()
    {
        Hide();
        BeginAnimation(OpacityProperty, null);
        _hiding = false;
        App.Current.TrimMemorySoon();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MeterViewModel.IsRefreshing)) return;
        if (_vm.IsRefreshing && !WindowEffects.ReducedMotion)
        {
            RefreshSpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { RepeatBehavior = RepeatBehavior.Forever });
        }
        else
        {
            RefreshSpin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void Menu_Click(object sender, RoutedEventArgs e) => MenuRequested?.Invoke(MenuButton);
    private void SignIn_Click(object sender, RoutedEventArgs e) => SignInRequested?.Invoke();

    private void ProblemAction_Click(object sender, RoutedEventArgs e)
    {
        switch (_vm.ProblemAction)
        {
            case ProblemAction.SignIn: SignInRequested?.Invoke(); break;
            case ProblemAction.OpenSettings: OpenSettingsRequested?.Invoke(); break;
            default: RefreshRequested?.Invoke(); break;
        }
    }
}

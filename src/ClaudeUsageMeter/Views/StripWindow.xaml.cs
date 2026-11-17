using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageMeter.Native;
using ClaudeUsageMeter.Services;
using ClaudeUsageMeter.ViewModels;
using static ClaudeUsageMeter.Native.Win32;

namespace ClaudeUsageMeter.Views;

/// <summary>
/// A slim always-visible strip sitting on the empty part of the taskbar.
/// It is a topmost, non-activating tool window placed over the taskbar (we never inject into
/// Explorer). It re-asserts topmost when the foreground changes, hides for full-screen apps and an
/// auto-hidden taskbar, follows DPI and monitor changes, and remembers where it was dragged to.
/// </summary>
public partial class StripWindow : Window
{
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _guard = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly WinEventProc _foregroundProc;
    private IntPtr _foregroundHook;
    private IntPtr _hwnd;
    private int _appBarMessage;
    private bool _appBarRegistered;

    private TaskbarInfo? _taskbar;
    private bool _wanted, _fullscreen, _taskbarHidden;
    private bool _pressed, _dragging;
    private POINT _pressPoint;
    private int _pressLeft;

    internal event Action<RECT>? Clicked;
    public event Action? RightClicked;
    /// <summary>Raised when the strip can't be shown on this taskbar (e.g. a vertical one), so the tray icon can stand in.</summary>
    public event Action<bool>? AvailabilityChanged;

    public bool IsAvailable { get; private set; } = true;

    internal StripWindow(MeterViewModel vm, AppSettings settings)
    {
        InitializeComponent();
        DataContext = vm;
        _settings = settings;
        _foregroundProc = OnForegroundChanged;
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => { if (IsVisible && !_dragging) Place(); };
        _guard.Tick += (_, _) => Guard();
        Surface.MouseLeftButtonDown += OnPress;
        Surface.MouseMove += OnMove;
        Surface.MouseLeftButtonUp += OnRelease;
        Surface.MouseRightButtonUp += (_, e) => { e.Handled = true; RightClicked?.Invoke(); };
        Surface.LostMouseCapture += (_, _) => { if (_dragging) EndDrag(); _pressed = false; };
    }

    public void SetTaskbarTheme(bool dark)
    {
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(dark ? "/Themes/Colors.Dark.xaml" : "/Themes/Colors.Light.xaml", UriKind.Relative),
        });
    }

    public void SetTooltip(string text) => Surface.ToolTip = text;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        WindowEffects.MakeToolWindow(this, noActivate: true);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Register as an appbar only to hear about full-screen apps; we never reserve space.
        _appBarMessage = RegisterWindowMessage("ClaudeUsageMeter.AppBar");
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd, uCallbackMessage = _appBarMessage };
        _appBarRegistered = SHAppBarMessage(ABM_NEW, ref abd) != IntPtr.Zero;
    }

    // ---------------- Show / hide ----------------

    public void Start()
    {
        _wanted = true;
        if (_foregroundHook == IntPtr.Zero)
            _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _foregroundProc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        _guard.Start();
        Evaluate();
    }

    public void Stop()
    {
        _wanted = false;
        _guard.Stop();
        if (_foregroundHook != IntPtr.Zero) { UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        Hide();
    }

    /// <summary>Decides visibility from the current taskbar and foreground state, then places the strip.</summary>
    private void Evaluate() => Evaluate(place: true);

    private void Evaluate(bool place)
    {
        if (!_wanted) return;
        _taskbar = PickTaskbar();
        var available = _taskbar is { IsHorizontal: true };
        if (available != IsAvailable)
        {
            IsAvailable = available;
            AvailabilityChanged?.Invoke(available);
        }
        _taskbarHidden = _taskbar is { } tb && IsTaskbarHidden(tb);
        _fullscreen = _taskbar is { } t && ForegroundIsFullScreen(t);

        var show = available && !_taskbarHidden && !_fullscreen;
        if (show)
        {
            var wasHidden = !IsVisible;
            if (wasHidden) Show();
            if (place || wasHidden) Place();
        }
        else if (IsVisible)
        {
            Hide();
        }
    }

    private void Guard()
    {
        if (!_wanted) return;
        var before = _taskbar?.TaskbarRect;
        Evaluate(place: false);
        if (!IsVisible) return;
        // Re-place only when the taskbar moved or resized (resolution, DPI, taskbar settings).
        if (before is not { } b || _taskbar is not { } now || !SameRect(b, now.TaskbarRect)) Place();
        // Keep above the taskbar (it raises itself when clicked).
        BringToTop();
    }

    private void OnForegroundChanged(IntPtr hook, int evt, IntPtr hwnd, int idObject, int idChild, int thread, int time)
    {
        // Runs on our UI thread (out-of-context hooks are delivered via the message loop).
        Evaluate();
        if (IsVisible) BringToTop();
    }

    private void BringToTop() =>
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    // ---------------- Placement ----------------

    private TaskbarInfo? PickTaskbar()
    {
        var all = Taskbar.All();
        if (all.Count == 0) return null;
        return all.FirstOrDefault(t => string.Equals(t.Device, _settings.Strip.Monitor, StringComparison.OrdinalIgnoreCase)) ?? all[0];
    }

    public IReadOnlyList<string> Monitors() => Taskbar.All().Select(t => t.Device).ToList();

    private void Place()
    {
        if (_taskbar is not { } tb || _hwnd == IntPtr.Zero || !tb.IsHorizontal) return;

        // Move onto the target monitor first so WPF adopts its DPI before we size ourselves.
        GetWindowRect(_hwnd, out var r);
        if (MonitorFromWindow(_hwnd, MONITOR_DEFAULTTONEAREST) != tb.Monitor)
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, tb.TaskbarRect.Left, tb.TaskbarRect.Top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        }
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        var heightDip = Math.Max(24, tb.TaskbarRect.Height / scale);
        if (Math.Abs(Height - heightDip) > 0.5) Height = heightDip;
        UpdateLayout();
        GetWindowRect(_hwnd, out r);
        var width = r.Width;
        var height = tb.TaskbarRect.Height;

        int left;
        var minLeft = tb.TaskbarRect.Left + (int)(4 * scale);
        var maxLeft = tb.TaskbarRect.Right - width - (int)(4 * scale);
        if (_settings.Strip.Offset is >= 0 and <= 1 && string.Equals(_settings.Strip.Monitor, tb.Device, StringComparison.OrdinalIgnoreCase))
        {
            left = minLeft + (int)Math.Round(_settings.Strip.Offset * Math.Max(0, maxLeft - minLeft));
        }
        else
        {
            // Default: just left of the notification area.
            var tray = FindWindowEx(tb.Hwnd, IntPtr.Zero, "TrayNotifyWnd", null);
            left = tray != IntPtr.Zero && GetWindowRect(tray, out var tr) && tr.Width > 0 && tr.Left > tb.TaskbarRect.Left
                ? tr.Left - width - (int)(12 * scale)
                : tb.TaskbarRect.Right - width - (int)(260 * scale);
        }
        left = Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft));
        SetWindowPos(_hwnd, HWND_TOPMOST, left, tb.TaskbarRect.Top, width, height, SWP_NOACTIVATE);
    }

    private static bool IsTaskbarHidden(TaskbarInfo tb)
    {
        var r = tb.TaskbarRect;
        var m = tb.MonitorRect;
        return tb.Edge == TaskbarEdge.Bottom ? r.Top >= m.Bottom - 4 : r.Bottom <= m.Top + 4;
    }

    private bool ForegroundIsFullScreen(TaskbarInfo tb)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || fg == _hwnd || fg == GetShellWindow() || fg == GetDesktopWindow()) return false;
        var cls = ClassName(fg);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow")
            return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var m = tb.MonitorRect;
        return r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
    }

    private static bool SameRect(RECT a, RECT b) => a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;

    // ---------------- Mouse: click opens the popup, drag moves along the taskbar ----------------

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _dragging = false;
        GetCursorPos(out _pressPoint);
        GetWindowRect(_hwnd, out var r);
        _pressLeft = r.Left;
        Surface.CaptureMouse();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || _taskbar is not { } tb) return;
        GetCursorPos(out var p);
        var dx = p.X - _pressPoint.X;
        if (!_dragging && Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance * VisualTreeHelper.GetDpi(this).DpiScaleX) return;
        _dragging = true;
        GetWindowRect(_hwnd, out var r);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var minLeft = tb.TaskbarRect.Left + (int)(4 * scale);
        var maxLeft = tb.TaskbarRect.Right - r.Width - (int)(4 * scale);
        var left = Math.Clamp(_pressLeft + dx, minLeft, Math.Max(minLeft, maxLeft));
        SetWindowPos(_hwnd, HWND_TOPMOST, left, r.Top, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed) return;
        _pressed = false;
        var wasDragging = _dragging;
        Surface.ReleaseMouseCapture();
        if (wasDragging)
        {
            EndDrag();
        }
        else
        {
            GetWindowRect(_hwnd, out var r);
            Clicked?.Invoke(r);
        }
        e.Handled = true;
    }

    private void EndDrag()
    {
        _dragging = false;
        if (_taskbar is not { } tb) return;
        GetWindowRect(_hwnd, out var r);
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var minLeft = tb.TaskbarRect.Left + (int)(4 * scale);
        var maxLeft = tb.TaskbarRect.Right - r.Width - (int)(4 * scale);
        _settings.Strip.Monitor = tb.Device;
        _settings.Strip.Offset = maxLeft > minLeft ? Math.Clamp((r.Left - minLeft) / (double)(maxLeft - minLeft), 0, 1) : 0;
        SettingsStore.Save(_settings);
    }

    /// <summary>Forget the dragged position and go back next to the notification area.</summary>
    public void ResetPosition()
    {
        _settings.Strip.Offset = -1;
        SettingsStore.Save(_settings);
        Evaluate();
    }

    public void MoveToMonitor(string device)
    {
        _settings.Strip.Monitor = device;
        _settings.Strip.Offset = -1;
        SettingsStore.Save(_settings);
        Evaluate();
    }

    // ---------------- Messages ----------------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE); // clicking the strip never steals focus from your app
        }
        if (msg == _appBarMessage && _appBarMessage != 0)
        {
            var code = (int)wParam;
            if (code is ABN_FULLSCREENAPP or ABN_STATECHANGE or ABN_POSCHANGED)
                Dispatcher.BeginInvoke(Evaluate, DispatcherPriority.Background);
            handled = true;
            return IntPtr.Zero;
        }
        if (msg is WM_DISPLAYCHANGE or WM_DPICHANGED or WM_SETTINGCHANGE)
        {
            Dispatcher.BeginInvoke(Evaluate, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        Stop();
        if (_appBarRegistered)
        {
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };
            SHAppBarMessage(ABM_REMOVE, ref abd);
            _appBarRegistered = false;
        }
        base.OnClosed(e);
    }
}

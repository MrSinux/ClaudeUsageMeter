using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClaudeUsageMeter.Core;
using ClaudeUsageMeter.Native;
using ClaudeUsageMeter.Rendering;
using ClaudeUsageMeter.Services;
using ClaudeUsageMeter.ViewModels;
using ClaudeUsageMeter.Views;

namespace ClaudeUsageMeter;

public partial class App : Application
{
    private const string InstanceName = "Local\\ClaudeUsageMeter.SingleInstance";
    private const string ShowEventName = "Local\\ClaudeUsageMeter.ShowPopup";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private TrayIcon? _tray;
    private PopupWindow? _popup;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
    private DispatcherTimer? _trimTimer;
    private bool _openPopupWhenReady;

    /// <summary>--demo: made-up numbers, no CLI, no alerts (for screenshots and GIFs).</summary>
    public static bool IsDemo { get; private set; }

    public static new App Current => (App)Application.Current;

    public AppSettings Settings { get; private set; } = new();
    public ThemeService Theme { get; } = new();
    public UsageService Usage { get; } = new();
    internal MeterViewModel ViewModel { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Dev aid for docs: --ring-png <file.png> <size> <percent> <dark|light> renders one tray ring.
        var ringArg = Array.IndexOf(e.Args, "--ring-png");
        if (ringArg >= 0 && ringArg + 4 < e.Args.Length)
        {
            var pct = double.Parse(e.Args[ringArg + 3], System.Globalization.CultureInfo.InvariantCulture);
            var bmp = TrayIconRenderer.RenderBitmap(int.Parse(e.Args[ringArg + 2]), pct, UsageLevels.FromPercent(pct),
                e.Args[ringArg + 4] == "dark", stale: false);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(e.Args[ringArg + 1])) png.Save(fs);
            Shutdown();
            return;
        }
        var sheetArg = Array.IndexOf(e.Args, "--icon-sheet");
        if (sheetArg >= 0 && sheetArg + 1 < e.Args.Length)
        {
            TrayIconRenderer.WriteSheet(e.Args[sheetArg + 1]);
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            // Already running: ask that instance to open its popup, then leave.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (Exception) { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            while (_showEvent.WaitOne())
                Dispatcher.BeginInvoke(() => TogglePopup(forceOpen: true));
        }) { IsBackground = true, Name = "ShowPopupListener" };
        listener.Start();

#if !DEBUG
        // If Start with Windows is on but points at an older path (the exe was moved or a newer copy
        // saved elsewhere), follow this exe. Only on a plain launch of a Release build, so dev, demo
        // and screenshot runs never take over the entry.
        if (e.Args.Length == 0) StartupService.FollowCurrentExe();
#endif

        _openPopupWhenReady = e.Args.Contains("--popup", StringComparer.OrdinalIgnoreCase);
        // Software rendering: for a UI this small it looks identical and saves ~30 MB of GPU-side
        // allocations (measured: 39 vs 70 MB private bytes idle). The DWM backdrop is unaffected.
        if (!e.Args.Contains("--gpu", StringComparer.OrdinalIgnoreCase))
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        Settings = SettingsStore.Load();
        if (e.Args.Contains("--light", StringComparer.OrdinalIgnoreCase)) Theme.Override = "light";
        if (e.Args.Contains("--dark", StringComparer.OrdinalIgnoreCase)) Theme.Override = "dark";
        Theme.Refresh(raise: false);
        Theme.Changed += OnThemeChanged;

        ViewModel = new MeterViewModel(Usage);
        _tray = new TrayIcon();
        _tray.Click += () => TogglePopup();
        _tray.RightClick += () => ShowMenu(null);
        _tray.ThemeOrSettingsChanged += () => Theme.Refresh();
        _tray.DisplayChanged += RedrawTrayIcon;
        _tray.TaskbarRecreated += RedrawTrayIcon;
        _tray.BalloonClick += () => TogglePopup(forceOpen: true);
        _tray.Show(TrayIconRenderer.Render(null, UsageLevel.Calm, Theme.TaskbarDark, false, ""), "Claude Usage Meter\nReading usage...");

        Usage.StateChanged += OnUsageChanged;
        Usage.Configure(Settings.CliPath, Settings.RefreshMinutes);
        IsDemo = e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--demo-cycle", StringComparer.OrdinalIgnoreCase);
        if (IsDemo) Usage.UseDemoData(cycle: e.Args.Contains("--demo-cycle", StringComparer.OrdinalIgnoreCase));
        ViewModel.SetStripHidden(Settings.StripHiddenBlocks);
        ApplyDisplayMode();
        Usage.Start();

        _tick.Tick += (_, _) =>
        {
            Usage.Recompute();   // staleness ages out
        };
        _tick.Start();

        // After sleep the numbers are old: refresh shortly after resume (network needs a moment).
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, pe) =>
        {
            if (pe.Mode != Microsoft.Win32.PowerModes.Resume) return;
            Dispatcher.BeginInvoke(() =>
            {
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
                t.Tick += (_, _) => { t.Stop(); _ = Usage.RefreshAsync(); };
                t.Start();
            });
        };

        if (e.Args.Contains("--test-alert", StringComparer.OrdinalIgnoreCase))
        {
            // Dev aid: shows what an alert looks like, without touching the real alert bookkeeping.
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { t.Stop(); ShowAlert(new Alert("demo", "Current week (all models)", 80, 82, DateTimeOffset.Now.AddHours(7).AddMinutes(12))); };
            t.Start();
        }
        if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) Dispatcher.BeginInvoke(OpenSettings, DispatcherPriority.ApplicationIdle);
        if (e.Args.Contains("--about", StringComparer.OrdinalIgnoreCase)) Dispatcher.BeginInvoke(OpenAbout, DispatcherPriority.ApplicationIdle);
    }

    private DateTimeOffset? _lastAlertCheck;

    private void OnUsageChanged()
    {
        UpdateTrayIcon();
        // Only fresh reads drive alerts (Recompute ticks re-raise StateChanged with the same data).
        if (Usage.State.DataTime is { } t && t != _lastAlertCheck)
        {
            _lastAlertCheck = t;
            CheckAlerts();
        }
        if (_popup is null || !_popup.IsVisible) TrimMemorySoon();
        if (_openPopupWhenReady && Usage.State.LastAttempt is not null)
        {
            _openPopupWhenReady = false;
            TogglePopup(forceOpen: true);
        }
    }

    private void OnThemeChanged()
    {
        _popup?.ApplyTheme(Theme.AppsDark);
        _strip?.SetTaskbarTheme(Theme.TaskbarDark);
        RedrawTrayIcon();
    }

    // ---------------- Display mode ----------------

    private StripWindow? _strip;

    internal void SetDisplayMode(DisplayMode mode)
    {
        Settings.DisplayMode = mode;
        SettingsStore.Save(Settings);
        ApplyDisplayMode();
    }

    private void ApplyDisplayMode()
    {
        var mode = Settings.DisplayMode;
        if (mode is DisplayMode.TaskbarStrip or DisplayMode.Both)
        {
            if (_strip is null)
            {
                _strip = new StripWindow(ViewModel, Settings);
                _strip.SetTaskbarTheme(Theme.TaskbarDark);
                _strip.Clicked += rect => TogglePopup(anchor: rect);
                _strip.RightClicked += () => ShowMenu(null);
                _strip.AvailabilityChanged += _ => UpdateTrayVisibility();
                _strip.SetTooltip(TooltipText(Usage.State));
            }
            _strip.Start();
        }
        else
        {
            _strip?.Stop();
        }
        UpdateTrayVisibility();
    }

    /// <summary>The tray icon shows unless the user chose strip only and the strip can actually be shown.</summary>
    private void UpdateTrayVisibility()
    {
        var stripOnly = Settings.DisplayMode == DisplayMode.TaskbarStrip && _strip is { IsAvailable: true };
        _tray?.SetVisible(!stripOnly);
    }

    // ---------------- Tray icon ----------------

    private void UpdateTrayIcon()
    {
        if (_tray is null) return;
        var s = Usage.State;
        var primary = s.Primary;
        var mark = s.Status switch
        {
            UsageStatus.LoggedOut => "!",
            UsageStatus.CliNotFound => "?",
            _ when s.LastAttempt is null => "",
            _ => "-",
        };
        var tip = TooltipText(s);
        _strip?.SetTooltip(tip);
        var key =$"{primary?.Percent}|{primary?.Level}|{Theme.TaskbarDark}|{s.IsStale}|{mark}|{TrayIconRenderer.IconSize()}|{tip}";
        if (key == _trayKey) return;
        _trayKey = key;
        var icon = TrayIconRenderer.Render(primary?.Percent, primary?.Level ?? UsageLevel.Calm, Theme.TaskbarDark, s.IsStale, mark);
        _tray.Update(icon, tip);
    }

    private string? _trayKey;

    /// <summary>Force a redraw (taskbar recreated, DPI or theme changed).</summary>
    private void RedrawTrayIcon()
    {
        _trayKey = null;
        UpdateTrayIcon();
    }

    private static string TooltipText(DisplayState s)
    {
        var sb = new StringBuilder("Claude Usage Meter");
        if (s.HasData)
        {
            var now = DateTimeOffset.Now;
            foreach (var b in s.Blocks)
            {
                var label = b.Label.Replace("Current ", "", StringComparison.OrdinalIgnoreCase);
                label = char.ToUpperInvariant(label[0]) + label[1..];
                var line = $"\n{label}: {Math.Round(b.Percent)}%";
                if (b.ResetsAt is { } at) line += $", {Countdown.Format(at - now)}";
                sb.Append(line);
            }
            if (s.IsStale) sb.Append("\n(not refreshed)");
        }
        else if (s.LastAttempt is null)
        {
            sb.Append("\nReading usage...");
        }
        else
        {
            sb.Append(s.Status switch
            {
                UsageStatus.LoggedOut => "\nSign in again (click for the button)",
                UsageStatus.CliNotFound => "\nClaude Code CLI not found",
                UsageStatus.Offline => "\nOffline",
                _ => "\nCouldn't read usage",
            });
        }
        return sb.ToString();
    }

    // ---------------- Popup ----------------

    private PopupWindow EnsurePopup()
    {
        if (_popup is not null) return _popup;
        _popup = new PopupWindow(ViewModel);
        _popup.RefreshRequested += () => _ = Usage.RefreshAsync();
        _popup.MenuRequested += anchor => ShowMenu(anchor);
        _popup.SignInRequested += StartSignIn;
        _popup.OpenSettingsRequested += OpenSettings;
        return _popup;
    }

    internal void TogglePopup(bool forceOpen = false, Win32.RECT? anchor = null)
    {
        var popup = EnsurePopup();
        if (popup.IsVisible && !forceOpen)
        {
            popup.HideAnimated();
            return;
        }
        // The click that deactivated (and so closed) the popup shouldn't immediately reopen it.
        if (!forceOpen && (DateTime.UtcNow - popup.LastHidden).TotalMilliseconds < 250) return;

        var rect = anchor ?? _tray?.GetIconRect() ?? CursorRect();
        ViewModel.Tick();
        popup.ShowAt(rect, alignToCorner: anchor is null);
        _ = Usage.RefreshIfOlderThan(UsageService.OpenRefreshGap);
    }

    private static Win32.RECT CursorRect()
    {
        Win32.GetCursorPos(out var p);
        return new Win32.RECT(p.X - 8, p.Y - 8, p.X + 8, p.Y + 8);
    }

    // ---------------- Menu (tray icon, strip and the popup's "..." button) ----------------

    internal void ShowMenu(UIElement? anchor)
    {
        var menu = new ContextMenu();

        var display = new MenuItem { Header = "Display mode" };
        foreach (var (mode, label) in new[] { (DisplayMode.TrayPopup, "Tray popup"), (DisplayMode.TaskbarStrip, "Taskbar strip"), (DisplayMode.Both, "Both") })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Settings.DisplayMode == mode };
            item.Click += (_, _) => SetDisplayMode(mode);
            display.Items.Add(item);
        }
        if (Settings.DisplayMode != DisplayMode.TrayPopup)
        {
            display.Items.Add(new Separator());
            var reset = new MenuItem { Header = "Move strip next to the clock" };
            reset.Click += (_, _) => ResetStripPosition();
            display.Items.Add(reset);
            var monitors = _strip?.Monitors() ?? [];
            if (monitors.Count > 1)
            {
                for (var i = 0; i < monitors.Count; i++)
                {
                    var device = monitors[i];
                    var item = new MenuItem { Header = $"Strip on display {i + 1}", IsCheckable = true,
                        IsChecked = string.Equals(device, Settings.Strip.Monitor, StringComparison.OrdinalIgnoreCase) || (i == 0 && string.IsNullOrEmpty(Settings.Strip.Monitor)) };
                    item.Click += (_, _) => _strip?.MoveToMonitor(device);
                    display.Items.Add(item);
                }
            }
        }
        menu.Items.Add(display);

        var refresh = new MenuItem { Header = "Refresh now" };
        refresh.Click += (_, _) => _ = Usage.RefreshAsync();
        menu.Items.Add(refresh);

        var settings = new MenuItem { Header = "Settings..." };
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var startup = new MenuItem { Header = "Start with Windows", IsCheckable = true, IsChecked = StartupService.IsEnabled() };
        startup.Click += (_, _) => StartupService.Set(startup.IsChecked);
        menu.Items.Add(startup);

        menu.Items.Add(new Separator());
        var about = new MenuItem { Header = "About" };
        about.Click += (_, _) => OpenAbout();
        menu.Items.Add(about);
        var quit = new MenuItem { Header = "Quit" };
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);

        if (anchor is not null && _popup is not null && Window.GetWindow(anchor) == _popup)
            _popup.HoldOpenFor(menu);
        OpenMenu(menu, anchor);
    }

    private static void OpenMenu(ContextMenu menu, UIElement? anchor)
    {
        if (anchor is not null)
        {
            // Anchored inside the popup, which is already the foreground window: the menu works as a
            // normal WPF context menu there, so don't steal foreground from the popup.
            menu.PlacementTarget = anchor;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }
        else
        {
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            Win32.GetCursorPos(out var p);
            var dpi = Win32.MonitorDpi(Win32.MonitorFromPoint(p, Win32.MONITOR_DEFAULTTONEAREST)) / 96.0;
            menu.HorizontalOffset = p.X / dpi;
            menu.VerticalOffset = p.Y / dpi;
            menu.Opened += (_, _) =>
            {
                // From the tray or strip nothing of ours is foreground: give it to the menu so it
                // closes on an outside click and takes the keyboard.
                if (System.Windows.Interop.HwndSource.FromVisual(menu) is System.Windows.Interop.HwndSource src)
                    Win32.SetForegroundWindow(src.Handle);
            };
        }
        menu.IsOpen = true;
    }

    // ---------------- Actions ----------------

    internal void StartSignIn()
    {
        _popup?.HideAnimated();
        if (!SignIn.Launch(Settings.CliPath))
        {
            MessageBox.Show("Couldn't find Claude Code to start the sign-in. Set the CLI path in Settings, then try again.", "Claude Usage Meter",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private SettingsWindow? _settingsWindow;
    private AboutWindow? _aboutWindow;

    internal void OpenSettings()
    {
        _popup?.HideAnimated();
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => { _settingsWindow = null; TrimMemorySoon(); };
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    internal void OpenAbout()
    {
        _popup?.HideAnimated();
        if (_aboutWindow is { IsLoaded: true })
        {
            _aboutWindow.Activate();
            return;
        }
        _aboutWindow = new AboutWindow();
        _aboutWindow.Closed += (_, _) => { _aboutWindow = null; TrimMemorySoon(); };
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    /// <summary>Applies the current Settings everywhere and saves them.</summary>
    internal void ApplySettings()
    {
        Settings.Normalize();
        SettingsStore.Save(Settings);
        Usage.Configure(Settings.CliPath, Settings.RefreshMinutes);
        ViewModel.SetStripHidden(Settings.StripHiddenBlocks);
        ApplyDisplayMode();
    }

    internal void ResetStripPosition() => _strip?.ResetPosition();

    // ---------------- Alerts ----------------

    private IntPtr _toastIcon;
    private DispatcherTimer? _rehideTray;

    private void CheckAlerts()
    {
        var s = Usage.State;
        if (IsDemo || !Settings.AlertsEnabled || !s.HasData || s.IsStale) return;
        var alerts = AlertService.Evaluate(s.Blocks, Settings.AlertThresholds, Settings.AlertsShown);
        if (alerts.Count == 0)
        {
            return;
        }
        SettingsStore.Save(Settings); // remember what was shown, across restarts
        foreach (var a in alerts.OrderByDescending(a => a.Threshold)) ShowAlert(a);
    }

    private void ShowAlert(Alert a)
    {
        if (_tray is null) return;
        var title = $"Claude usage at {Math.Round(a.Percent)}%";
        var text = a.Label + (a.ResetsAt is { } at ? $" resets in {Countdown.Format(at - DateTimeOffset.Now)}." : ".");
        if (_toastIcon == IntPtr.Zero) _toastIcon = TrayIconRenderer.LoadAppIcon(48);

        // Notifications come from the tray icon; in strip-only mode show it briefly for the alert.
        if (!_tray.Visible)
        {
            _tray.SetVisible(true);
            _rehideTray ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _rehideTray.Stop();
            _rehideTray.Tick += RehideTray;
            _rehideTray.Start();
        }
        _tray.ShowBalloon(title, text, a.Threshold >= 95, _toastIcon);
    }

    private void RehideTray(object? sender, EventArgs e)
    {
        _rehideTray?.Stop();
        if (_rehideTray is not null) _rehideTray.Tick -= RehideTray;
        UpdateTrayVisibility();
    }

    /// <summary>Hand unused pages back after the popup closes (keeps the idle footprint small).</summary>
    public void TrimMemorySoon()
    {
        _trimTimer ??= new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromSeconds(3) };
        _trimTimer.Stop();
        _trimTimer.Tick -= TrimNow;
        _trimTimer.Tick += TrimNow;
        _trimTimer.Start();
    }

    private void TrimNow(object? sender, EventArgs e)
    {
        _trimTimer?.Stop();
        if (_popup is { IsVisible: true }) return; // trimming under a visible window just causes page faults
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
        Win32.EmptyWorkingSet(Win32.GetCurrentProcess());
    }

    internal void Quit()
    {
        Usage.Stop();
        _tray?.Dispose();
        _tray = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _showEvent?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch (Exception) { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

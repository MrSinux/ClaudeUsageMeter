using System.Runtime.InteropServices;
using System.Windows.Interop;
using static ClaudeUsageMeter.Native.Win32;

namespace ClaudeUsageMeter.Native;

/// <summary>
/// A minimal notification-area icon on top of Shell_NotifyIcon (no WinForms).
/// Also owns a hidden top-level window that receives shell broadcasts (TaskbarCreated,
/// theme and display changes) for the rest of the app.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const int IconId = 1;
    private static readonly int CallbackMessage = WM_APP + 1;
    private readonly int _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly HwndSource _window;
    private IntPtr _hIcon;
    private string _tip = "";
    private bool _added;

    public event Action? Click;
    public event Action? RightClick;
    public event Action? BalloonClick;
    public event Action? ThemeOrSettingsChanged;
    public event Action? DisplayChanged;
    public event Action? TaskbarRecreated;
    /// <summary>Raw messages for other components (e.g. AppBar callbacks).</summary>
    public event Func<int, IntPtr, IntPtr, bool>? Message;

    public IntPtr Handle => _window.Handle;

    public TrayIcon()
    {
        var p = new HwndSourceParameters("ClaudeUsageMeterTrayHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = WS_EX_TOOLWINDOW,
        };
        _window = new HwndSource(p);
        _window.AddHook(WndProc);
    }

    public void Show(IntPtr hIcon, string tip)
    {
        SetIconHandle(hIcon);
        _tip = Clip(tip, 127);
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref data);
        if (!_added)
        {
            // Explorer may still hold a stale icon from a crashed run: replace it.
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = Shell_NotifyIconW(NIM_ADD, ref data);
        }
        data.uTimeoutOrVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, ref data);
    }

    /// <summary>False removes the icon from the notification area (strip-only mode).</summary>
    public bool Visible { get; private set; } = true;

    public void SetVisible(bool visible)
    {
        if (visible == Visible) return;
        Visible = visible;
        if (visible) Show(_hIcon, _tip);
        else
        {
            var data = NewData(0);
            Shell_NotifyIconW(NIM_DELETE, ref data);
            _added = false;
        }
    }

    public void Update(IntPtr hIcon, string tip)
    {
        SetIconHandle(hIcon);
        _tip = Clip(tip, 127);
        if (!Visible) return;
        if (!_added) { Show(_hIcon, _tip); return; }
        var data = NewData(NIF_ICON | NIF_TIP | NIF_SHOWTIP);
        if (!Shell_NotifyIconW(NIM_MODIFY, ref data)) Show(_hIcon, _tip);
    }

    /// <summary>Windows 11 shows this as a native toast.</summary>
    public void ShowBalloon(string title, string text, bool warning, IntPtr largeIcon)
    {
        if (!_added) return;
        var data = NewData(NIF_INFO);
        data.szInfoTitle = Clip(title, 63);
        data.szInfo = Clip(text, 255);
        data.dwInfoFlags = (largeIcon != IntPtr.Zero ? NIIF_USER | NIIF_LARGE_ICON : (warning ? NIIF_WARNING : NIIF_INFO)) | NIIF_RESPECT_QUIET_TIME;
        data.hBalloonIcon = largeIcon;
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    /// <summary>The icon's rectangle on screen in physical pixels, if the shell will say.</summary>
    public RECT? GetIconRect()
    {
        var id = new NOTIFYICONIDENTIFIER { cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), hWnd = Handle, uID = IconId };
        return Shell_NotifyIconGetRect(ref id, out var rect) == 0 && rect.Width > 0 ? rect : null;
    }

    private void SetIconHandle(IntPtr hIcon)
    {
        if (hIcon == _hIcon) return;
        var old = _hIcon;
        _hIcon = hIcon;
        if (old != IntPtr.Zero)
        {
            // Destroy after the shell has the new one.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(() => DestroyIcon(old), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private NOTIFYICONDATAW NewData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = Handle,
        uID = IconId,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _hIcon,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
    };

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)] + "...";

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            var evt = (int)((long)lParam & 0xFFFF);
            switch (evt)
            {
                case NIN_SELECT:
                case NIN_KEYSELECT:
                    Click?.Invoke();
                    break;
                case WM_CONTEXTMENU:
                    RightClick?.Invoke();
                    break;
                case NIN_BALLOONUSERCLICK:
                    BalloonClick?.Invoke();
                    break;
            }
            handled = true;
            return IntPtr.Zero;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            _added = false;
            if (Visible) Show(_hIcon, _tip);
            TaskbarRecreated?.Invoke();
            return IntPtr.Zero;
        }
        switch (msg)
        {
            case WM_SETTINGCHANGE:
            case WM_THEMECHANGED:
                ThemeOrSettingsChanged?.Invoke();
                break;
            case WM_DISPLAYCHANGE:
            case WM_DPICHANGED:
                DisplayChanged?.Invoke();
                break;
        }
        if (Message is { } m && m(msg, wParam, lParam)) handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        var data = NewData(0);
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
        if (_hIcon != IntPtr.Zero) DestroyIcon(_hIcon);
        _hIcon = IntPtr.Zero;
        _window.Dispose();
    }
}

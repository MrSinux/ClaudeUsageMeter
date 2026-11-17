using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static ClaudeUsageMeter.Native.Win32;

namespace ClaudeUsageMeter.Native;

internal static class WindowEffects
{
    /// <summary>Windows 11 22H2+ supports DWMWA_SYSTEMBACKDROP_TYPE.</summary>
    public static bool BackdropSupported => Environment.OSVersion.Version.Build >= 22621;
    public static bool RoundedCornersSupported => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// Rounded corners, dark/light frame and an acrylic (transient) backdrop.
    /// Returns true if the backdrop is active, so the caller can keep its background transparent.
    /// </summary>
    public static bool Apply(Window window, bool dark, int backdrop = DWMSBT_TRANSIENTWINDOW)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;

        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));

        if (RoundedCornersSupported)
        {
            var corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        }

        // Paint the (invisible) caption the same as our surface so "accent colour on title bars"
        // doesn't tint the top of the flyout through the extended frame.
        const int DWMWA_CAPTION_COLOR = 35;
        var caption = dark ? 0x001D130E : 0x00FBF8F7; // COLORREF 0x00BBGGRR: #0E131D / #F7F8FB
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

        if (!BackdropSupported || SystemParameters.HighContrast) return false;

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is not null) source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var type = backdrop;
        return DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int)) == 0;
    }

    /// <summary>For ordinary windows (Settings, About): dark/light title bar with Mica, solid client area.</summary>
    public static void ApplyFrame(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
        if (BackdropSupported)
        {
            var mica = DWMSBT_MAINWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
        }
    }

    public static void MakeToolWindow(Window window, bool noActivate)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        // No system menu or min/max boxes: otherwise DWM draws caption buttons into the extended glass frame.
        // No caption either, so DWM has no caption strip to tint; the (hidden) thick frame keeps the shadow.
        const int GWL_STYLE = -16, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000,
            WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        style &= ~(long)(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_CAPTION);
        style |= WS_THICKFRAME;
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
        var ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW;
        ex &= ~WS_EX_APPWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        const int SWP_FRAMECHANGED = 0x0020;
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public static bool ReducedMotion => !SystemParameters.ClientAreaAnimation;
}

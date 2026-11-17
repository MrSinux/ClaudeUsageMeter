using System.Runtime.InteropServices;
using static ClaudeUsageMeter.Native.Win32;

namespace ClaudeUsageMeter.Native;

public enum TaskbarEdge { Bottom, Top, Left, Right }

/// <summary>Facts about a monitor's taskbar, in physical pixels.</summary>
internal sealed record TaskbarInfo(IntPtr Monitor, string Device, RECT MonitorRect, RECT WorkRect, RECT TaskbarRect, TaskbarEdge Edge, uint Dpi, bool AutoHide, IntPtr Hwnd)
{
    public double Scale => Dpi / 96.0;
    public bool IsHorizontal => Edge is TaskbarEdge.Bottom or TaskbarEdge.Top;
}

internal static class Taskbar
{
    /// <summary>Monitor work area and taskbar edge for the monitor nearest a point.</summary>
    public static (RECT work, RECT monitor, TaskbarEdge edge, uint dpi) MonitorAt(POINT p)
    {
        var mon = MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST);
        var mi = MonitorInfo(mon);
        return (mi.rcWork, mi.rcMonitor, EdgeFromWork(mi.rcWork, mi.rcMonitor), MonitorDpi(mon));
    }

    public static TaskbarEdge EdgeFromWork(RECT work, RECT monitor)
    {
        if (work.Bottom < monitor.Bottom) return TaskbarEdge.Bottom;
        if (work.Top > monitor.Top) return TaskbarEdge.Top;
        if (work.Left > monitor.Left) return TaskbarEdge.Left;
        if (work.Right < monitor.Right) return TaskbarEdge.Right;
        // Auto-hide or no reserved space: ask the shell.
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(ABM_GETTASKBARPOS, ref abd) != IntPtr.Zero)
        {
            return abd.uEdge switch
            {
                ABE_TOP => TaskbarEdge.Top,
                ABE_LEFT => TaskbarEdge.Left,
                ABE_RIGHT => TaskbarEdge.Right,
                _ => TaskbarEdge.Bottom,
            };
        }
        return TaskbarEdge.Bottom;
    }

    public static bool IsAutoHide()
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
        return ((long)SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) != 0;
    }

    /// <summary>All taskbars: the primary (Shell_TrayWnd) and secondary ones (Shell_SecondaryTrayWnd).</summary>
    public static List<TaskbarInfo> All()
    {
        var list = new List<TaskbarInfo>();
        var autoHide = IsAutoHide();
        void Add(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return;
            var center = new POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
            var mon = MonitorFromPoint(center, MONITOR_DEFAULTTONEAREST);
            var mi = MonitorInfo(mon);
            var edge = EdgeFromRect(r, mi.rcMonitor);
            list.Add(new TaskbarInfo(mon, mi.szDevice, mi.rcMonitor, mi.rcWork, r, edge, MonitorDpi(mon), autoHide, hwnd));
        }
        Add(FindWindow("Shell_TrayWnd", null));
        var after = IntPtr.Zero;
        while ((after = FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) Add(after);
        return list;
    }

    private static TaskbarEdge EdgeFromRect(RECT tb, RECT mon)
    {
        var horizontal = tb.Width >= tb.Height;
        if (horizontal) return Math.Abs(tb.Top - mon.Top) < Math.Abs(mon.Bottom - tb.Bottom) ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        return Math.Abs(tb.Left - mon.Left) < Math.Abs(mon.Right - tb.Right) ? TaskbarEdge.Left : TaskbarEdge.Right;
    }

    /// <summary>
    /// Where a flyout of the given physical size goes: against the taskbar edge, near the anchor,
    /// inside the work area, with the Windows 11 flyout margin.
    /// </summary>
    /// <param name="alignToCorner">Tray flyouts sit in the corner next to the clock like Windows 11's own; strip flyouts centre on the strip.</param>
    public static POINT FlyoutPosition(RECT anchor, int width, int height, bool alignToCorner)
    {
        var center = new POINT((anchor.Left + anchor.Right) / 2, (anchor.Top + anchor.Bottom) / 2);
        var (work, monitor, edge, dpi) = MonitorAt(center);
        var m = (int)Math.Round(12 * dpi / 96.0);
        int x, y;
        switch (edge)
        {
            case TaskbarEdge.Top:
                x = alignToCorner ? work.Right - width - m : center.X - width / 2; y = work.Top + m; break;
            case TaskbarEdge.Left:
                x = work.Left + m; y = alignToCorner ? work.Bottom - height - m : center.Y - height / 2; break;
            case TaskbarEdge.Right:
                x = work.Right - width - m; y = alignToCorner ? work.Bottom - height - m : center.Y - height / 2; break;
            default:
                x = alignToCorner ? work.Right - width - m : center.X - width / 2; y = work.Bottom - height - m; break;
        }
        // If the work area doesn't exclude the taskbar (auto-hide), keep clear of the monitor edge too.
        if (work.Bottom == monitor.Bottom && edge == TaskbarEdge.Bottom) y = Math.Min(y, anchor.Top - height - m);
        x = Math.Clamp(x, work.Left + m, Math.Max(work.Left + m, work.Right - width - m));
        y = Math.Clamp(y, work.Top + m, Math.Max(work.Top + m, work.Bottom - height - m));
        return new POINT(x, y);
    }
}

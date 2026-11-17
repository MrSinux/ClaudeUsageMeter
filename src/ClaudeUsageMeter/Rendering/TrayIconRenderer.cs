using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeUsageMeter.Core;
using ClaudeUsageMeter.Native;

namespace ClaudeUsageMeter.Rendering;

/// <summary>Draws the tray icon: a ring filled to the % used, with the number inside.</summary>
internal static class TrayIconRenderer
{
    public static int IconSize()
    {
        try { return Win32.GetSystemMetricsForDpi(Win32.SM_CXSMICON, Win32.GetDpiForSystem()); }
        catch (Exception) { return 16; }
    }

    /// <param name="percent">Null draws the "no data" state.</param>
    /// <param name="mark">"!" or "?" style glyph for error states, used when percent is null.</param>
    public static IntPtr Render(double? percent, UsageLevel level, bool taskbarDark, bool stale, string mark = "")
    {
        var size = IconSize();
        return ToHIcon(RenderBitmap(size, percent, level, taskbarDark, stale, mark), size);
    }

    public static BitmapSource RenderBitmap(int size, double? percent, UsageLevel level, bool taskbarDark, bool stale, string mark = "")
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Draw(dc, size, percent, level, taskbarDark, stale, mark);
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Dev aid: writes every icon state at common sizes onto one sheet (dark and light taskbar).</summary>
    public static void WriteSheet(string path)
    {
        int[] sizes = [16, 20, 24, 32];
        (double? p, UsageLevel l, bool stale, string mark)[] states =
            [(8, UsageLevel.Calm, false, ""), (42, UsageLevel.Calm, false, ""), (74, UsageLevel.Amber, false, ""), (91, UsageLevel.Red, false, ""),
             (100, UsageLevel.Red, false, ""), (74, UsageLevel.Amber, true, ""), (null, UsageLevel.Calm, false, "!"), (null, UsageLevel.Calm, false, "-")];
        const int cell = 40, zoom = 3;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (var t = 0; t < 2; t++)
            {
                var dark = t == 0;
                dc.DrawRectangle(new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x22, 0x2E) : Color.FromRgb(0xEE, 0xF0, 0xF4)), null,
                    new Rect(0, t * sizes.Length * cell, states.Length * cell, sizes.Length * cell));
                for (var si = 0; si < sizes.Length; si++)
                for (var i = 0; i < states.Length; i++)
                {
                    var s = states[i];
                    var bmp = RenderBitmap(sizes[si], s.p, s.l, dark, s.stale, s.mark);
                    dc.DrawImage(bmp, new Rect(i * cell + (cell - sizes[si]) / 2.0, (t * sizes.Length + si) * cell + (cell - sizes[si]) / 2.0, sizes[si], sizes[si]));
                }
            }
        }
        var w = states.Length * cell; var h = 2 * sizes.Length * cell;
        var sheet = new RenderTargetBitmap(w * zoom, h * zoom, 96 * zoom, 96 * zoom, PixelFormats.Pbgra32);
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        sheet.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(sheet));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    public static Color LevelColor(UsageLevel level, bool dark) => level switch
    {
        UsageLevel.Red => dark ? Color.FromRgb(0xFF, 0x5F, 0x66) : Color.FromRgb(0xD1, 0x34, 0x38),
        UsageLevel.Amber => dark ? Color.FromRgb(0xF7, 0xB1, 0x2E) : Color.FromRgb(0xC2, 0x70, 0x00),
        _ => dark ? Color.FromRgb(0x5B, 0x8D, 0xE0) : Color.FromRgb(0x00, 0x67, 0xC0),
    };

    private static void Draw(DrawingContext dc, int size, double? percent, UsageLevel level, bool dark, bool stale, string mark)
    {
        var ink = dark ? Color.FromRgb(0xF4, 0xF6, 0xFB) : Color.FromRgb(0x11, 0x18, 0x27);
        var thickness = Math.Max(2.0, Math.Round(size * 0.13 * 2) / 2);
        var radius = size / 2.0 - thickness / 2.0 - 0.25;
        var center = new Point(size / 2.0, size / 2.0);

        var track = new Pen(new SolidColorBrush(Color.FromArgb(dark ? (byte)0x55 : (byte)0x40, ink.R, ink.G, ink.B)), thickness);
        dc.DrawEllipse(null, track, center, radius, radius);

        if (percent is { } p)
        {
            var color = LevelColor(level, dark);
            if (stale) color = Color.FromArgb(0x99, color.R, color.G, color.B);
            var arcPen = new Pen(new SolidColorBrush(color), thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
            var fraction = Math.Clamp(p / 100.0, 0, 1);
            if (fraction >= 0.999)
            {
                dc.DrawEllipse(null, arcPen, center, radius, radius);
            }
            else if (fraction > 0.005)
            {
                var angle = fraction * 2 * Math.PI;
                var start = new Point(center.X, center.Y - radius);
                var end = new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle));
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(start, false, false);
                    g.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, arcPen, geo);
            }
        }

        var text = percent is { } v ? Math.Round(v).ToString(CultureInfo.InvariantCulture) : mark;
        if (text.Length == 0) return;
        var inner = size - 2 * thickness;
        var face = new Typeface(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.SemiCondensed);
        double fontSize = inner * (text.Length >= 3 ? 0.62 : 0.86);
        FormattedText ft;
        while (true)
        {
            ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fontSize,
                new SolidColorBrush(stale ? Color.FromArgb(0xB0, ink.R, ink.G, ink.B) : ink), 1.0);
            // Small icons need every pixel for legibility; larger ones get breathing room inside the ring.
            var maxWidth = size <= 20 ? inner + 1 : inner * 0.9;
            if (ft.Width <= maxWidth || fontSize < 5) break;
            fontSize -= 0.5;
        }
        // Centre the ink (not the line box) so digits sit optically in the middle of the ring.
        var inkBottom = ft.Height + ft.OverhangAfter;
        var inkMiddle = inkBottom - ft.Extent / 2;
        var x = center.X - ft.Width / 2;
        var y = center.Y - inkMiddle;
        dc.DrawText(ft, new Point(x, y));
    }

    private static IntPtr ToHIcon(BitmapSource bmp, int size)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        var bytes = ms.ToArray();
        // Vista+ accepts PNG data directly.
        return Win32.CreateIconFromResourceEx(bytes, bytes.Length, true, 0x00030000, size, size, 0);
    }

    /// <summary>The app icon (ring) as an HICON, for toasts.</summary>
    public static IntPtr LoadAppIcon(int size)
    {
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/gauge-160.png");
            var src = new BitmapImage();
            src.BeginInit();
            src.UriSource = uri;
            src.DecodePixelWidth = size;
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.EndInit();
            return ToHIcon(src, size);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }
}

using System.Windows;
using Microsoft.Win32;

namespace ClaudeUsageMeter.Services;

/// <summary>Follows Windows light/dark: apps mode for windows, system mode for the taskbar/tray.</summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public bool AppsDark { get; private set; } = true;
    public bool TaskbarDark { get; private set; } = true;

    public event Action? Changed;

    /// <summary>Dev/screenshot override: "light" or "dark" regardless of Windows.</summary>
    public string? Override { get; set; }

    public void Refresh(bool raise = true)
    {
        var appsDark = Override is { } o ? o == "dark" : !ReadFlag("AppsUseLightTheme", false);
        var taskbarDark = Override is { } o2 ? o2 == "dark" : !ReadFlag("SystemUsesLightTheme", false);
        var changed = appsDark != AppsDark || taskbarDark != TaskbarDark;
        AppsDark = appsDark;
        TaskbarDark = taskbarDark;
        Apply();
        if (changed && raise) Changed?.Invoke();
    }

    private static bool ReadFlag(string name, bool fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int v ? v != 0 : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private void Apply()
    {
        var uri = new Uri(AppsDark ? "Themes/Colors.Dark.xaml" : "Themes/Colors.Light.xaml", UriKind.Relative);
        var dicts = Application.Current.Resources.MergedDictionaries;
        var colors = new ResourceDictionary { Source = uri };
        if (dicts.Count > 0 && dicts[0].Source?.OriginalString.StartsWith("Themes/Colors.", StringComparison.Ordinal) == true)
            dicts[0] = colors;
        else
            dicts.Insert(0, colors);
    }
}

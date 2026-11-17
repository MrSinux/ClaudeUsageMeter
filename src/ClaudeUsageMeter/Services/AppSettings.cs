using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Services;

public enum DisplayMode { TrayPopup, TaskbarStrip, Both }

public sealed class StripPlacement
{
    /// <summary>Monitor device name, e.g. \\.\DISPLAY1.</summary>
    public string Monitor { get; set; } = "";
    /// <summary>Left edge of the strip as a fraction of the taskbar length (0..1), so it survives resolution changes.</summary>
    public double Offset { get; set; } = -1;
}

public sealed class AppSettings
{
    public const int MinRefreshMinutes = 1, MaxRefreshMinutes = 15, DefaultRefreshMinutes = 2;

    public int RefreshMinutes { get; set; } = DefaultRefreshMinutes;
    public DisplayMode DisplayMode { get; set; } = DisplayMode.Both;
    /// <summary>Block keys hidden on the taskbar strip. Hidden-list (not shown-list) so new blocks appear by default.</summary>
    public List<string> StripHiddenBlocks { get; set; } = [];
    public bool AlertsEnabled { get; set; } = true;
    public List<int> AlertThresholds { get; set; } = [80, 95];
    /// <summary>Empty means auto-detect.</summary>
    public string? CliPath { get; set; }
    public StripPlacement Strip { get; set; } = new();
    /// <summary>Highest alert threshold already shown per block key, per reset.</summary>
    public Dictionary<string, AlertMark> AlertsShown { get; set; } = [];

    public void Normalize()
    {
        RefreshMinutes = Math.Clamp(RefreshMinutes, MinRefreshMinutes, MaxRefreshMinutes);
        AlertThresholds = AlertThresholds.Where(t => t is > 0 and <= 100).Distinct().OrderBy(t => t).ToList();
        StripHiddenBlocks ??= [];
        AlertsShown ??= [];
        Strip ??= new StripPlacement();
        if (string.IsNullOrWhiteSpace(CliPath)) CliPath = null;
    }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeUsageMeter");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
                s.Normalize();
                return s;
            }
        }
        catch (Exception)
        {
            // A broken file shouldn't stop the app; keep a copy for diagnosis and start fresh.
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch (Exception) { }
        }
        var fresh = new AppSettings();
        fresh.Normalize();
        return fresh;
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Settings are a convenience; never crash over them.
        }
    }
}

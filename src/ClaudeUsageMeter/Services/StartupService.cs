using ClaudeUsageMeter.Core;
using Microsoft.Win32;

namespace ClaudeUsageMeter.Services;

/// <summary>"Start with Windows" through the per-user Run key (no admin needed).</summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeUsageMeter";

    private static string ExePath => Environment.ProcessPath ?? "";

    private static string? Registered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    public static bool IsEnabled()
    {
        try
        {
            return AutostartEntry.Matches(Registered(), ExePath);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled) key.SetValue(ValueName, AutostartEntry.CommandFor(ExePath));
            else if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// If Start with Windows is on but registered for another path (the exe was moved, or a newer
    /// copy was saved somewhere else), point it at this exe. Does nothing when it is off.
    /// </summary>
    public static void FollowCurrentExe()
    {
        try
        {
            var command = AutostartEntry.Repair(Registered(), Environment.ProcessPath);
            if (command is null) return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(ValueName, command);
        }
        catch (Exception)
        {
            // Not critical: the menu shows Start with Windows as off and the user can turn it on.
        }
    }
}

namespace ClaudeUsageMeter.Core;

/// <summary>
/// The value stored in the per-user Run key for "Start with Windows", and the rule that keeps it
/// pointing at the exe that is actually running when the app is moved or replaced by a newer copy.
/// </summary>
public static class AutostartEntry
{
    /// <summary>The Run key command for an exe: its full path, quoted.</summary>
    public static string CommandFor(string exePath) => $"\"{exePath}\"";

    /// <summary>True if the registered command already starts this exe.</summary>
    public static bool Matches(string? registered, string exePath)
    {
        if (string.IsNullOrWhiteSpace(registered)) return false;
        var path = registered.Trim();
        if (path.Length >= 2 && path[0] == '"')
        {
            var end = path.IndexOf('"', 1);
            path = end > 0 ? path[1..end] : path.Trim('"');
        }
        try
        {
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The command to write so that an enabled autostart follows this exe, or null to leave the
    /// Run key alone: when autostart is off (no value), when it already points here, or when this
    /// process isn't the app's own exe (for example <c>dotnet ClaudeUsageMeter.dll</c>).
    /// </summary>
    public static string? Repair(string? registered, string? exePath)
    {
        if (string.IsNullOrWhiteSpace(registered) || string.IsNullOrWhiteSpace(exePath)) return null;
        if (!string.Equals(Path.GetFileNameWithoutExtension(exePath), "ClaudeUsageMeter", StringComparison.OrdinalIgnoreCase)) return null;
        return Matches(registered, exePath) ? null : CommandFor(exePath);
    }
}

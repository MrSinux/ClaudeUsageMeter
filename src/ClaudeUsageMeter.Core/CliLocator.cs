using System.Text.RegularExpressions;

namespace ClaudeUsageMeter.Core;

/// <summary>
/// Finds the Claude Code CLI: a configured path first, then PATH, then the usual install
/// locations, including the copy bundled with the VS Code extension (newest version wins).
/// </summary>
public static partial class CliLocator
{
    public static string? Locate(string? configuredPath = null)
    {
        foreach (var candidate in Candidates(configuredPath))
        {
            if (IsUsable(candidate)) return candidate;
        }
        return null;
    }

    public static bool IsUsable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) && IsRunnableExtension(path);

    private static bool IsRunnableExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".cmd" or ".bat";

    public static IEnumerable<string> Candidates(string? configuredPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
            yield return Environment.ExpandEnvironmentVariables(configuredPath.Trim().Trim('"'));

        // 1. PATH, same order as `where claude`: claude.exe beats claude.cmd within a folder.
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string cleanDir;
            try { cleanDir = Environment.ExpandEnvironmentVariables(dir.Trim('"')); }
            catch (Exception) { continue; }
            foreach (var name in new[] { "claude.exe", "claude.cmd", "claude.bat" })
            {
                string full;
                try { full = Path.Combine(cleanDir, name); }
                catch (ArgumentException) { continue; }
                yield return full;
            }
        }

        // 2. Usual install locations.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(home, ".local", "bin", "claude.exe");          // native installer
        yield return Path.Combine(localAppData, "Programs", "claude", "claude.exe");
        yield return Path.Combine(appData, "npm", "claude.cmd");                // npm global install
        yield return Path.Combine(localAppData, "pnpm", "claude.cmd");
        yield return Path.Combine(home, "scoop", "shims", "claude.exe");

        // 3. Copy bundled with the editor extension (VS Code, Insiders, Cursor, Windsurf...).
        foreach (var editorDir in new[] { ".vscode", ".vscode-insiders", ".cursor", ".windsurf", ".vscodium" })
        {
            var extRoot = Path.Combine(home, editorDir, "extensions");
            foreach (var exe in BundledInExtensions(extRoot)) yield return exe;
        }
    }

    /// <summary>claude.exe inside anthropic.claude-code-* folders, newest version first.</summary>
    public static IEnumerable<string> BundledInExtensions(string extensionsRoot)
    {
        string[] dirs;
        try
        {
            if (!Directory.Exists(extensionsRoot)) yield break;
            dirs = Directory.GetDirectories(extensionsRoot, "anthropic.claude-code-*");
        }
        catch (Exception) { yield break; }

        foreach (var dir in OrderByVersionDescending(dirs))
        {
            yield return Path.Combine(dir, "resources", "native-binary", "claude.exe");
        }
    }

    [GeneratedRegex(@"anthropic\.claude-code-(?<v>\d+(?:\.\d+){1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionVersion();

    public static IEnumerable<string> OrderByVersionDescending(IEnumerable<string> extensionDirs) =>
        extensionDirs
            .Select(d => (dir: d, ver: ParseVersion(Path.GetFileName(d.TrimEnd('\\', '/')))))
            .OrderByDescending(x => x.ver)
            .Select(x => x.dir);

    private static Version ParseVersion(string folderName)
    {
        var m = ExtensionVersion().Match(folderName);
        return m.Success && Version.TryParse(m.Groups["v"].Value, out var v) ? v : new Version(0, 0);
    }
}

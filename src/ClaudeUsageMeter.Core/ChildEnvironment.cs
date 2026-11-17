using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ClaudeUsageMeter.Core;

/// <summary>
/// Builds a clean environment for the CLI child process so a background /usage poll can never be
/// mistaken for, or attach to, a running Claude Code session.
///
/// Why: a process started from inside a Claude Code session (VS Code, a terminal, Remote Control)
/// inherits that session's identity and IPC handles, e.g. CLAUDE_CODE_SESSION_ID,
/// CLAUDE_CODE_MESSAGING_SOCKET/TOKEN, CLAUDE_CODE_CHILD_SESSION, CLAUDE_PID, CLAUDE_CODE_ENTRYPOINT
/// and the IDE's VSCODE_* variables. On 2026-11-11 a /usage run made with those inherited coincided
/// with the parent VS Code session being interrupted. We drop every such variable unless the user
/// set it persistently (HKCU\Environment or the machine environment), so real user configuration
/// such as CLAUDE_CONFIG_DIR or CLAUDE_CODE_GIT_BASH_PATH still reaches the CLI.
/// </summary>
public static partial class ChildEnvironment
{
    [GeneratedRegex(@"^(CLAUDECODE$|CLAUDE_|ANTHROPIC_|VSCODE_|MCP_|ELECTRON_|TERM_PROGRAM|GIT_ASKPASS|ENABLE_IDE_INTEGRATION|NODE_OPTIONS$)", RegexOptions.IgnoreCase)]
    private static partial Regex SessionScoped();

    /// <summary>Variables we always set for the child.</summary>
    public static readonly IReadOnlyDictionary<string, string> Forced = new Dictionary<string, string>
    {
        ["NO_COLOR"] = "1",
        ["FORCE_COLOR"] = "0",
        // No autoupdater, error reporting or telemetry pings triggered by our polling.
        // Not CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: with it, /usage only shows limits another
        // session fetched recently and prints the panel without them otherwise (CLI 2.1.319 to 2.1.322).
        ["DISABLE_TELEMETRY"] = "1",
        ["DISABLE_ERROR_REPORTING"] = "1",
        ["DISABLE_AUTOUPDATER"] = "1",
        // Never try to attach to an editor.
        ["CLAUDE_CODE_AUTO_CONNECT_IDE"] = "false",
    };

    public static void Apply(IDictionary<string, string?> env) =>
        Apply(env, PersistentVariableNames());

    /// <summary>Pure version for tests.</summary>
    public static void Apply(IDictionary<string, string?> env, ISet<string> persistentNames)
    {
        foreach (var key in env.Keys.ToList())
        {
            if (SessionScoped().IsMatch(key) && !persistentNames.Contains(key)) env.Remove(key);
        }
        foreach (var (k, v) in Forced) env[k] = v;
    }

    public static bool IsSessionScoped(string name) => SessionScoped().IsMatch(name);

    /// <summary>Names the user or machine defined on purpose (what a process started from Explorer would see).</summary>
    public static ISet<string> PersistentVariableNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return names;
        try
        {
            using (var user = Registry.CurrentUser.OpenSubKey("Environment"))
                if (user is not null) names.UnionWith(user.GetValueNames());
            using (var machine = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                if (machine is not null) names.UnionWith(machine.GetValueNames());
        }
        catch (Exception) { /* no registry access: treat nothing as persistent */ }
        return names;
    }
}

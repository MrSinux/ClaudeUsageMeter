using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

/// <summary>
/// A background /usage poll must never look like part of a running Claude Code session.
/// </summary>
public class IsolationTests
{
    [Fact]
    public void Session_identity_and_ipc_variables_are_dropped()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["CLAUDECODE"] = "1",
            ["CLAUDE_CODE_SESSION_ID"] = "test-session-0000",
            ["CLAUDE_CODE_MESSAGING_SOCKET"] = @"\\.\pipe\LOCAL\cc-msg-x",
            ["CLAUDE_CODE_MESSAGING_TOKEN"] = "secret",
            ["CLAUDE_CODE_CHILD_SESSION"] = "1",
            ["CLAUDE_CODE_ENTRYPOINT"] = "claude-vscode",
            ["CLAUDE_CODE_SSE_PORT"] = "12345",
            ["CLAUDE_PID"] = "1111",
            ["VSCODE_PID"] = "2222",
            ["VSCODE_IPC_HOOK"] = @"\\.\pipe\x",
            ["MCP_CONNECTION_NONBLOCKING"] = "true",
            ["TERM_PROGRAM"] = "vscode",
            ["ELECTRON_RUN_AS_NODE"] = "1",
            ["PATH"] = @"C:\Windows",
            ["USERPROFILE"] = @"C:\Users\x",
            ["CLAUDE_CONFIG_DIR"] = @"D:\claude",           // set persistently by the user: keep
            ["CLAUDE_CODE_GIT_BASH_PATH"] = @"C:\Git\bash.exe", // set persistently by the user: keep
        };
        var persistent = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PATH", "CLAUDE_CONFIG_DIR", "CLAUDE_CODE_GIT_BASH_PATH" };

        ChildEnvironment.Apply(env, persistent);

        foreach (var gone in new[] { "CLAUDECODE", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN",
                     "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_SSE_PORT", "CLAUDE_PID",
                     "VSCODE_PID", "VSCODE_IPC_HOOK", "MCP_CONNECTION_NONBLOCKING", "TERM_PROGRAM", "ELECTRON_RUN_AS_NODE" })
        {
            Assert.False(env.ContainsKey(gone), gone);
        }
        Assert.Equal(@"C:\Windows", env["PATH"]);
        Assert.Equal(@"C:\Users\x", env["USERPROFILE"]);
        Assert.Equal(@"D:\claude", env["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(@"C:\Git\bash.exe", env["CLAUDE_CODE_GIT_BASH_PATH"]);
        Assert.Equal("1", env["NO_COLOR"]);
        Assert.Equal("1", env["DISABLE_TELEMETRY"]);
        Assert.False(env.ContainsKey("CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC")); // it hides the limits
    }

    [Fact]
    public async Task Child_process_really_gets_the_clean_environment_and_safe_mode()
    {
        // A fake "claude.cmd" that prints its arguments and environment instead of running anything.
        var dir = Path.Combine(Path.GetTempPath(), "usage-meter-iso-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var fake = Path.Combine(dir, "claude.cmd");
        File.WriteAllText(fake, "@echo off\r\necho ARGS=%*\r\nset\r\n");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", "parent-session-should-not-leak");
        Environment.SetEnvironmentVariable("CLAUDE_CODE_MESSAGING_SOCKET", @"\\.\pipe\should-not-leak");
        try
        {
            var r = await ClaudeCli.RunUsageAsync(fake, TimeSpan.FromSeconds(20));
            Assert.False(r.TimedOut);
            Assert.Contains("ARGS=-p /usage --output-format text --no-session-persistence --safe-mode", r.StdOut);
            Assert.DoesNotContain("should-not-leak", r.StdOut);
            Assert.DoesNotContain("CLAUDE_CODE_SESSION_ID=", r.StdOut);
            Assert.Contains("DISABLE_TELEMETRY=1", r.StdOut);
            Assert.Contains("NO_COLOR=1", r.StdOut);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_SESSION_ID", null);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_MESSAGING_SOCKET", null);
            Directory.Delete(dir, recursive: true);
        }
    }
}

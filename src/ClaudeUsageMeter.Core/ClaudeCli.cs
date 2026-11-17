using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClaudeUsageMeter.Core;

public sealed record CliRunResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

/// <summary>
/// Runs exactly one command: <c>claude -p /usage --output-format text --no-session-persistence</c>.
/// /usage is a local slash command: it prints the usage panel and does not start a model turn
/// (verified by hand on 2026-11-11: 0 turns, 0 tokens, $0 in the JSON result).
/// This class never reads credentials and never talks to the network itself.
/// </summary>
public static class ClaudeCli
{
    /// <summary>The command from the spec. Never changes at runtime.</summary>
    public static readonly IReadOnlyList<string> UsageArguments =
        ["-p", "/usage", "--output-format", "text", "--no-session-persistence"];

    /// <summary>
    /// Added after the spec'd arguments: start with hooks, MCP servers, plugins and CLAUDE.md off,
    /// so a poll never fires the user's SessionEnd/Stop hooks or connects to MCP servers.
    /// Auth still works. Dropped automatically if a CLI version doesn't know the flag.
    /// </summary>
    public const string SafeModeFlag = "--safe-mode";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

    private static volatile bool _safeModeUnsupported;

    public static async Task<CliRunResult> RunUsageAsync(string cliPath, TimeSpan timeout, CancellationToken cancel = default)
    {
        var useSafeMode = !_safeModeUnsupported;
        var result = await RunOnceAsync(cliPath, useSafeMode, timeout, cancel).ConfigureAwait(false);
        if (useSafeMode && !result.TimedOut && IsUnknownOption(result, SafeModeFlag))
        {
            _safeModeUnsupported = true;
            result = await RunOnceAsync(cliPath, false, timeout, cancel).ConfigureAwait(false);
        }
        return result;
    }

    private static bool IsUnknownOption(CliRunResult r, string flag) =>
        r.ExitCode != 0 &&
        (r.StdErr + r.StdOut).Contains(flag, StringComparison.Ordinal) &&
        (r.StdErr + r.StdOut).Contains("unknown option", StringComparison.OrdinalIgnoreCase);

    private static async Task<CliRunResult> RunOnceAsync(string cliPath, bool safeMode, TimeSpan timeout, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = WorkingDirectory(),
        };

        var ext = Path.GetExtension(cliPath).ToLowerInvariant();
        if (ext is ".cmd" or ".bat")
        {
            // npm-style shims need cmd.exe. Arguments are fixed constants, so there is nothing to inject.
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.ArgumentList.Add("/d");
            psi.ArgumentList.Add("/s");
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(cliPath);
        }
        else
        {
            psi.FileName = cliPath;
        }
        foreach (var a in UsageArguments) psi.ArgumentList.Add(a);
        if (safeMode) psi.ArgumentList.Add(SafeModeFlag);

        // Never inherit a running session's identity or IPC handles; plain text output.
        ChildEnvironment.Apply(psi.Environment);

        using var process = new Process { StartInfo = psi };
        process.Start();
        try { process.StandardInput.Close(); } catch (IOException) { /* already gone */ }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancel);
        var stderrTask = process.StandardError.ReadToEndAsync(cancel);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeoutCts.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancel.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); } catch (Exception) { /* already exited */ }
            if (!timedOut) throw;
        }

        string stdout = "", stderr = "";
        try
        {
            // After exit (or kill) the pipes close; don't wait forever on a grandchild holding them open.
            var both = Task.WhenAll(stdoutTask, stderrTask);
            if (await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None)).ConfigureAwait(false) == both)
            {
                stdout = stdoutTask.Result;
                stderr = stderrTask.Result;
            }
            else
            {
                if (stdoutTask.IsCompletedSuccessfully) stdout = stdoutTask.Result;
                if (stderrTask.IsCompletedSuccessfully) stderr = stderrTask.Result;
            }
        }
        catch (Exception) when (timedOut) { /* partial output is fine on timeout */ }

        return new CliRunResult(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut);
    }

    /// <summary>An empty folder of our own, so the CLI never picks up a random project's settings.</summary>
    private static string WorkingDirectory()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeUsageMeter", "cli");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception)
        {
            return Path.GetTempPath();
        }
    }

    public static bool IsStartFailure(Exception ex) => ex is Win32Exception or FileNotFoundException or DirectoryNotFoundException;
}

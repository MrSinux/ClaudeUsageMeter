using System.Text.RegularExpressions;

namespace ClaudeUsageMeter.Core;

/// <summary>Locates the CLI, runs /usage and turns the result into an honest snapshot.</summary>
public sealed partial class UsageReader
{
    public string? ConfiguredCliPath { get; set; }
    public TimeSpan Timeout { get; set; } = ClaudeCli.DefaultTimeout;

    /// <summary>Screenshot/demo mode: parse these texts in turn instead of running the CLI.</summary>
    public IReadOnlyList<string>? DemoOutputs { get; set; }
    private int _demoIndex;

    /// <summary>The CLI path used by the last refresh, for Settings and About.</summary>
    public string? LastCliPath { get; private set; }

    public async Task<UsageSnapshot> ReadAsync(CancellationToken cancel = default)
    {
        var now = DateTimeOffset.Now;
        if (DemoOutputs is { Count: > 0 } demo)
            return Interpret(new CliRunResult(0, demo[_demoIndex++ % demo.Count], "", false), now);
        var cli = CliLocator.Locate(ConfiguredCliPath);
        LastCliPath = cli;
        if (cli is null)
        {
            var reason = string.IsNullOrWhiteSpace(ConfiguredCliPath)
                ? "Claude Code CLI not found. Install Claude Code, or set the CLI path in Settings."
                : "The CLI path in Settings doesn't exist, and no other copy was found.";
            return Fail(UsageStatus.CliNotFound, now, reason);
        }

        CliRunResult run;
        try
        {
            run = await ClaudeCli.RunUsageAsync(cli, Timeout, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ClaudeCli.IsStartFailure(ex))
        {
            return Fail(UsageStatus.CliNotFound, DateTimeOffset.Now, $"Couldn't start the CLI ({ex.Message.Trim()}).");
        }
        catch (Exception ex)
        {
            return Fail(UsageStatus.Error, DateTimeOffset.Now, $"Running the CLI failed ({ex.Message.Trim()}).");
        }

        var snap = Interpret(run, DateTimeOffset.Now);
        if (snap.Status == UsageStatus.Incomplete)
        {
            // The CLI sometimes prints the panel before its limits arrive. One quiet retry fixes most of these.
            try
            {
                await Task.Delay(RetryDelay, cancel).ConfigureAwait(false);
                var again = Interpret(await ClaudeCli.RunUsageAsync(cli, Timeout, cancel).ConfigureAwait(false), DateTimeOffset.Now);
                if (again.Status is UsageStatus.Ok or UsageStatus.LoggedOut) snap = again;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* keep the first result */ }
        }
        return snap;
    }

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    private static UsageSnapshot Fail(UsageStatus status, DateTimeOffset now, string reason, string? notice = null) =>
        new(status, [], now, reason, notice);

    /// <summary>Text that only appears when the CLI printed its usage panel (with or without the limits).</summary>
    [GeneratedRegex(@"subscription to power your Claude Code usage|What's contributing to your usage|\d+% used", RegexOptions.IgnoreCase)]
    private static partial Regex UsagePanelHint();

    [GeneratedRegex(@"ENOTFOUND|ECONNREFUSED|ECONNRESET|ETIMEDOUT|EAI_AGAIN|getaddrinfo|network|offline|fetch failed|connection error|unable to connect|could not connect|socket hang up", RegexOptions.IgnoreCase)]
    private static partial Regex OfflineHint();

    [GeneratedRegex(@"not logged in|not signed in|log ?in again|please (?:run )?/?login|auth login|invalid api key|unauthori[sz]ed|\b401\b|oauth token|token (?:has )?expired|session expired|sign-?in (?:has )?expired|authentication", RegexOptions.IgnoreCase)]
    private static partial Regex LoggedOutHint();

    /// <summary>Pure classification of a CLI run. Public for tests.</summary>
    public static UsageSnapshot Interpret(CliRunResult run, DateTimeOffset now, TimeZoneInfo? fallbackZone = null)
    {
        var blocks = UsageParser.Parse(run.StdOut, now, fallbackZone);
        if (blocks.Count > 0)
        {
            return new UsageSnapshot(UsageStatus.Ok, blocks, now, Notice: NoticeFrom(run.StdErr));
        }

        if (run.TimedOut)
            return Fail(UsageStatus.Timeout, now, "The CLI didn't answer in time. Will try again at the next refresh.");

        // Ignore advisory "Warning:" lines (e.g. "this sign-in can end at any time... run claude auth login")
        // so they don't masquerade as the cause of a different failure.
        var all = string.Join('\n',
            UsageParser.StripAnsi(run.StdOut + "\n" + run.StdErr)
                .Split('\n')
                .Where(l => !l.TrimStart().StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)));
        if (string.IsNullOrWhiteSpace(all))
            return Fail(UsageStatus.Error, now, $"The CLI exited with code {run.ExitCode} and printed nothing.");

        if (OfflineHint().IsMatch(all))
            return Fail(UsageStatus.Offline, now, "Can't reach Claude right now. Check your connection.");

        if (LoggedOutHint().IsMatch(all))
            return Fail(UsageStatus.LoggedOut, now, "Claude Code isn't signed in. Use Sign in to open the sign-in page.");

        // The usage panel came back, but without its limit lines (seen with CLI 2.1.322: intermittent).
        // That's a hiccup on the service side, not a format change, so keep the last numbers and retry.
        if (UsagePanelHint().IsMatch(all))
            return Fail(UsageStatus.Incomplete, now, "This read came back without the usage limits. Will try again at the next refresh.", NoticeFrom(run.StdErr));

        return Fail(UsageStatus.FormatChanged, now, $"Couldn't read the usage panel. The CLI said: \"{FirstLine(all, 140)}\"");
    }

    /// <summary>A short, user-facing note from stderr (for example a sign-in expiry warning), or null.</summary>
    private static string? NoticeFrom(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return null;
        var line = FirstLine(UsageParser.StripAnsi(stderr), 220);
        if (line.Length == 0) return null;
        if (line.StartsWith("Warning:", StringComparison.OrdinalIgnoreCase)) line = line["Warning:".Length..].Trim();
        return line;
    }

    private static string FirstLine(string text, int max)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().Replace('—', '-').Replace('–', '-');
            if (line.Length == 0) continue;
            return line.Length <= max ? line : line[..(max - 3)].TrimEnd() + "...";
        }
        return "";
    }
}

namespace ClaudeUsageMeter.Core;

/// <summary>One usage block as reported by Claude Code's /usage panel.</summary>
/// <param name="Label">Label exactly as the CLI prints it, e.g. "Current week (all models)".</param>
/// <param name="Percent">Percent used, 0 to 100 (clamped).</param>
/// <param name="ResetText">The raw reset text, e.g. "Nov 12, 1am (Asia/Kuala_Lumpur)". Empty if none.</param>
/// <param name="ResetsAt">Parsed reset instant, or null if the reset text could not be understood.</param>
public sealed record UsageBlock(string Label, double Percent, string ResetText, DateTimeOffset? ResetsAt)
{
    public UsageLevel Level => UsageLevels.FromPercent(Percent);

    /// <summary>A stable key for settings (which blocks show on the strip, alert bookkeeping).</summary>
    public string Key => Label.Trim().ToLowerInvariant();
}

public enum UsageLevel { Calm, Amber, Red }

public static class UsageLevels
{
    public const double AmberFrom = 60;
    public const double RedAbove = 85;

    /// <summary>Calm under 60%, amber 60 to 85%, red above 85%.</summary>
    public static UsageLevel FromPercent(double percent) =>
        percent > RedAbove ? UsageLevel.Red :
        percent >= AmberFrom ? UsageLevel.Amber :
        UsageLevel.Calm;
}

public enum UsageStatus
{
    /// <summary>At least one block was read.</summary>
    Ok,
    /// <summary>No Claude Code CLI could be found.</summary>
    CliNotFound,
    /// <summary>The CLI says nobody is signed in, or the sign-in expired.</summary>
    LoggedOut,
    /// <summary>The CLI could not reach the service.</summary>
    Offline,
    /// <summary>The CLI did not answer within the timeout.</summary>
    Timeout,
    /// <summary>The CLI answered but nothing in it looked like a usage block.</summary>
    FormatChanged,
    /// <summary>The CLI printed its usage panel but without the limit lines (a transient service hiccup).</summary>
    Incomplete,
    /// <summary>Anything else (process failed to start, crashed, and so on).</summary>
    Error,
}

/// <summary>The result of one refresh. When Status is not Ok, Blocks is empty and Reason says why.</summary>
public sealed record UsageSnapshot(
    UsageStatus Status,
    IReadOnlyList<UsageBlock> Blocks,
    DateTimeOffset FetchedAt,
    string? Reason = null,
    string? Notice = null)
{
    public bool IsOk => Status == UsageStatus.Ok && Blocks.Count > 0;
}

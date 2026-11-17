using ClaudeUsageMeter.Core;
using Xunit.Abstractions;

namespace ClaudeUsageMeter.Tests;

/// <summary>
/// Runs the real CLI. Opt in with USAGE_METER_LIVE=1 so CI and normal runs never touch the machine's CLI.
/// /usage is local (no model turn), so this costs no usage.
/// </summary>
public class LiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Real_cli_returns_blocks()
    {
        if (Environment.GetEnvironmentVariable("USAGE_METER_LIVE") != "1") return;

        var reader = new UsageReader();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snap = await reader.ReadAsync();
        output.WriteLine($"cli: {reader.LastCliPath} ({sw.ElapsedMilliseconds} ms, at {DateTimeOffset.Now:HH:mm:ss})");
        output.WriteLine($"status: {snap.Status} reason: {snap.Reason} notice: {snap.Notice}");
        foreach (var b in snap.Blocks)
            output.WriteLine($"{b.Label}: {b.Percent}% [{b.Level}] {Countdown.Describe(b, DateTimeOffset.Now)} (at {b.ResetsAt:u})");

        Assert.Equal(UsageStatus.Ok, snap.Status);
        Assert.NotEmpty(snap.Blocks);
        Assert.All(snap.Blocks, b => Assert.NotNull(b.ResetsAt));
    }
}

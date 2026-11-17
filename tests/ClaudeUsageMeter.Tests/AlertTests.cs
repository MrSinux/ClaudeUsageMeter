using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

public class AlertTests
{
    private static readonly DateTimeOffset Reset1 = new(2026, 11, 12, 1, 0, 0, TimeSpan.FromHours(8));
    private static readonly DateTimeOffset Reset2 = Reset1.AddDays(7);
    private static readonly int[] Thresholds = [80, 95];

    private static UsageBlock Week(double pct, DateTimeOffset? reset = null) =>
        new("Current week (all models)", pct, "", reset ?? Reset1);

    [Fact]
    public void Below_thresholds_no_alert()
    {
        var shown = new Dictionary<string, AlertMark>();
        Assert.Empty(AlertService.Evaluate([Week(79.9)], Thresholds, shown));
    }

    [Fact]
    public void Each_threshold_fires_once_per_reset()
    {
        var shown = new Dictionary<string, AlertMark>();
        var a = Assert.Single(AlertService.Evaluate([Week(81)], Thresholds, shown));
        Assert.Equal(80, a.Threshold);
        Assert.Empty(AlertService.Evaluate([Week(83)], Thresholds, shown));
        var b = Assert.Single(AlertService.Evaluate([Week(96)], Thresholds, shown));
        Assert.Equal(95, b.Threshold);
        Assert.Empty(AlertService.Evaluate([Week(99)], Thresholds, shown));
    }

    [Fact]
    public void Jumping_past_both_gives_one_alert_for_the_higher()
    {
        var shown = new Dictionary<string, AlertMark>();
        var a = Assert.Single(AlertService.Evaluate([Week(97)], Thresholds, shown));
        Assert.Equal(95, a.Threshold);
        Assert.Empty(AlertService.Evaluate([Week(97)], Thresholds, shown));
    }

    [Fact]
    public void New_reset_period_rearms()
    {
        var shown = new Dictionary<string, AlertMark>();
        Assert.Single(AlertService.Evaluate([Week(85, Reset1)], Thresholds, shown));
        // Small drift in the reset time is the same period.
        Assert.Empty(AlertService.Evaluate([Week(86, Reset1.AddMinutes(1))], Thresholds, shown));
        // Next week, usage climbs past 80 again.
        Assert.Single(AlertService.Evaluate([Week(82, Reset2)], Thresholds, shown));
    }

    [Fact]
    public void Dropping_below_rearms_even_without_reset_time()
    {
        var shown = new Dictionary<string, AlertMark>();
        Assert.Single(AlertService.Evaluate([new UsageBlock("X", 90, "", null)], Thresholds, shown));
        Assert.Empty(AlertService.Evaluate([new UsageBlock("X", 5, "", null)], Thresholds, shown));
        Assert.Single(AlertService.Evaluate([new UsageBlock("X", 81, "", null)], Thresholds, shown));
    }

    [Fact]
    public void Blocks_are_tracked_separately()
    {
        var shown = new Dictionary<string, AlertMark>();
        var alerts = AlertService.Evaluate([Week(81), new UsageBlock("Current week (Fable)", 96, "", Reset1)], Thresholds, shown);
        Assert.Equal(2, alerts.Count);
    }
}

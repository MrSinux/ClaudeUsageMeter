namespace ClaudeUsageMeter.Core;

public sealed class AlertMark
{
    public int Threshold { get; set; }
    /// <summary>The reset the alert belongs to; a new reset re-arms the alert.</summary>
    public DateTimeOffset? ResetsAt { get; set; }
}

public sealed record Alert(string BlockKey, string Label, int Threshold, double Percent, DateTimeOffset? ResetsAt);

/// <summary>
/// Decides when to notify: at most once per block, per threshold, per reset period. Crossing two
/// thresholds between refreshes produces one alert for the higher one. Pure logic; the caller shows it.
/// </summary>
public static class AlertService
{
    public static List<Alert> Evaluate(IReadOnlyList<UsageBlock> blocks, IReadOnlyList<int> thresholds, Dictionary<string, AlertMark> shown)
    {
        var alerts = new List<Alert>();
        if (thresholds.Count == 0) return alerts;
        var lowest = thresholds.Min();

        foreach (var b in blocks)
        {
            shown.TryGetValue(b.Key, out var mark);

            // A new reset period (the reset time moved on), or usage fell back below every threshold: re-arm.
            if (mark is not null && (IsNewPeriod(mark.ResetsAt, b.ResetsAt) || b.Percent < lowest))
            {
                shown.Remove(b.Key);
                mark = null;
            }

            var crossed = thresholds.Where(t => b.Percent >= t).DefaultIfEmpty(0).Max();
            if (crossed == 0) continue;
            if (mark is not null && mark.Threshold >= crossed) continue;

            shown[b.Key] = new AlertMark { Threshold = crossed, ResetsAt = b.ResetsAt };
            alerts.Add(new Alert(b.Key, b.Label, crossed, b.Percent, b.ResetsAt));
        }
        return alerts;
    }

    /// <summary>Reset times drift by a minute between reads; only a jump of an hour or more is a new period.</summary>
    private static bool IsNewPeriod(DateTimeOffset? before, DateTimeOffset? now) =>
        before is { } a && now is { } b && (b - a).Duration() > TimeSpan.FromHours(1);
}


namespace ClaudeUsageMeter.Core;

public static class Countdown
{
    /// <summary>"2h 14m", "1d 3h", "14m", "under 1m", "now".</summary>
    public static string Format(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "now";
        // Round up to the next minute so "resets in 1m" never sits at 0.
        var totalMinutes = (long)Math.Ceiling(remaining.TotalMinutes);
        if (totalMinutes < 1) return "under 1m";
        var days = totalMinutes / (60 * 24);
        var hours = totalMinutes / 60 % 24;
        var minutes = totalMinutes % 60;
        if (days > 0) return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
        if (hours > 0) return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
        return $"{minutes}m";
    }

    /// <summary>"resets in 2h 14m", or a fallback using the raw CLI text when the time couldn't be parsed.</summary>
    public static string Describe(UsageBlock block, DateTimeOffset now)
    {
        if (block.ResetsAt is { } at)
        {
            var left = at - now;
            return left <= TimeSpan.Zero ? "resetting now" : $"resets in {Format(left)}";
        }
        return string.IsNullOrWhiteSpace(block.ResetText) ? "" : $"resets {block.ResetText}";
    }
}

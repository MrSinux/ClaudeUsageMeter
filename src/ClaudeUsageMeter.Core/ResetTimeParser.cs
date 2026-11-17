using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeUsageMeter.Core;

/// <summary>
/// Turns the CLI's reset text into an instant. Known shapes:
/// "Nov 12, 1am (Asia/Kuala_Lumpur)", "3pm (Europe/London)", "1:30pm", "Nov 12 at 1:30 am",
/// "in 2h 14m". Anything else returns null and the UI shows the raw text instead of a countdown.
/// </summary>
public static partial class ResetTimeParser
{
    [GeneratedRegex(@"\(\s*(?<tz>[A-Za-z_]+(?:/[A-Za-z0-9_+\-]+)*|UTC|GMT)\s*\)\s*$")]
    private static partial Regex TrailingZone();

    [GeneratedRegex(@"^(?:in\s+)?(?:(?<d>\d+)\s*d(?:ays?)?\s*)?(?:(?<h>\d+)\s*h(?:(?:ou)?rs?)?\s*)?(?:(?<m>\d+)\s*m(?:in(?:ute)?s?)?)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Relative();

    [GeneratedRegex(
        @"^(?:(?<mon>[A-Za-z]{3,9})\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?(?:,?\s*(?<year>\d{4}))?\s*,?\s*(?:at\s+)?)?(?:(?<hour>\d{1,2})(?::(?<min>\d{2}))?\s*(?<ampm>[ap]\.?m\.?)?)?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex Absolute();

    /// <summary>Strips the leading "resets"/"resets at" and surrounding noise.</summary>
    public static string CleanResetText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var s = raw.Trim();
        foreach (var prefix in new[] { "resets at ", "resets on ", "resets ", "reset at ", "reset ", "renews " })
        {
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { s = s[prefix.Length..].Trim(); break; }
        }
        return s;
    }

    public static DateTimeOffset? TryParse(string? text, DateTimeOffset now, TimeZoneInfo? fallbackZone = null)
    {
        var s = CleanResetText(text);
        if (s.Length == 0) return null;

        var zone = fallbackZone ?? TimeZoneInfo.Local;
        var tzMatch = TrailingZone().Match(s);
        if (tzMatch.Success)
        {
            zone = FindZone(tzMatch.Groups["tz"].Value) ?? zone;
            s = s[..tzMatch.Index].Trim();
        }
        if (s.Length == 0) return null;

        // Relative: "in 2h 14m", "2h", "45m", "1d 3h"
        var rel = Relative().Match(s);
        if (rel.Success && (rel.Groups["d"].Success || rel.Groups["h"].Success || rel.Groups["m"].Success))
        {
            var span = TimeSpan.FromDays(Num(rel, "d")) + TimeSpan.FromHours(Num(rel, "h")) + TimeSpan.FromMinutes(Num(rel, "m"));
            return now + span;
        }

        var abs = Absolute().Match(s);
        if (!abs.Success || (!abs.Groups["mon"].Success && !abs.Groups["hour"].Success)) return null;

        var nowInZone = TimeZoneInfo.ConvertTime(now, zone);
        int hour = 0, minute = 0;
        if (abs.Groups["hour"].Success)
        {
            hour = int.Parse(abs.Groups["hour"].Value, CultureInfo.InvariantCulture);
            minute = abs.Groups["min"].Success ? int.Parse(abs.Groups["min"].Value, CultureInfo.InvariantCulture) : 0;
            if (abs.Groups["ampm"].Success)
            {
                if (hour is < 1 or > 12) return null;
                var pm = abs.Groups["ampm"].Value.StartsWith("p", StringComparison.OrdinalIgnoreCase);
                hour = hour % 12 + (pm ? 12 : 0);
            }
            else if (!abs.Groups["min"].Success && !abs.Groups["mon"].Success)
            {
                return null; // a bare number like "3" is too ambiguous
            }
            if (hour > 23 || minute > 59) return null;
        }

        DateTime local;
        if (abs.Groups["mon"].Success)
        {
            if (!TryMonth(abs.Groups["mon"].Value, out var month)) return null;
            var day = int.Parse(abs.Groups["day"].Value, CultureInfo.InvariantCulture);
            var explicitYear = abs.Groups["year"].Success;
            var year = explicitYear ? int.Parse(abs.Groups["year"].Value, CultureInfo.InvariantCulture) : nowInZone.Year;
            if (day < 1 || day > DateTime.DaysInMonth(year, month)) return null;
            local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            // No year printed: a date well in the past means next year (e.g. "Jan 2" seen on Dec 31).
            if (!explicitYear && local < nowInZone.DateTime.AddDays(-2))
            {
                var next = year + 1;
                if (day > DateTime.DaysInMonth(next, month)) return null;
                local = new DateTime(next, month, day, hour, minute, 0, DateTimeKind.Unspecified);
            }
        }
        else
        {
            // Time only: the next occurrence of that wall-clock time.
            local = nowInZone.Date + new TimeSpan(hour, minute, 0);
            if (local < nowInZone.DateTime.AddMinutes(-5)) local = local.AddDays(1);
        }

        return ToInstant(local, zone);
    }

    private static DateTimeOffset ToInstant(DateTime wallClock, TimeZoneInfo zone)
    {
        // Wall-clock times inside a DST gap don't exist; nudge forward an hour.
        if (zone.IsInvalidTime(wallClock)) wallClock = wallClock.AddHours(1);
        var offset = zone.GetUtcOffset(wallClock);
        return new DateTimeOffset(wallClock, offset);
    }

    private static int Num(Match m, string group) =>
        m.Groups[group].Success ? int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture) : 0;

    private static bool TryMonth(string text, out int month)
    {
        var t = text.Trim().TrimEnd('.').ToLowerInvariant();
        string[] names = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
        for (var i = 0; i < names.Length; i++)
        {
            if (t.StartsWith(names[i], StringComparison.Ordinal)) { month = i + 1; return true; }
        }
        month = 0;
        return false;
    }

    /// <summary>Finds a zone by IANA id (or Windows id). Returns null if unknown.</summary>
    public static TimeZoneInfo? FindZone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (id is "UTC" or "GMT") return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception) { /* fall through */ }
        try
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win)) return TimeZoneInfo.FindSystemTimeZoneById(win);
        }
        catch (Exception) { /* unknown zone */ }
        return null;
    }
}

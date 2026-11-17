using System.Globalization;
using System.Text.RegularExpressions;

namespace ClaudeUsageMeter.Core;

/// <summary>
/// Parses the plain-text output of <c>claude -p /usage --output-format text</c>.
/// Blocks are matched generically ("&lt;label&gt;: &lt;n&gt;% used · resets &lt;when&gt;"),
/// so a renamed or new block still shows up without code changes.
/// </summary>
public static partial class UsageParser
{
    // "Current week (all models): 74% used · resets Nov 12, 1am (Asia/Kuala_Lumpur)"
    // The separator before "resets" has been a middle dot; accept a few look-alikes too.
    [GeneratedRegex(
        @"^\s*(?<label>[^:\r\n%]{1,80}?)\s*:\s*(?<pct>\d{1,3}(?:[.,]\d+)?)\s*%\s*used\b\s*(?:[·•|,;\-]\s*(?<reset>.*?))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BlockLine();

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07")]
    private static partial Regex AnsiEscape();

    public static string StripAnsi(string text) => AnsiEscape().Replace(text, "");

    /// <summary>Returns every usage block found, in the order the CLI printed them. Duplicate labels keep the first.</summary>
    public static IReadOnlyList<UsageBlock> Parse(string? output, DateTimeOffset now, TimeZoneInfo? fallbackZone = null)
    {
        var blocks = new List<UsageBlock>();
        if (string.IsNullOrWhiteSpace(output)) return blocks;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in StripAnsi(output).Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var m = BlockLine().Match(line);
            if (!m.Success) continue;

            var label = m.Groups["label"].Value.Trim();
            if (label.Length == 0 || !seen.Add(label)) continue;

            var pctText = m.Groups["pct"].Value.Replace(',', '.');
            if (!double.TryParse(pctText, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) continue;
            pct = Math.Clamp(pct, 0, 100);

            var resetText = ResetTimeParser.CleanResetText(m.Groups["reset"].Value);
            var resetsAt = ResetTimeParser.TryParse(resetText, now, fallbackZone);
            blocks.Add(new UsageBlock(label, pct, resetText, resetsAt));
        }
        return blocks;
    }
}

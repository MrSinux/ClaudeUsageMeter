using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

public class ResetAndCountdownTests
{
    private static readonly TimeZoneInfo KualaLumpur = ResetTimeParser.FindZone("Asia/Kuala_Lumpur")!;
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.FromHours(8));

    [Theory]
    [InlineData("Nov 12, 1am (Asia/Kuala_Lumpur)", "2026-11-12T01:00:00+08:00")]
    [InlineData("resets Nov 12, 1am (Asia/Kuala_Lumpur)", "2026-11-12T01:00:00+08:00")]
    [InlineData("Nov 12 at 1:30 am (Asia/Kuala_Lumpur)", "2026-11-12T01:30:00+08:00")]
    [InlineData("November 20, 2026, 9pm (UTC)", "2026-11-20T21:00:00+00:00")]
    [InlineData("8:30pm (Asia/Kuala_Lumpur)", "2026-11-11T20:30:00+08:00")]
    [InlineData("12am (Asia/Kuala_Lumpur)", "2026-11-12T00:00:00+08:00")]
    [InlineData("12pm (Asia/Kuala_Lumpur)", "2026-11-12T12:00:00+08:00")]
    [InlineData("21:15 (Asia/Kuala_Lumpur)", "2026-11-11T21:15:00+08:00")]
    [InlineData("in 2h 14m", "2026-11-11T20:14:00+08:00")]
    [InlineData("in 45m", "2026-11-11T18:45:00+08:00")]
    [InlineData("1d 3h", "2026-11-12T21:00:00+08:00")]
    public void Parses_known_shapes(string text, string expected)
    {
        var at = ResetTimeParser.TryParse(text, Now, KualaLumpur);
        Assert.NotNull(at);
        Assert.Equal(DateTimeOffset.Parse(expected), at!.Value);
    }

    [Fact]
    public void Other_zones_convert_to_the_right_instant()
    {
        // 1am in London on Nov 12 (GMT, UTC+0) is 9am in Kuala Lumpur.
        var at = ResetTimeParser.TryParse("Nov 12, 1am (Europe/London)", Now, KualaLumpur);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 1, 0, 0, TimeSpan.Zero), at);
    }

    [Fact]
    public void Missing_zone_uses_the_fallback_zone()
    {
        var at = ResetTimeParser.TryParse("Nov 12, 1am", Now, KualaLumpur);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 1, 0, 0, TimeSpan.FromHours(8)), at);
    }

    [Fact]
    public void Date_without_year_rolls_over_new_year()
    {
        var dec31 = new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.FromHours(8));
        var at = ResetTimeParser.TryParse("Jan 2, 1am (Asia/Kuala_Lumpur)", dec31, KualaLumpur);
        Assert.Equal(new DateTimeOffset(2027, 1, 2, 1, 0, 0, TimeSpan.FromHours(8)), at);
    }

    [Fact]
    public void Unknown_zone_falls_back_instead_of_failing()
    {
        var at = ResetTimeParser.TryParse("Nov 12, 1am (Mars/Olympus_Mons)", Now, KualaLumpur);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 1, 0, 0, TimeSpan.FromHours(8)), at);
    }

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("3")]
    [InlineData("13pm")]
    [InlineData("Feb 30, 1am")]
    [InlineData("Smarch 3, 1am")]
    public void Garbage_gives_null(string text) =>
        Assert.Null(ResetTimeParser.TryParse(text, Now, KualaLumpur));

    [Theory]
    [InlineData(0, "now")]
    [InlineData(-5, "now")]
    [InlineData(0.5, "1m")]
    [InlineData(14, "14m")]
    [InlineData(60, "1h")]
    [InlineData(134, "2h 14m")]
    [InlineData(1440, "1d")]
    [InlineData(1440 + 180, "1d 3h")]
    [InlineData(6 * 1440 + 23 * 60 + 59, "6d 23h")]
    public void Countdown_format(double minutes, string expected) =>
        Assert.Equal(expected, Countdown.Format(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public void Describe_uses_live_countdown()
    {
        var block = new UsageBlock("Current session", 10, "8:30pm", Now.AddMinutes(134));
        Assert.Equal("resets in 2h 14m", Countdown.Describe(block, Now));
        Assert.Equal("resetting now", Countdown.Describe(block, Now.AddHours(3)));
    }

    [Theory]
    [InlineData(0, UsageLevel.Calm)]
    [InlineData(59.9, UsageLevel.Calm)]
    [InlineData(60, UsageLevel.Amber)]
    [InlineData(85, UsageLevel.Amber)]
    [InlineData(85.1, UsageLevel.Red)]
    [InlineData(100, UsageLevel.Red)]
    public void Levels(double pct, UsageLevel expected) =>
        Assert.Equal(expected, UsageLevels.FromPercent(pct));
}

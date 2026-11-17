using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

public class ParserTests
{
    private static readonly TimeZoneInfo KualaLumpur = ResetTimeParser.FindZone("Asia/Kuala_Lumpur")!;

    // 6pm in Kuala Lumpur on Nov 11, 2026 (UTC+8).
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.FromHours(8));

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Sample_output_gives_two_week_blocks()
    {
        var blocks = UsageParser.Parse(Fixture("usage-sample-two-blocks.txt"), Now, KualaLumpur);

        Assert.Equal(2, blocks.Count);

        Assert.Equal("Current week (all models)", blocks[0].Label);
        Assert.Equal(67, blocks[0].Percent);
        Assert.Equal("Nov 12, 1am (Europe/Berlin)", blocks[0].ResetText);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), blocks[0].ResetsAt);
        Assert.Equal(UsageLevel.Amber, blocks[0].Level);

        Assert.Equal("Current week (Fable)", blocks[1].Label);
        Assert.Equal(4, blocks[1].Percent);
        Assert.Equal(UsageLevel.Calm, blocks[1].Level);
    }

    [Fact]
    public void Contributing_section_percentages_are_not_blocks()
    {
        // "  83%  Start fresh when you switch tasks" must never be read as usage.
        var blocks = UsageParser.Parse(Fixture("usage-sample-two-blocks.txt"), Now, KualaLumpur);
        Assert.DoesNotContain(blocks, b => b.Label.Contains("Start fresh") || b.Percent == 83);
    }

    [Fact]
    public void Synthetic_three_block_output_with_session_and_renamed_opus_block()
    {
        var blocks = UsageParser.Parse(Fixture("usage-synthetic-three-blocks.txt"), Now, KualaLumpur);

        Assert.Collection(blocks,
            b =>
            {
                Assert.Equal("Current session", b.Label);
                Assert.Equal(12, b.Percent);
                // "8:30pm" on Nov 11 in KL, which is later today.
                Assert.Equal(new DateTimeOffset(2026, 11, 11, 20, 30, 0, TimeSpan.FromHours(8)), b.ResetsAt);
            },
            b =>
            {
                Assert.Equal("Current week (all models)", b.Label);
                Assert.Equal(91, b.Percent);
                Assert.Equal(UsageLevel.Red, b.Level);
            },
            b =>
            {
                Assert.Equal("Current week (Opus)", b.Label);
                Assert.Equal(100, b.Percent);
            });
    }

    [Fact]
    public void Crlf_and_ansi_codes_are_tolerated()
    {
        var text = "\u001b[1mCurrent session\u001b[0m: \u001b[33m45% used\u001b[0m · resets 3pm (Asia/Kuala_Lumpur)\r\n";
        var b = Assert.Single(UsageParser.Parse(text, Now, KualaLumpur));
        Assert.Equal("Current session", b.Label);
        Assert.Equal(45, b.Percent);
        // 3pm has passed at 6pm, so it's tomorrow's 3pm.
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.FromHours(8)), b.ResetsAt);
    }

    [Fact]
    public void Block_without_reset_text_still_shows()
    {
        var b = Assert.Single(UsageParser.Parse("Extra usage: 5% used\n", Now, KualaLumpur));
        Assert.Equal("Extra usage", b.Label);
        Assert.Equal("", b.ResetText);
        Assert.Null(b.ResetsAt);
    }

    [Fact]
    public void Unknown_reset_format_keeps_raw_text_and_no_countdown()
    {
        var b = Assert.Single(UsageParser.Parse("Current week: 40% used · resets when the moon is full\n", Now, KualaLumpur));
        Assert.Equal("when the moon is full", b.ResetText);
        Assert.Null(b.ResetsAt);
        Assert.Equal("resets when the moon is full", Countdown.Describe(b, Now));
    }

    [Fact]
    public void Duplicate_labels_keep_the_first()
    {
        var blocks = UsageParser.Parse("A: 1% used\nA: 2% used\nB: 3% used\n", Now, KualaLumpur);
        Assert.Equal(["A", "B"], blocks.Select(b => b.Label));
        Assert.Equal(1, blocks[0].Percent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n")]
    [InlineData("You are currently using your subscription to power your Claude Code usage\n")]
    [InlineData("Usage: 50% of nothing")]
    public void Non_usage_text_gives_no_blocks(string text) =>
        Assert.Empty(UsageParser.Parse(text, Now, KualaLumpur));

    [Fact]
    public void Decimal_percent_and_clamping()
    {
        var blocks = UsageParser.Parse("A: 12.5% used\nB: 140% used\n", Now, KualaLumpur);
        Assert.Equal(12.5, blocks[0].Percent);
        Assert.Equal(100, blocks[1].Percent);
    }
}

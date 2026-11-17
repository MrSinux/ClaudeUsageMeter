using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

public class ReaderTests
{
    private static readonly TimeZoneInfo KualaLumpur = ResetTimeParser.FindZone("Asia/Kuala_Lumpur")!;
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 0, 0, TimeSpan.FromHours(8));

    private static UsageSnapshot Run(string stdout, string stderr = "", int exit = 0, bool timedOut = false) =>
        UsageReader.Interpret(new CliRunResult(exit, stdout, stderr, timedOut), Now, KualaLumpur);

    [Fact]
    public void Sample_output_is_ok_and_carries_the_signin_warning_as_a_notice()
    {
        var stdout = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "usage-sample-two-blocks.txt"));
        var stderr = "Warning: this sign-in can end at any time from now until 2026-11-12. Renewing it does not move that date. Run claude auth login now, or use claude setup-token for unattended jobs.\n";
        var s = Run(stdout, stderr);
        Assert.Equal(UsageStatus.Ok, s.Status);
        Assert.True(s.IsOk);
        Assert.Equal(2, s.Blocks.Count);
        Assert.StartsWith("this sign-in can end", s.Notice);
    }

    [Fact]
    public void Blocks_win_even_with_nonzero_exit() =>
        Assert.Equal(UsageStatus.Ok, Run("Current session: 3% used · resets 9pm\n", exit: 1).Status);

    [Fact]
    public void Timeout() => Assert.Equal(UsageStatus.Timeout, Run("", timedOut: true).Status);

    [Fact]
    public void Empty_output_is_an_error_with_the_exit_code()
    {
        var s = Run("", exit: 3);
        Assert.Equal(UsageStatus.Error, s.Status);
        Assert.Contains("code 3", s.Reason);
    }

    [Theory]
    [InlineData("Invalid API key · Please run /login")]
    [InlineData("Not logged in · Please run /login")]
    [InlineData("OAuth token has expired. Please obtain a new token or refresh your existing token.")]
    [InlineData("API Error: 401 {\"type\":\"error\"}")]
    public void Logged_out(string text)
    {
        var s = Run(text);
        Assert.Equal(UsageStatus.LoggedOut, s.Status);
        Assert.Empty(s.Blocks);
        Assert.Contains("Sign in", s.Reason);
    }

    [Fact]
    public void Panel_without_limit_lines_is_incomplete_not_a_format_change()
    {
        // Seen with CLI 2.1.322: the header and "What's contributing" came back, the limit lines didn't.
        var stdout = "You are currently using your subscription to power your Claude Code usage\n\nWhat's contributing to your usage?\nApproximate · sessions on this machine only\n";
        var s = Run(stdout, "Warning: this sign-in can end at any time from now until 2026-11-12. Run claude auth login now.\n");
        Assert.Equal(UsageStatus.Incomplete, s.Status);
        Assert.Empty(s.Blocks);
        Assert.StartsWith("this sign-in can end", s.Notice);
    }

    [Theory]
    [InlineData("API Error: Connection error.")]
    [InlineData("getaddrinfo ENOTFOUND api.example.com")]
    [InlineData("TypeError: fetch failed")]
    public void Offline(string text) => Assert.Equal(UsageStatus.Offline, Run("", text, exit: 1).Status);

    [Fact]
    public void Signin_warning_alone_does_not_look_like_logged_out()
    {
        var s = Run("Something brand new happened here\n", "Warning: this sign-in can end soon. Run claude auth login now.\n");
        Assert.Equal(UsageStatus.FormatChanged, s.Status);
        Assert.Contains("Something brand new happened here", s.Reason);
    }

    [Fact]
    public void Unknown_output_is_format_changed_and_quotes_the_cli()
    {
        var s = Run("/usage is only available for subscription plans.\n");
        Assert.Equal(UsageStatus.FormatChanged, s.Status);
        Assert.Contains("/usage is only available for subscription plans.", s.Reason);
        Assert.DoesNotContain('—', s.Reason!);
    }
}

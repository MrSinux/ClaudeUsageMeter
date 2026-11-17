using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

/// <summary>An enabled "Start with Windows" follows the exe when it is moved or updated.</summary>
public class AutostartTests
{
    private const string Current = @"C:\Apps\ClaudeUsageMeter\ClaudeUsageMeter.exe";

    [Fact]
    public void Command_is_the_quoted_path()
    {
        Assert.Equal("\"" + Current + "\"", AutostartEntry.CommandFor(Current));
    }

    [Fact]
    public void Off_stays_off()
    {
        Assert.Null(AutostartEntry.Repair(null, Current));
        Assert.Null(AutostartEntry.Repair("", Current));
    }

    [Theory]
    [InlineData("\"C:\\Apps\\ClaudeUsageMeter\\ClaudeUsageMeter.exe\"")]
    [InlineData("\"c:\\apps\\claudeusagemeter\\CLAUDEUSAGEMETER.EXE\"")]
    [InlineData("C:\\Apps\\ClaudeUsageMeter\\ClaudeUsageMeter.exe")]
    [InlineData("  \"C:\\Apps\\ClaudeUsageMeter\\ClaudeUsageMeter.exe\"  ")]
    public void Matching_entry_is_left_alone(string registered)
    {
        Assert.True(AutostartEntry.Matches(registered, Current));
        Assert.Null(AutostartEntry.Repair(registered, Current));
    }

    [Theory]
    [InlineData("\"C:\\Users\\you\\Downloads\\ClaudeUsageMeter.exe\"")]
    [InlineData("\"C:\\dev\\src\\ClaudeUsageMeter\\bin\\Debug\\net8.0-windows\\ClaudeUsageMeter.exe\"")]
    public void Moved_or_old_entry_is_pointed_at_this_exe(string registered)
    {
        Assert.False(AutostartEntry.Matches(registered, Current));
        Assert.Equal("\"" + Current + "\"", AutostartEntry.Repair(registered, Current));
    }

    [Fact]
    public void Not_repaired_when_hosted_by_dotnet()
    {
        Assert.Null(AutostartEntry.Repair("\"C:\\Old\\ClaudeUsageMeter.exe\"", @"C:\Program Files\dotnet\dotnet.exe"));
        Assert.Null(AutostartEntry.Repair("\"C:\\Old\\ClaudeUsageMeter.exe\"", null));
    }
}

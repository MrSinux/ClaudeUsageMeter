using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Tests;

public class LocatorTests
{
    [Fact]
    public void Newest_extension_version_comes_first()
    {
        var dirs = new[]
        {
            @"C:\u\.vscode\extensions\anthropic.claude-code-2.1.319-win32-x64",
            @"C:\u\.vscode\extensions\anthropic.claude-code-2.1.322-win32-x64",
            @"C:\u\.vscode\extensions\anthropic.claude-code-2.10.1-win32-x64",
        };
        var ordered = CliLocator.OrderByVersionDescending(dirs).ToList();
        Assert.EndsWith("2.10.1-win32-x64", ordered[0]);
        Assert.EndsWith("2.1.322-win32-x64", ordered[1]);
    }

    [Fact]
    public void Configured_path_is_tried_first_and_missing_paths_are_skipped()
    {
        var first = CliLocator.Candidates(@"""C:\nowhere\claude.exe""").First();
        Assert.Equal(@"C:\nowhere\claude.exe", first);
        Assert.False(CliLocator.IsUsable(@"C:\nowhere\claude.exe"));
        Assert.False(CliLocator.IsUsable(null));
    }

    [Fact]
    public void Real_extension_folder_layout_is_found()
    {
        var root = Path.Combine(Path.GetTempPath(), "sum-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var bin = Path.Combine(root, "anthropic.claude-code-9.9.9-win32-x64", "resources", "native-binary");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "claude.exe"), "");
            var found = CliLocator.BundledInExtensions(root).Single();
            Assert.True(CliLocator.IsUsable(found));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

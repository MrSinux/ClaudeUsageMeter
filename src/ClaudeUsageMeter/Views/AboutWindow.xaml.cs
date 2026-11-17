using System.Diagnostics;
using System.Reflection;
using System.Windows;
using ClaudeUsageMeter.Native;

namespace ClaudeUsageMeter.Views;

public partial class AboutWindow : Window
{
    /// <summary>Opened in the browser only when the user asks. The app never downloads or runs updates.</summary>
    public const string ReleasesUrl = "https://github.com/MrSinux/ClaudeUsageMeter/releases";

    public AboutWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "" : $"Version {v.Major}.{v.Minor}.{v.Build}";
        SourceInitialized += (_, _) => WindowEffects.ApplyFrame(this, App.Current.Theme.AppsDark);
    }

    private void Releases_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { /* no browser: nothing else to do */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

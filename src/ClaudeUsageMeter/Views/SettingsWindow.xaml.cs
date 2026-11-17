using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ClaudeUsageMeter.Core;
using ClaudeUsageMeter.Native;
using ClaudeUsageMeter.Services;

namespace ClaudeUsageMeter.Views;

/// <summary>Settings apply as you change them, like Windows 11 Settings; "Done" just closes.</summary>
public partial class SettingsWindow : Window
{
    private readonly App _app;
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        _app = App.Current;
        SourceInitialized += (_, _) => WindowEffects.ApplyFrame(this, _app.Theme.AppsDark);
        Load();
        _loading = false;
        _app.Usage.StateChanged += OnUsageChanged;
        Closed += (_, _) => _app.Usage.StateChanged -= OnUsageChanged;
    }

    private string _blockSignature = "";

    /// <summary>Blocks may arrive after the window opens (first read, or a renamed block).</summary>
    private void OnUsageChanged()
    {
        var sig = string.Join("|", _app.Usage.State.Blocks.Select(b => b.Key));
        if (sig == _blockSignature || _app.Usage.State.Blocks.Count == 0) return;
        _blockSignature = sig;
        _loading = true;
        BuildStripBlocks();
        _loading = false;
    }

    private AppSettings S => _app.Settings;

    private void Load()
    {
        ModeTray.IsChecked = S.DisplayMode == DisplayMode.TrayPopup;
        ModeStrip.IsChecked = S.DisplayMode == DisplayMode.TaskbarStrip;
        ModeBoth.IsChecked = S.DisplayMode == DisplayMode.Both;
        RefreshValue.Text = $"{S.RefreshMinutes} min";
        AlertsOn.IsChecked = S.AlertsEnabled;
        ShowThresholds();
        CliPath.Text = S.CliPath ?? "";
        UpdateCliStatus();
        StartWithWindows.IsChecked = StartupService.IsEnabled();
        BuildStripBlocks();
    }

    private void BuildStripBlocks()
    {
        StripBlocksPanel.Children.Clear();
        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in _app.Usage.State.Blocks) labels[b.Key] = b.Label;
        foreach (var hidden in S.StripHiddenBlocks) labels.TryAdd(hidden, hidden);
        foreach (var (key, label) in labels)
        {
            var cb = new CheckBox
            {
                Content = label,
                Style = (Style)FindResource("CheckLine"),
                IsChecked = !S.StripHiddenBlocks.Contains(key, StringComparer.OrdinalIgnoreCase),
                Tag = key,
            };
            cb.Checked += StripBlock_Changed;
            cb.Unchecked += StripBlock_Changed;
            StripBlocksPanel.Children.Add(cb);
        }
        StripBlocksEmpty.Visibility = labels.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Save() => _app.ApplySettings();

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.DisplayMode = ModeStrip.IsChecked == true ? DisplayMode.TaskbarStrip : ModeBoth.IsChecked == true ? DisplayMode.Both : DisplayMode.TrayPopup;
        Save();
    }

    private void ResetStrip_Click(object sender, RoutedEventArgs e) => _app.ResetStripPosition();

    private void StripBlock_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string key } cb) return;
        S.StripHiddenBlocks.RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
        if (cb.IsChecked != true) S.StripHiddenBlocks.Add(key);
        Save();
    }

    private void RefreshDown_Click(object sender, RoutedEventArgs e) => StepRefresh(-1);
    private void RefreshUp_Click(object sender, RoutedEventArgs e) => StepRefresh(+1);

    private void StepRefresh(int delta)
    {
        int[] steps = [1, 2, 3, 5, 10, 15];
        var i = Array.FindIndex(steps, s => s >= S.RefreshMinutes);
        if (i < 0) i = steps.Length - 1;
        i = Math.Clamp(i + delta, 0, steps.Length - 1);
        S.RefreshMinutes = steps[i];
        RefreshValue.Text = $"{S.RefreshMinutes} min";
        Save();
    }

    private void Alerts_Changed(object sender, RoutedEventArgs e)
    {
        ThresholdsPanel.IsEnabled = AlertsOn.IsChecked == true;
        ThresholdsPanel.Opacity = AlertsOn.IsChecked == true ? 1 : 0.45;
        if (_loading) return;
        S.AlertsEnabled = AlertsOn.IsChecked == true;
        Save();
    }

    private void ShowThresholds()
    {
        while (S.AlertThresholds.Count < 2) S.AlertThresholds.Add(S.AlertThresholds.Count == 0 ? 80 : 95);
        Threshold1.Text = $"{S.AlertThresholds[0]}%";
        Threshold2.Text = $"{S.AlertThresholds[1]}%";
        ThresholdsPanel.IsEnabled = S.AlertsEnabled;
        ThresholdsPanel.Opacity = S.AlertsEnabled ? 1 : 0.45;
    }

    private void Threshold_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        var parts = tag.Split(':');
        var index = int.Parse(parts[0]);
        var delta = int.Parse(parts[1]);
        var t = S.AlertThresholds;
        while (t.Count < 2) t.Add(t.Count == 0 ? 80 : 95);
        // Keep first < second, both within 50..100.
        var lo = index == 0 ? 50 : t[0] + 5;
        var hi = index == 0 ? t[1] - 5 : 100;
        t[index] = Math.Clamp(t[index] + delta, lo, hi);
        ShowThresholds();
        Save();
    }

    private void CliPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CliPath_Commit(sender, e);
    }

    private void CliPath_Commit(object sender, RoutedEventArgs e)
    {
        var value = CliPath.Text.Trim().Trim('"');
        var newPath = value.Length == 0 ? null : value;
        if (newPath == S.CliPath) { UpdateCliStatus(); return; }
        S.CliPath = newPath;
        Save();
        UpdateCliStatus();
        _ = _app.Usage.RefreshAsync();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Find the Claude Code CLI (claude.exe)",
            Filter = "Claude Code CLI|claude.exe;claude.cmd|Programs|*.exe;*.cmd",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) == true)
        {
            CliPath.Text = dlg.FileName;
            CliPath_Commit(sender, e);
        }
    }

    private void UpdateCliStatus()
    {
        var configured = S.CliPath;
        if (!string.IsNullOrEmpty(configured) && !CliLocator.IsUsable(configured))
        {
            var fallback = CliLocator.Locate(null);
            CliStatus.Text = fallback is null
                ? "That file doesn't exist, and no other copy was found."
                : $"That file doesn't exist. Using the detected copy instead: {fallback}";
            return;
        }
        var found = App.IsDemo ? @"C:\Users\you\.local\bin\claude.exe" : CliLocator.Locate(configured);
        CliStatus.Text = found is null
            ? "Not found. Install Claude Code, or browse to claude.exe. Leave empty to detect automatically."
            : string.IsNullOrEmpty(configured) ? $"Detected automatically: {found}" : $"Using: {found}";
    }

    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (!StartupService.Set(StartWithWindows.IsChecked == true))
        {
            _loading = true;
            StartWithWindows.IsChecked = StartupService.IsEnabled();
            _loading = false;
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
        base.OnKeyDown(e);
    }
}

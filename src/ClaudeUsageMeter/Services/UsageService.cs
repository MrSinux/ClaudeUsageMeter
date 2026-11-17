using System.Text.RegularExpressions;
using System.Windows.Threading;
using ClaudeUsageMeter.Core;

namespace ClaudeUsageMeter.Services;

/// <summary>What the UI should show right now, derived from the latest snapshot.</summary>
public sealed record DisplayState(
    IReadOnlyList<UsageBlock> Blocks,
    UsageStatus Status,
    string? Reason,
    bool IsStale,
    DateTimeOffset? DataTime,
    DateTimeOffset? LastAttempt,
    string? SignInWarning)
{
    public bool HasData => Blocks.Count > 0;

    /// <summary>The block the tray icon and strip lead with: the session if there is one, else the first.</summary>
    public UsageBlock? Primary =>
        Blocks.FirstOrDefault(b => b.Label.Contains("session", StringComparison.OrdinalIgnoreCase)) ?? Blocks.FirstOrDefault();

    public static readonly DisplayState Initial = new([], UsageStatus.Ok, null, false, null, null, null);
}

/// <summary>Polls the CLI on a timer, one run at a time, and publishes a DisplayState.</summary>
public sealed partial class UsageService
{
    /// <summary>Transient failures keep the last good numbers (marked stale) for this long, then show "--".</summary>
    public static readonly TimeSpan StaleGrace = TimeSpan.FromMinutes(10);
    /// <summary>Opening the popup refreshes at most this often.</summary>
    public static readonly TimeSpan OpenRefreshGap = TimeSpan.FromSeconds(30);

    private readonly UsageReader _reader = new();
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private UsageSnapshot? _lastGood;
    private UsageSnapshot? _latest;
    private Task? _running;
    private TimeSpan? _demoInterval;

    public DisplayState State { get; private set; } = DisplayState.Initial;
    public bool IsRefreshing => _running is not null;
    public string? CliPath => _reader.LastCliPath;

    public event Action? StateChanged;
    public event Action? RefreshingChanged;

    public UsageService()
    {
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public void Configure(string? cliPath, int refreshMinutes)
    {
        _reader.ConfiguredCliPath = cliPath;
        var interval = _demoInterval ?? TimeSpan.FromMinutes(Math.Clamp(refreshMinutes, AppSettings.MinRefreshMinutes, AppSettings.MaxRefreshMinutes));
        if (_timer.Interval != interval)
        {
            _timer.Interval = interval;
            if (_timer.IsEnabled) { _timer.Stop(); _timer.Start(); }
        }
    }

    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Stop() => _timer.Stop();

    /// <summary>For screenshots: made-up numbers, the CLI is never run.</summary>
    public void UseDemoData(bool cycle = false)
    {
        static string Read(int s, int w, int f) =>
            $"Current session: {s}% used · resets in 2h 14m\nCurrent week (all models): {w}% used · resets in 3d 5h\nCurrent week (Fable): {f}% used · resets in 3d 5h\n";
        _reader.DemoOutputs = cycle
            ? [Read(34, 67, 12), Read(46, 74, 14),
               // The panel without its limit lines: shows the dimmed fallback.
               "You are currently using your subscription to power your Claude Code usage\n\nWhat's contributing to your usage?\n",
               Read(58, 88, 17), Read(12, 88, 17)]
            : [Read(34, 67, 12)];
        if (cycle) { _demoInterval = TimeSpan.FromSeconds(4); _timer.Interval = _demoInterval.Value; }
    }

    /// <summary>Refresh unless the last attempt was very recent (used when the popup opens).</summary>
    public Task RefreshIfOlderThan(TimeSpan age)
    {
        if (_latest is { } l && DateTimeOffset.Now - l.FetchedAt < age) return _running ?? Task.CompletedTask;
        return RefreshAsync();
    }

    /// <summary>Single-flight: concurrent callers share the run in progress.</summary>
    public Task RefreshAsync()
    {
        if (_running is not null) return _running;
        _running = RunAsync();
        RefreshingChanged?.Invoke();
        return _running;
    }

    private async Task RunAsync()
    {
        // Always finish asynchronously, so the finally below can't clear _running before
        // RefreshAsync has stored it (a read that completes synchronously would leave it stuck).
        await Task.Yield();
        try
        {
            UsageSnapshot snap;
            try
            {
                snap = await _reader.ReadAsync();
            }
            catch (Exception ex)
            {
                snap = new UsageSnapshot(UsageStatus.Error, [], DateTimeOffset.Now, $"Unexpected error ({ex.Message})");
            }
            _latest = snap;
            if (snap.IsOk) _lastGood = snap;
            if (snap.Status is UsageStatus.LoggedOut or UsageStatus.CliNotFound) _lastGood = null; // never show numbers we can't vouch for
            Recompute();
            // Keep the timer phase anchored to the last real refresh.
            if (_timer.IsEnabled) { _timer.Stop(); _timer.Start(); }
        }
        finally
        {
            _running = null;
            RefreshingChanged?.Invoke();
        }
    }

    /// <summary>Re-evaluates staleness (called by the UI tick too).</summary>
    public void Recompute()
    {
        var now = DateTimeOffset.Now;
        var latest = _latest;
        DisplayState state;
        if (latest is null)
        {
            state = DisplayState.Initial;
        }
        else if (latest.IsOk)
        {
            state = new DisplayState(latest.Blocks, UsageStatus.Ok, null, false, latest.FetchedAt, latest.FetchedAt, SignInWarningFrom(latest.Notice));
        }
        else if (latest.Status is UsageStatus.Timeout or UsageStatus.Offline or UsageStatus.Error or UsageStatus.Incomplete
                 && _lastGood is { } good && now - good.FetchedAt < StaleGrace)
        {
            state = new DisplayState(good.Blocks, latest.Status, latest.Reason, true, good.FetchedAt, latest.FetchedAt, SignInWarningFrom(latest.Notice ?? good.Notice));
        }
        else
        {
            state = new DisplayState([], latest.Status, latest.Reason, false, null, latest.FetchedAt, SignInWarningFrom(latest.Notice));
        }
        State = state;
        StateChanged?.Invoke();
    }

    [GeneratedRegex(@"until (?<d>\d{4}-\d{2}-\d{2})")]
    private static partial Regex UntilDate();

    /// <summary>Turns the CLI's sign-in expiry warning into a short banner line, or null.</summary>
    public static string? SignInWarningFrom(string? notice)
    {
        if (string.IsNullOrWhiteSpace(notice)) return null;
        if (!notice.Contains("sign-in", StringComparison.OrdinalIgnoreCase) &&
            !notice.Contains("auth login", StringComparison.OrdinalIgnoreCase) &&
            !notice.Contains("log in", StringComparison.OrdinalIgnoreCase))
            return null;
        var m = UntilDate().Match(notice);
        if (m.Success && DateTime.TryParse(m.Groups["d"].Value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
            return $"Your Claude sign-in may end any time up to {d:d MMM}. Sign in again so the meter keeps working.";
        return "Your Claude sign-in is ending soon. Sign in again so the meter keeps working.";
    }
}

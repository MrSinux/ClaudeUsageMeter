using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using ClaudeUsageMeter.Core;
using ClaudeUsageMeter.Services;

namespace ClaudeUsageMeter.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BlockViewModel : Observable
{
    private string _label = "";
    private double _percent;
    private UsageLevel _level;
    private string _countdown = "";
    private string _resetAt = "";
    private bool _stale;

    public BlockViewModel(string key) { Key = key; }

    public string Key { get; }
    public string Label
    {
        get => _label;
        set { if (Set(ref _label, value)) Raise(nameof(ShortLabel)); }
    }

    /// <summary>Compact label for the strip: "Session", "Week", "Fable".</summary>
    public string ShortLabel => Shorten(_label);

    private bool _isPrimary;
    /// <summary>The block the strip shows most prominently (the session, else the first).</summary>
    public bool IsPrimary { get => _isPrimary; set => Set(ref _isPrimary, value); }

    public static string Shorten(string label)
    {
        var l = label.Trim();
        var open = l.IndexOf('(');
        var close = l.LastIndexOf(')');
        if (open >= 0 && close > open)
        {
            var inner = l[(open + 1)..close].Trim();
            if (!inner.StartsWith("all", StringComparison.OrdinalIgnoreCase) && inner.Length > 0)
                return char.ToUpperInvariant(inner[0]) + inner[1..];
            l = l[..open].Trim();
        }
        foreach (var prefix in new[] { "Current ", "This " })
            if (l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) l = l[prefix.Length..];
        return l.Length == 0 ? label : char.ToUpperInvariant(l[0]) + l[1..];
    }
    public double Percent
    {
        get => _percent;
        set { if (Set(ref _percent, value)) { Raise(nameof(PercentText)); Raise(nameof(Fraction)); } }
    }
    public string PercentText => Math.Round(_percent).ToString(CultureInfo.InvariantCulture) + "%";
    public double Fraction => Math.Clamp(_percent / 100.0, 0, 1);
    public UsageLevel Level { get => _level; set => Set(ref _level, value); }
    public string Countdown { get => _countdown; set => Set(ref _countdown, value); }
    public string ResetAt { get => _resetAt; set => Set(ref _resetAt, value); }
    public bool IsStale { get => _stale; set => Set(ref _stale, value); }

    public void Update(UsageBlock b, bool stale, DateTimeOffset now)
    {
        Label = b.Label;
        Percent = b.Percent;
        Level = b.Level;
        IsStale = stale;
        Tick(b, now);
    }

    public void Tick(UsageBlock b, DateTimeOffset now)
    {
        Countdown = Core.Countdown.Describe(b, now);
        ResetAt = b.ResetsAt is { } at ? Formats.ResetAt(at.ToLocalTime(), now) : "";
    }
}

public enum ProblemAction { None, SignIn, OpenSettings, Retry }

public sealed class MeterViewModel : Observable
{
    private readonly UsageService _service;
    private DisplayState _state = DisplayState.Initial;
    private bool _isRefreshing;
    private string _updatedText = "";
    private string? _staleText;

    public MeterViewModel(UsageService service)
    {
        _service = service;
        _service.StateChanged += () => Apply(_service.State);
        _service.RefreshingChanged += () => IsRefreshing = _service.IsRefreshing;
        StripBlocks = new System.Windows.Data.ListCollectionView(Blocks)
        {
            Filter = o => o is BlockViewModel b && !StripHidden.Contains(b.Key),
        };
        Apply(_service.State);
    }

    public ObservableCollection<BlockViewModel> Blocks { get; } = [];

    /// <summary>Blocks shown on the taskbar strip (Settings can hide some).</summary>
    public System.Windows.Data.ListCollectionView StripBlocks { get; }

    private HashSet<string> StripHidden { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void SetStripHidden(IEnumerable<string> keys)
    {
        StripHidden = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        StripBlocks.Refresh();
        Raise(nameof(StripHasBlocks));
    }

    public bool StripHasBlocks => HasData && StripBlocks.Count > 0;

    /// <summary>Short text for the strip when there's nothing to show.</summary>
    public string StripProblemText => _state.Status switch
    {
        _ when IsLoading => "Usage",
        UsageStatus.LoggedOut => "Sign in",
        UsageStatus.CliNotFound => "No CLI",
        UsageStatus.Offline => "Offline",
        _ when HasData => "Hidden",
        _ => "Usage",
    };

    public bool HasData => _state.HasData;
    public bool IsLoading => !_state.HasData && _state.LastAttempt is null;
    public bool HasProblem => !_state.HasData && _state.LastAttempt is not null;
    public bool IsStale => _state.IsStale;
    public string? StaleText { get => _staleText; private set => Set(ref _staleText, value); }
    public string? SignInWarning => _state.SignInWarning;
    public bool HasSignInWarning => !string.IsNullOrEmpty(_state.SignInWarning);

    public string ProblemTitle => _state.Status switch
    {
        UsageStatus.LoggedOut => "Sign in again",
        UsageStatus.CliNotFound => "Claude Code not found",
        UsageStatus.Offline => "Offline",
        UsageStatus.Timeout => "No answer from Claude Code",
        UsageStatus.FormatChanged => "Couldn't read usage",
        _ => "Couldn't refresh",
    };

    public string ProblemReason => _state.Reason ?? "";

    public ProblemAction ProblemAction => _state.Status switch
    {
        UsageStatus.LoggedOut => ProblemAction.SignIn,
        UsageStatus.CliNotFound => ProblemAction.OpenSettings,
        _ => ProblemAction.Retry,
    };

    public string ProblemActionText => ProblemAction switch
    {
        ProblemAction.SignIn => "Sign in",
        ProblemAction.OpenSettings => "Open settings",
        _ => "Try again",
    };

    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }

    private void Apply(DisplayState state)
    {
        _state = state;
        var now = DateTimeOffset.Now;

        // Update in place by key so bars animate from their old value.
        var keys = state.Blocks.Select(b => b.Key).ToList();
        for (var i = Blocks.Count - 1; i >= 0; i--)
            if (!keys.Contains(Blocks[i].Key)) Blocks.RemoveAt(i);
        for (var i = 0; i < state.Blocks.Count; i++)
        {
            var b = state.Blocks[i];
            var existing = Blocks.FirstOrDefault(x => x.Key == b.Key);
            if (existing is null)
            {
                existing = new BlockViewModel(b.Key);
                Blocks.Insert(Math.Min(i, Blocks.Count), existing);
            }
            else if (Blocks.IndexOf(existing) != i)
            {
                Blocks.Move(Blocks.IndexOf(existing), i);
            }
            existing.Update(b, state.IsStale, now);
            existing.IsPrimary = ReferenceEquals(b, state.Primary);
        }

        foreach (var name in new[] { nameof(StripHasBlocks), nameof(StripProblemText), nameof(HasData), nameof(IsLoading), nameof(HasProblem), nameof(IsStale), nameof(SignInWarning),
                     nameof(HasSignInWarning), nameof(ProblemTitle), nameof(ProblemReason), nameof(ProblemAction), nameof(ProblemActionText) })
            Raise(name);
        Tick();
    }

    /// <summary>Called every few seconds: countdowns and "updated x ago".</summary>
    public void Tick()
    {
        var now = DateTimeOffset.Now;
        foreach (var vm in Blocks)
        {
            var b = _state.Blocks.FirstOrDefault(x => x.Key == vm.Key);
            if (b is not null) vm.Tick(b, now);
        }
        UpdatedText = _state.DataTime is { } t ? "Updated " + Formats.Ago(t, now) : _state.LastAttempt is { } a ? "Checked " + Formats.Ago(a, now) : "Reading usage...";
        StaleText = _state.IsStale ? $"Couldn't refresh. {_state.Reason}" : null;
    }
}

public static class Formats
{
    public static string Ago(DateTimeOffset t, DateTimeOffset now)
    {
        var d = now - t;
        if (d < TimeSpan.FromSeconds(45)) return "just now";
        if (d < TimeSpan.FromMinutes(1.5)) return "1 min ago";
        if (d < TimeSpan.FromMinutes(60)) return $"{(int)Math.Round(d.TotalMinutes)} min ago";
        return "at " + Time(t.ToLocalTime());
    }

    public static string Time(DateTimeOffset t) =>
        t.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H') ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture).ToLowerInvariant();

    /// <summary>"1:00 am", "Tomorrow 1:00 am", "Thu 1:00 am", "18 Nov".</summary>
    public static string ResetAt(DateTimeOffset at, DateTimeOffset now)
    {
        var today = now.ToLocalTime().Date;
        var day = at.Date;
        if (day == today) return Time(at);
        if (day == today.AddDays(1)) return "Tomorrow " + Time(at);
        if (day < today.AddDays(7)) return at.ToString("ddd", CultureInfo.CurrentCulture) + " " + Time(at);
        return at.ToString("d MMM", CultureInfo.CurrentCulture);
    }
}

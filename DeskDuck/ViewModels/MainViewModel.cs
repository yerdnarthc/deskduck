using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using DeskDuck.Audio;
using DeskDuck.Core;
using DeskDuck.Models;
using DeskDuck.Services;

namespace DeskDuck.ViewModels;

/// <summary>
/// Bridges services and the WPF UI. All NAudio callbacks already arrive on
/// the UI thread (SessionMonitor marshals them), as do the engine timers,
/// so no extra thread-switching is needed here.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly SessionMonitor _monitor;
    private readonly VolumeController _volume = new();
    private readonly DuckEngine _engine;
    private readonly SettingsService _settingsService = new();
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public AppSettings Settings { get; }

    public ObservableCollection<AudioSessionInfo> Sessions { get; } = new();
    public ObservableCollection<DiscoveredApp> AvailableApps { get; } = new();
    public ObservableCollection<TriggerEntry> TriggerOptions { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();

    public RelayCommand TestDuckCommand { get; }

    public MainViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        Settings = _settingsService.Load();

        _monitor = new SessionMonitor(dispatcher, Log);
        _engine = new DuckEngine(() => Settings, _volume, dispatcher, Log);
        _engine.Changed += () =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Reason));
            OnPropertyChanged(nameof(TargetVolumeText));
            UpdateCausingDuck();
        };
        _monitor.SessionsChanged += OnSessionsChanged;
        _monitor.MetersChanged += OnMetersChanged;

        TestDuckCommand = new RelayCommand(() => _engine.TestDuck());

        _monitor.Start();
        OnSessionsChanged(); // initial paint
        Log($"Settings: {Settings.TargetProcessName} | duck {Settings.DuckFactor:0%} | " +
            $"A{Settings.AttackMilliseconds}/H{Settings.HoldMilliseconds}/R{Settings.ReleaseMilliseconds}ms | " +
            $"mode {Settings.TriggerMode} ({Settings.ThresholdDb:0} dB)");
    }

    // ---- bindable settings (validated, auto-saved) ----

    public bool Enabled
    {
        get => Settings.Enabled;
        set
        {
            if (Settings.Enabled == value) return;
            Settings.Enabled = value;
            SaveAndRefresh();
            OnPropertyChanged();
        }
    }

    public string TargetProcessName
    {
        get => Settings.TargetProcessName;
        set
        {
            string normalized = ProcessNames.Normalize(value);
            if (ProcessNames.Matches(Settings.TargetProcessName, normalized)) return;
            Settings.TargetProcessName = string.IsNullOrWhiteSpace(normalized) ? "Spotify" : normalized;
            SaveAndRefresh();
            OnPropertyChanged();
        }
    }

    /// <summary>Duck factor as 1–100 for the slider.</summary>
    public int DuckFactorPercent
    {
        get => (int)Math.Round(Settings.DuckFactor * 100);
        set
        {
            value = Math.Clamp(value, 1, 100);
            if (DuckFactorPercent == value) return;
            Settings.DuckFactor = value / 100.0;
            SaveAndRefresh();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DuckFactorText));
        }
    }

    public string DuckFactorText => $"{DuckFactorPercent}%";

    public int AttackMilliseconds
    {
        get => Settings.AttackMilliseconds;
        set
        {
            value = Math.Clamp(value, 0, 5000);
            if (Settings.AttackMilliseconds == value) return;
            Settings.AttackMilliseconds = value;
            SaveAndRefresh();
            OnPropertyChanged();
        }
    }

    public int HoldMilliseconds
    {
        get => Settings.HoldMilliseconds;
        set
        {
            value = Math.Clamp(value, 0, 10000);
            if (Settings.HoldMilliseconds == value) return;
            Settings.HoldMilliseconds = value;
            SaveAndRefresh();
            OnPropertyChanged();
        }
    }

    public int ReleaseMilliseconds
    {
        get => Settings.ReleaseMilliseconds;
        set
        {
            value = Math.Clamp(value, 0, 10000);
            if (Settings.ReleaseMilliseconds == value) return;
            Settings.ReleaseMilliseconds = value;
            SaveAndRefresh();
            OnPropertyChanged();
        }
    }

    // ---- trigger mode + threshold ----

    public bool IsActivityMode
    {
        get => !string.Equals(Settings.TriggerMode, "Level", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value == IsActivityMode) return;
            Settings.TriggerMode = value ? "Activity" : "Level";
            SaveAndRefresh();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLevelMode));
            Log($"Trigger mode: {Settings.TriggerMode}" +
                (IsLevelMode ? $" (threshold {Settings.ThresholdDb:0} dB)" : " (session ACTIVE state)"));
        }
    }

    public bool IsLevelMode
    {
        get => !IsActivityMode;
        set => IsActivityMode = !value;
    }

    /// <summary>Threshold in dBFS, -60 (anything audible) to 0 (only full-scale).</summary>
    public int ThresholdDb
    {
        get => (int)Math.Round(Settings.ThresholdDb);
        set
        {
            value = Math.Clamp(value, -60, 0);
            if (ThresholdDb == value) return;
            Settings.ThresholdDb = value;
            SaveAndRefresh();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThresholdText));
        }
    }

    public string ThresholdText => $"{ThresholdDb} dB";

    // ---- status ----

    public string StatusText => _engine.State.ToString().ToUpperInvariant();
    public string Reason => _engine.Reason;
    public string DeviceName => _monitor.DeviceName;
    public string TargetVolumeText => $"Target volume: {_engine.CurrentVolume:0%}";

    public int SessionsCount => Sessions.Count;
    public int LogCount => LogLines.Count;

    // Preformatted here (not via Binding.StringFormat in XAML): StringFormat
    // is silently ignored when the target property isn't a string, and
    // Button.Content is object — which is exactly how the buttons ended up
    // showing bare numbers. The underscore keeps the Alt+S / Alt+L keys.
    public string SessionsButtonText => $"_Sessions ({SessionsCount})";
    public string LogButtonText => $"_Log ({LogCount})";

    public void ClearLog()
    {
        LogLines.Clear();
        OnPropertyChanged(nameof(LogCount));
        OnPropertyChanged(nameof(LogButtonText));
    }

    /// <summary>Flags rows currently causing the duck (highlighted in diagnostics).</summary>
    private void UpdateCausingDuck()
    {
        var active = new HashSet<string>(_engine.ActiveTriggers, StringComparer.OrdinalIgnoreCase);
        foreach (var row in Sessions)
            row.IsCausingDuck = active.Contains(row.ProcessName);
    }

    // ---- core reaction: any session/meter change re-evaluates the trigger ----

    private void OnSessionsChanged()
    {
        if (_disposed) return;
        var snapshot = _monitor.GetSnapshot(Settings.TargetProcessName, Settings.TriggerProcesses);

        Sessions.Clear();
        foreach (var s in snapshot) Sessions.Add(s);

        var discovered = _monitor.GetDiscoveredApps();
        foreach (var app in discovered)
            app.Icon = AppIconService.GetIcon(app.ExecutablePath);
        var iconByApp = discovered
            .GroupBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Icon, StringComparer.OrdinalIgnoreCase);
        foreach (var s in snapshot)
            if (iconByApp.TryGetValue(s.ProcessName, out var icon))
                s.Icon = icon;
        SyncAppsInPlace(discovered);

        SyncTriggerOptions(discovered);
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(SessionsCount));
        OnPropertyChanged(nameof(SessionsButtonText));

        RefreshEngine();
    }

    /// <summary>
    /// Updates the dropdown list without ever rebuilding it: no Reset event,
    /// stable item instances, so the editable ComboBox keeps its selection
    /// and text even as sessions come and go. New items slot into sorted order.
    /// </summary>
    private void SyncAppsInPlace(List<DiscoveredApp> discovered)
    {
        for (int i = AvailableApps.Count - 1; i >= 0; i--)
        {
            if (discovered.All(d => !ProcessNames.Matches(d.ProcessName, AvailableApps[i].ProcessName)))
                AvailableApps.RemoveAt(i);
        }
        foreach (var d in discovered)
        {
            var existing = AvailableApps.FirstOrDefault(a => ProcessNames.Matches(a.ProcessName, d.ProcessName));
            if (existing is null)
            {
                int insertAt = AvailableApps.Count;
                for (int i = 0; i < AvailableApps.Count; i++)
                {
                    if (string.Compare(AvailableApps[i].ProcessName, d.ProcessName,
                            StringComparison.OrdinalIgnoreCase) > 0)
                    {
                        insertAt = i;
                        break;
                    }
                }
                AvailableApps.Insert(insertAt, d);
            }
            else
            {
                existing.DisplayName = d.DisplayName;
                existing.HasActiveSession = d.HasActiveSession;
                existing.ExecutablePath = d.ExecutablePath;
                existing.Icon = d.Icon;
            }
        }
    }

    /// <summary>
    /// Meter tick (10x/sec): update dB readouts in place and re-evaluate.
    /// No collection rebuild, no logging — only the engine decision runs.
    /// </summary>
    private void OnMetersChanged()
    {
        if (_disposed) return;
        var levels = _monitor.GetLevels();
        foreach (var row in Sessions)
        {
            if (levels.TryGetValue(row.SessionId, out double db))
                row.LevelDb = db;
        }
        RefreshEngine();
    }

    private void RefreshEngine()
    {
        var readings = _monitor.GetTriggerReadings(Settings.TargetProcessName, Settings.TriggerProcesses);
        string signature = string.Join("|", Sessions.Where(s => s.IsTarget).Select(s => s.SessionId));
        _engine.Refresh(readings, signature);
        FlushEnginePersistence();
    }

    /// <summary>
    /// Persists the engine's pending-restore capture, but only when it changed
    /// (volume ramps never mark dirty, so the 10 Hz ticks stay IO-free).
    /// </summary>
    private void FlushEnginePersistence()
    {
        if (_disposed || !_engine.ConsumePersistenceDirty())
            return;
        Settings.Normalize();
        _settingsService.Save(Settings);
    }

    /// <summary>
    /// Checklist = configured triggers ∪ currently discovered apps, minus the
    /// target itself. The target can never be its own trigger (the engine
    /// ignores it), so offering it as a choice only invites confusion.
    /// </summary>
    private void SyncTriggerOptions(List<DiscoveredApp> discovered)
    {
        var labels = discovered.ToDictionary(d => d.ProcessName, d => d.Label,
            StringComparer.OrdinalIgnoreCase);
        var names = Settings.TriggerProcesses
            .Concat(discovered.Select(d => d.ProcessName))
            .Select(ProcessNames.Normalize)
            .Where(n => !string.IsNullOrEmpty(n))
            .Where(n => !ProcessNames.Matches(n, Settings.TargetProcessName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = TriggerOptions.Count - 1; i >= 0; i--)
            if (!names.Contains(TriggerOptions[i].ProcessName, StringComparer.OrdinalIgnoreCase))
                TriggerOptions.RemoveAt(i);

        var existing = new HashSet<string>(
            TriggerOptions.Select(t => t.ProcessName), StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (existing.Contains(name)) continue;
            labels.TryGetValue(name, out string? label);
            var source = discovered.FirstOrDefault(d => ProcessNames.Matches(d.ProcessName, name));
            bool selected = Settings.TriggerProcesses.Contains(name, StringComparer.OrdinalIgnoreCase);
            TriggerOptions.Add(new TriggerEntry(name, label ?? name, selected, OnTriggerToggled, source?.Icon));
        }
    }

    private void OnTriggerToggled()
    {
        Settings.TriggerProcesses = TriggerOptions
            .Where(t => t.IsSelected)
            .Select(t => t.ProcessName)
            .ToList();
        SaveAndRefresh();
    }

    private void SaveAndRefresh()
    {
        Settings.Normalize();
        _settingsService.Save(Settings);
        OnSessionsChanged();
    }

    private void Log(string message)
    {
        RunOnUi(() =>
        {
            LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            while (LogLines.Count > 300) LogLines.RemoveAt(0);
            OnPropertyChanged(nameof(LogCount));
            OnPropertyChanged(nameof(LogButtonText));
        });
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        // A capture alive at shutdown (e.g. exit via tray mid-duck) becomes a
        // pending restore for the next run; FlushEnginePersistence saves it.
        // (All audio callbacks marshal to this thread, so no teardown race.)
        _engine.CaptureForShutdown();
        FlushEnginePersistence();
        _disposed = true;
        _monitor.Dispose();
        _volume.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

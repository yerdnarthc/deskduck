using System.Windows.Threading;
using DeskDuck.Audio;
using DeskDuck.Models;

namespace DeskDuck.Core;

/// <summary>
/// Deterministic ducking state machine driven by one aggregate signal:
/// "at least one configured trigger session is ACTIVE".
/// Attack/hold/release timings are read fresh from settings on every use.
/// The captured normal volume is never overwritten while ducked, so
/// restoring always returns to the pre-duck level.
/// </summary>
public sealed class DuckEngine
{
    private const int TickMs = 25;

    private readonly Func<AppSettings> _getSettings;
    private readonly VolumeController _volume;
    private readonly Action<string> _log;
    private readonly DispatcherTimer _rampTimer;
    private readonly DispatcherTimer _holdTimer;

    private DuckState _state = DuckState.Normal;
    private float _rampFrom;
    private float _rampTo;
    private DateTime _rampStart;
    private int _rampDurationMs;

    private float _normalVolume = 1f;
    private float _currentVolume = 1f;
    private bool _normalCaptured;
    private bool _releaseQueued; // trigger vanished mid-attack: hold once the ramp lands
    private string _appliedSignature = string.Empty; // target sessions we last applied volume to

    /// <summary>
    /// True when we knocked the target down but never got to put it back
    /// (release/disable completed while the target was absent). The captured
    /// volume is retained until the target returns and is reconciled.
    /// </summary>
    private bool _pendingRestore;

    private enum TestPhase { None, Attack, Hold, Release }
    private TestPhase _test = TestPhase.None;

    /// <summary>
    /// Set whenever the persisted pending-capture changes, so the ViewModel
    /// can save settings. Never set by volume ramps (10 Hz ticks stay IO-free).
    /// </summary>
    private bool _persistenceDirty;

    /// <summary>Returns and clears the persistence-dirty flag.</summary>
    public bool ConsumePersistenceDirty()
    {
        bool dirty = _persistenceDirty;
        _persistenceDirty = false;
        return dirty;
    }

    public DuckState State => _state;
    public string Reason { get; private set; } = string.Empty;
    public float CurrentVolume => _currentVolume;

    /// <summary>Raw process names currently causing the duck (for diagnostics).</summary>
    public IReadOnlyList<string> ActiveTriggers { get; private set; } = [];

    public event Action? Changed;

    public DuckEngine(Func<AppSettings> getSettings, VolumeController volume,
        Dispatcher dispatcher, Action<string> log)
    {
        _getSettings = getSettings;
        _volume = volume;
        _log = log;
        _rampTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(TickMs)
        };
        _rampTimer.Tick += OnRampTick;
        _holdTimer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher);
        _holdTimer.Tick += OnHoldElapsed;

        // A previous run may have exited mid-duck (or with the target gone),
        // leaving an un-restored volume on disk. Seed it; Refresh reconciles.
        var settings = _getSettings();
        if (settings.PendingRestoreVolume is float pending
            && !float.IsNaN(pending) && pending >= 0 && pending <= 1
            && ProcessNames.Matches(settings.PendingRestoreTarget, settings.TargetProcessName))
        {
            _normalVolume = pending;
            _normalCaptured = true;
            _pendingRestore = true;
            _log($"Recovered un-restored volume {pending:0.00} for {settings.TargetProcessName} (saved before exit).");
        }
        else if (settings.PendingRestoreVolume.HasValue)
        {
            // Belongs to a different target now: drop it on next save.
            settings.PendingRestoreVolume = null;
            settings.PendingRestoreTarget = string.Empty;
            _persistenceDirty = true;
        }
    }

    /// <summary>
    /// Mirrors the live capture into settings and marks persistence dirty.
    /// Persists whenever a capture exists (duck in progress counts — a kill
    /// or crash must still reconcile next run); clears only when the capture
    /// genuinely completes. Call on every capture mutation — never from ramps.
    /// </summary>
    private void SyncPersistedCapture()
    {
        var settings = _getSettings();
        if (_normalCaptured)
        {
            settings.PendingRestoreVolume = _normalVolume;
            settings.PendingRestoreTarget = settings.TargetProcessName;
        }
        else
        {
            settings.PendingRestoreVolume = null;
            settings.PendingRestoreTarget = string.Empty;
        }
        _persistenceDirty = true;
    }

    /// <summary>
    /// Called on application exit. A live capture is restored immediately so
    /// nothing is left ducked behind us; a missing target keeps the pending
    /// path for the next run. Either way the disk ends consistent.
    /// </summary>
    public void RestoreForExit()
    {
        if (!_normalCaptured)
            return;
        var settings = _getSettings();
        if (_volume.GetTargetVolume(settings.TargetProcessName) is null)
        {
            _pendingRestore = true;
            SyncPersistedCapture();
            _log("Exiting with target missing — restore pending.");
            return;
        }
        _volume.SetTargetVolume(settings.TargetProcessName, _normalVolume);
        _currentVolume = _normalVolume;
        _normalCaptured = false;
        _pendingRestore = false;
        _appliedSignature = string.Empty;
        SyncPersistedCapture();
        _log($"Restored {_normalVolume:0.00} on exit.");
    }

    /// <summary>
    /// Gap (dB) between engaging and releasing in Level mode. Without this,
    /// audio hovering at the threshold would flicker duck on/off rapidly.
    /// </summary>
    private const double HysteresisDb = 3.0;

    // Trigger apps currently latched "over threshold" in Level mode.
    private readonly HashSet<string> _levelLatched = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Re-evaluate after any session/meter change.
    /// targetSignature identifies the current target sessions (ids joined);
    /// when it changes mid-duck we (re-)apply the ducked volume so a target
    /// that (re-)launches while a trigger is active gets ducked immediately.
    /// </summary>
    public void Refresh(IReadOnlyList<TriggerReading> readings, string targetSignature)
    {
        if (_test != TestPhase.None) return;
        var settings = _getSettings();

        if (!settings.Enabled)
        {
            if (_state is DuckState.Ducking or DuckState.Ducked or DuckState.Holding or DuckState.Releasing)
                RestoreNow("Disabled — restoring target volume.", targetSignature);
            SetState(DuckState.Disabled, string.Empty);
            return;
        }
        if (_state == DuckState.Disabled)
            SetState(DuckState.Normal, string.Empty);

        List<string> activeTriggers = ResolveTriggers(readings, settings);
        ActiveTriggers = activeTriggers;
        bool shouldDuck = activeTriggers.Count > 0;
        string reason = shouldDuck ? BuildReason(readings, settings, activeTriggers) : string.Empty;

        // A target that vanished mid-duck keeps Windows' persisted (ducked)
        // volume on return. If we still hold its pre-duck level, reconcile now.
        // targetSignature comes from the snapshot, so this costs no extra IO.
        if (_pendingRestore && !string.IsNullOrEmpty(targetSignature))
        {
            if (shouldDuck)
            {
                // Trigger still active: duck from the live level but restore
                // to the retained one later — never capture the stuck value.
                _pendingRestore = false;
                BeginDuckRetained(reason);
            }
            else
            {
                // Only our own residue qualifies: at or below our ducked level
                // and below the retained normal. Anything else means the user
                // (or something else) set this volume deliberately — stand down.
                float? actual = _volume.GetTargetVolume(settings.TargetProcessName);
                float duckedLevel = _normalVolume * (float)settings.DuckFactor;
                if (actual is null)
                {
                    _pendingRestore = true; // lost the race; retry on a later refresh
                }
                else if (actual.Value < _normalVolume - 0.005f
                    && actual.Value <= duckedLevel + 0.02f)
                {
                    _volume.SetTargetVolume(settings.TargetProcessName, _normalVolume);
                    _currentVolume = _normalVolume;
                    _appliedSignature = targetSignature;
                    _log($"Target returned at {actual:0.00} — restored to {_normalVolume:0.00}.");
                    _pendingRestore = false;
                    _normalCaptured = false;
                    SyncPersistedCapture();
                    Changed?.Invoke();
                }
                else
                {
                    _log($"Target returned at {actual:0.00} — leaving alone.");
                    _pendingRestore = false;
                    _normalCaptured = false;
                    SyncPersistedCapture();
                }
            }
        }

        if (shouldDuck)
        {
            _holdTimer.Stop();
            _releaseQueued = false;
            switch (_state)
            {
                case DuckState.Normal:
                    BeginDuck(reason);
                    break;
                case DuckState.Holding:
                    SetState(DuckState.Ducked, reason);
                    _log("Trigger returned during hold — staying ducked.");
                    break;
                case DuckState.Releasing:
                    // Reverse direction mid-release: glide back down to ducked.
                    StartRamp(_currentVolume, DuckedVolume(), _getSettings().AttackMilliseconds);
                    SetState(DuckState.Ducking, reason);
                    break;
                case DuckState.Ducked when !_normalCaptured:
                    // Target appeared while ducked (was missing at duck start).
                    BeginDuck(reason);
                    break;
                default:
                    Reason = reason; // stay ducked/ducking, keep reason fresh
                    break;
            }
            // New target session(s) while ducked: assert the ducked volume on them.
            if ((_state is DuckState.Ducking or DuckState.Ducked) && _normalCaptured
                && targetSignature != _appliedSignature)
            {
                _volume.SetTargetVolume(settings.TargetProcessName, _currentVolume);
                _appliedSignature = targetSignature;
            }
        }
        else if (_state == DuckState.Ducked)
        {
            bool levelMode = string.Equals(settings.TriggerMode, "Level", StringComparison.OrdinalIgnoreCase);
            SetState(DuckState.Holding, levelMode ? "all triggers below threshold — holding" : "all triggers inactive — holding");
            _holdTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, settings.HoldMilliseconds));
            _holdTimer.Start();
            _log($"Hold started: {settings.HoldMilliseconds} ms");
        }
        else if (_state == DuckState.Ducking)
        {
            _releaseQueued = true; // finish the attack, then hold + release
        }
    }

    /// <summary>
    /// Turns live readings into the duck/no-duck decision.
    /// Activity mode: any ACTIVE session ducks (loudness ignored).
    /// Level mode: an app latches "triggering" at the threshold and only
    /// unlatches 3 dB below it (hysteresis against flicker).
    /// </summary>
    private List<string> ResolveTriggers(IReadOnlyList<TriggerReading> readings, AppSettings settings)
    {
        bool levelMode = string.Equals(settings.TriggerMode, "Level", StringComparison.OrdinalIgnoreCase);
        if (!levelMode)
        {
            _levelLatched.Clear();
            return readings.Where(r => r.IsActive).Select(r => r.Name).ToList();
        }

        foreach (var r in readings)
        {
            if (!r.IsActive)
            {
                if (_levelLatched.Remove(r.Name))
                    _log($"{r.Name} went inactive — unlatching.");
                continue;
            }
            if (_levelLatched.Contains(r.Name))
            {
                if (r.PeakDb < settings.ThresholdDb - HysteresisDb)
                {
                    _levelLatched.Remove(r.Name);
                    _log($"{r.Name} fell to {r.PeakDb:0} dB (release below {settings.ThresholdDb - HysteresisDb:0} dB).");
                }
            }
            else if (r.PeakDb >= settings.ThresholdDb)
            {
                _levelLatched.Add(r.Name);
                _log($"{r.Name} hit {r.PeakDb:0} dB (threshold {settings.ThresholdDb:0} dB).");
            }
        }
        // Drop apps that vanished entirely (e.g. process closed).
        _levelLatched.RemoveWhere(name => readings.All(r => r.Name != name));

        // Raw names only — BuildReason adds the dB details for display.
        return readings
            .Where(r => _levelLatched.Contains(r.Name))
            .Select(r => r.Name)
            .ToList();
    }

    private static string BuildReason(
        IReadOnlyList<TriggerReading> readings, AppSettings settings, List<string> active)
    {
        if (!string.Equals(settings.TriggerMode, "Level", StringComparison.OrdinalIgnoreCase))
            return $"{string.Join(", ", active)} is ACTIVE";
        var parts = readings
            .Where(r => active.Contains(r.Name, StringComparer.OrdinalIgnoreCase))
            .Select(r => $"{r.Name} ({r.PeakDb:0} dB ≥ {settings.ThresholdDb:0} dB)");
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Verifies the volume path without needing a real trigger:
    /// normal → duck → brief hold → restore.
    /// </summary>
    public void TestDuck()
    {
        var settings = _getSettings();
        if (_test != TestPhase.None) return;
        if (_state is not DuckState.Normal)
        {
            _log("Test Duck skipped — a duck cycle is already active.");
            return;
        }
        float? actual = _volume.GetTargetVolume(settings.TargetProcessName);
        if (actual is null)
        {
            _log($"Test Duck: target '{settings.TargetProcessName}' has no audio session.");
            return;
        }
        _test = TestPhase.Attack;
        _normalVolume = actual.Value;
        _normalCaptured = true;
        _currentVolume = actual.Value;
        _appliedSignature = string.Empty; // force re-assert, not needed but harmless
        SyncPersistedCapture();
        _log($"Test Duck: {actual:0.00} -> ducking");
        StartRamp(actual.Value, DuckedVolume(), settings.AttackMilliseconds);
        ActiveTriggers = []; // manual test isn't a trigger — clear stale highlights
        SetState(DuckState.Ducking, "manual test");
    }

    private void BeginDuck(string reason)
    {
        var settings = _getSettings();
        float? actual = _volume.GetTargetVolume(settings.TargetProcessName);
        if (actual is null)
        {
            // Target not running (yet): stay logically ducked so a target
            // appearing mid-duck gets ducked on the next refresh.
            _normalCaptured = false;
            SetState(DuckState.Ducked, reason + " (target not found)");
            _log("Trigger active but target has no session — waiting for target.");
            return;
        }
        _normalVolume = actual.Value;
        _normalCaptured = true;
        _currentVolume = actual.Value;
        if (_pendingRestore)
        {
            // Fresh capture supersedes any retained one.
            _pendingRestore = false;
            SyncPersistedCapture();
        }
        StartRamp(actual.Value, DuckedVolume(), settings.AttackMilliseconds);
        SetState(DuckState.Ducking, reason);
        _log($"{settings.TargetProcessName} volume: {actual:0.00} -> {DuckedVolume():0.00}");
    }

    /// <summary>
    /// Duck when a retained pre-duck level exists (target returned after we
    /// lost it mid-cycle). Starts the ramp from the live level but restores
    /// to the retained one — never captures the stuck-low value as normal.
    /// </summary>
    private void BeginDuckRetained(string reason)
    {
        var settings = _getSettings();
        float? actual = _volume.GetTargetVolume(settings.TargetProcessName);
        if (actual is null)
        {
            // Target gone again before we could act: stay pending.
            _pendingRestore = true;
            return;
        }
        _currentVolume = actual.Value;
        StartRamp(actual.Value, DuckedVolume(), settings.AttackMilliseconds);
        SetState(DuckState.Ducking, reason);
        _log($"{settings.TargetProcessName} returned quiet — ducking from retained {_normalVolume:0.00}.");
        SyncPersistedCapture(); // pending cleared above; persist the clean slate
    }

    private float DuckedVolume()
    {
        var settings = _getSettings();
        return _normalVolume * (float)settings.DuckFactor;
    }

    private void StartRamp(float from, float to, int durationMs)
    {
        _rampFrom = from;
        _rampTo = to;
        _rampDurationMs = Math.Max(0, durationMs);
        _rampStart = DateTime.UtcNow;
        if (_rampDurationMs == 0)
        {
            _currentVolume = to;
            ApplyCurrent();
            OnRampFinished();
        }
        else
        {
            _currentVolume = from;
            _rampTimer.Start();
        }
    }

    private void OnRampTick(object? sender, EventArgs e)
    {
        double elapsed = (DateTime.UtcNow - _rampStart).TotalMilliseconds;
        double t = _rampDurationMs <= 0 ? 1 : Math.Min(1, elapsed / _rampDurationMs);
        _currentVolume = (float)(_rampFrom + (_rampTo - _rampFrom) * t);
        ApplyCurrent();
        Changed?.Invoke(); // live volume readout during ramps
        if (t >= 1)
        {
            _rampTimer.Stop();
            OnRampFinished();
        }
    }

    private void ApplyCurrent()
    {
        var settings = _getSettings();
        _volume.SetTargetVolume(settings.TargetProcessName, _currentVolume);
    }

    private void OnRampFinished()
    {
        if (_state == DuckState.Ducking)
        {
            SetState(DuckState.Ducked, Reason);
            if (_test == TestPhase.Attack)
            {
                _test = TestPhase.Hold;
                _holdTimer.Interval = TimeSpan.FromMilliseconds(400);
                _holdTimer.Start();
            }
            else if (_releaseQueued)
            {
                _releaseQueued = false;
                // Attack finished but the trigger already vanished: go to hold.
                var settings = _getSettings();
                SetState(DuckState.Holding, "all triggers inactive — holding");
                _holdTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, settings.HoldMilliseconds));
                _holdTimer.Start();
            }
        }
        else if (_state == DuckState.Releasing)
        {
            var settings = _getSettings();
            // Only forget the pre-duck level if the restore could land.
            // A missing target keeps the capture so its return reconciles.
            if (_volume.GetTargetVolume(settings.TargetProcessName) is null && _normalCaptured)
            {
                _pendingRestore = true;
                SyncPersistedCapture();
                _log($"Target missing — will restore {_normalVolume:0.00} when it returns.");
            }
            else
            {
                _normalCaptured = false;
                _appliedSignature = string.Empty;
                SyncPersistedCapture();
            }
            SetState(DuckState.Normal, string.Empty);
            if (_test == TestPhase.Release)
            {
                _test = TestPhase.None;
                _log("Test Duck complete — restored.");
            }
            else
            {
                _log("Restored.");
            }
        }
    }

    private void OnHoldElapsed(object? sender, EventArgs e)
    {
        _holdTimer.Stop();
        var settings = _getSettings();
        if (_test == TestPhase.Hold)
        {
            _test = TestPhase.Release;
            _log("Test Duck: restoring");
            StartRamp(_currentVolume, _normalVolume, settings.ReleaseMilliseconds);
            SetState(DuckState.Releasing, "manual test");
            return;
        }
        if (!_normalCaptured)
        {
            // Target was missing the whole duck: nothing to restore.
            SetState(DuckState.Normal, string.Empty);
            return;
        }
        _log($"Hold completed — restoring {_currentVolume:0.00} -> {_normalVolume:0.00}");
        StartRamp(_currentVolume, _normalVolume, settings.ReleaseMilliseconds);
        SetState(DuckState.Releasing, Reason);
    }

    private void RestoreNow(string why, string targetSignature)
    {
        var settings = _getSettings();
        _rampTimer.Stop();
        _holdTimer.Stop();
        _releaseQueued = false;
        _test = TestPhase.None;
        if (_normalCaptured)
        {
            if (string.IsNullOrEmpty(targetSignature))
            {
                _pendingRestore = true;
                SyncPersistedCapture();
                _log("Disabled with target missing — restore pending.");
            }
            else
            {
                _volume.SetTargetVolume(settings.TargetProcessName, _normalVolume);
                _currentVolume = _normalVolume;
                _normalCaptured = false;
                _appliedSignature = string.Empty;
                SyncPersistedCapture();
                _log(why);
            }
        }
        SetState(DuckState.Normal, string.Empty);
    }

    private void SetState(DuckState state, string reason)
    {
        _state = state;
        Reason = reason;
        Changed?.Invoke();
    }
}

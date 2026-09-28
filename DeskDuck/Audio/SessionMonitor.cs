using System.Diagnostics;
using System.Windows.Threading;
using DeskDuck.Models;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace DeskDuck.Audio;

/// <summary>
/// Tracks Windows audio sessions on the default render device.
/// Event-driven: session state changes arrive via IAudioSessionEventsHandler,
/// new sessions via AudioSessionManager.OnSessionCreated, device switches via
/// MMDeviceNotificationClient. Callbacks stay lightweight — they capture the
/// change and marshal to the UI thread for everything else.
/// </summary>
public sealed class SessionMonitor : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<string> _log;

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioSessionManager? _manager;
    private MMDeviceNotificationClient? _deviceNotifications;

    // Keyed by session instance id. Guarded by _gate; snapshots are copies.
    private readonly object _gate = new();
    private readonly Dictionary<string, TrackedSession> _sessions = new();

    // Stored so we can unsubscribe cleanly. The delegate's session parameter
    // is an NAudio-internal type, so the lambda lets the compiler infer it
    // instead of us naming it.
    private AudioSessionManager.SessionCreatedDelegate? _sessionCreatedHandler;

    private bool _disposed;

    /// <summary>Raised (on the UI thread) whenever the session list or any state changes.</summary>
    public event Action? SessionsChanged;

    public string DeviceName { get; private set; } = "(none)";

    public SessionMonitor(Dispatcher dispatcher, Action<string> log)
    {
        _dispatcher = dispatcher;
        _log = log;
    }

    public void Start()
    {
        _dispatcher.VerifyAccess();
        Initialize();
    }

    private void Initialize()
    {
        TearDownAudioObjects();

        _enumerator = new MMDeviceEnumerator();
        // true = marshal device-change events back to this thread's context.
        _deviceNotifications = _enumerator.CreateNotificationClient(true);
        _deviceNotifications.DefaultDeviceChanged += OnDefaultDeviceChanged;

        if (!_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
        {
            _log("No default render device found.");
            SessionsChanged?.Invoke();
            return;
        }

        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        DeviceName = _device.FriendlyName;
        _manager = _device.AudioSessionManager;
        _sessionCreatedHandler = (sender, newSession) =>
        {
            // A new session appeared: re-enumerate and adopt anything unknown.
            // (We deliberately don't wrap the raw session object — its type is
            // NAudio-internal. RefreshSessions + Sessions[] is all public API.)
            RunOnUi(() =>
            {
                if (_disposed) return;
                int before;
                lock (_gate) before = _sessions.Count;
                AdoptNewSessions();
                lock (_gate)
                {
                    if (_sessions.Count != before)
                        _log($"Session created ({_sessions.Count} tracked).");
                }
                SessionsChanged?.Invoke();
            });
        };
        _manager.OnSessionCreated += _sessionCreatedHandler;
        AdoptNewSessions();

        _log($"Monitoring '{DeviceName}': {_sessions.Count} session(s).");
        SessionsChanged?.Invoke();
    }

    public List<AudioSessionInfo> GetSnapshot(string target, IEnumerable<string> triggers)
    {
        var triggerSet = new HashSet<string>(triggers.Select(ProcessNames.Normalize),
            StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            return _sessions.Values.Select(t => new AudioSessionInfo
            {
                SessionId = t.InstanceId,
                ProcessId = t.ProcessId,
                ProcessName = t.ProcessName,
                DisplayName = t.DisplayName,
                State = t.State,
                IsSystemSounds = t.IsSystemSounds,
                IsTarget = ProcessNames.Matches(t.ProcessName, target),
                IsConfiguredTrigger = triggerSet.Contains(ProcessNames.Normalize(t.ProcessName))
            }).OrderBy(s => s.ProcessName).ThenBy(s => s.SessionId).ToList();
        }
    }

    /// <summary>
    /// The canonical trigger: a configured trigger process with an ACTIVE session.
    /// Process-exists alone never counts. Returns the names of triggering apps.
    /// </summary>
    public List<string> GetActiveTriggers(string target, IEnumerable<string> triggers)
    {
        var snapshot = GetSnapshot(target, triggers);
        return snapshot
            .Where(s => s.State == "Active" && s.IsConfiguredTrigger && !s.IsTarget)
            .Select(s => s.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Distinct apps currently having sessions, for target/trigger pickers.</summary>
    public List<DiscoveredApp> GetDiscoveredApps()
    {
        lock (_gate)
        {
            return _sessions.Values
                .GroupBy(t => ProcessNames.Normalize(t.ProcessName), StringComparer.OrdinalIgnoreCase)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g => new DiscoveredApp
                {
                    ProcessName = g.Key,
                    DisplayName = g.First().DisplayName,
                    HasActiveSession = g.Any(t => t.State == "Active")
                })
                .OrderBy(a => a.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    // ---- events (may arrive on audio threads) ----

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow != DataFlow.Render)
            return;
        RunOnUi(() =>
        {
            if (_disposed) return;
            _log("Default output device changed — reinitializing.");
            Initialize();
        });
    }

    internal void OnTrackedStateChanged(string instanceId)
    {
        RunOnUi(() =>
        {
            if (_disposed) return;
            string state, name;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(instanceId, out var t)) return;
                try { t.State = ToShortState(t.Control.State); }
                catch { t.State = "Expired"; }
                state = t.State;
                name = t.ProcessName;
                if (state == "Expired")
                {
                    _sessions.Remove(instanceId);
                    t.Dispose();
                }
            }
            _log($"{name} state: {state}");
            SessionsChanged?.Invoke();
        });
    }

    // ---- internals ----

    /// <summary>
    /// Enumerates current sessions and tracks any instance id not seen yet.
    /// Duplicate wrappers for already-known sessions are disposed.
    /// </summary>
    private void AdoptNewSessions()
    {
        if (_manager is null) return;
        try
        {
            _manager.RefreshSessions();
            for (int i = 0; i < _manager.Sessions.Count; i++)
            {
                AudioSessionControl control = _manager.Sessions[i];
                string id;
                try { id = control.GetSessionInstanceIdentifier; }
                catch { control.Dispose(); continue; }
                bool known;
                lock (_gate) known = _sessions.ContainsKey(id);
                if (known) control.Dispose();
                else Track(control);
            }
        }
        catch { /* device vanished mid-enumeration; next init recovers */ }
    }

    private void Track(AudioSessionControl control)
    {
        var entry = new TrackedSession(control, OnTrackedStateChanged);
        lock (_gate)
        {
            if (_sessions.TryGetValue(entry.InstanceId, out var old))
            {
                old.Dispose();
                _sessions.Remove(entry.InstanceId);
            }
            _sessions[entry.InstanceId] = entry;
        }
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }

    private void TearDownAudioObjects()
    {
        if (_manager is not null)
        {
            if (_sessionCreatedHandler is not null)
                _manager.OnSessionCreated -= _sessionCreatedHandler;
            _manager = null;
        }
        lock (_gate)
        {
            foreach (var t in _sessions.Values) t.Dispose();
            _sessions.Clear();
        }
        _device?.Dispose();
        _device = null;
        if (_deviceNotifications is not null)
        {
            _deviceNotifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
            _deviceNotifications.Dispose();
            _deviceNotifications = null;
        }
        // NOTE: keep _enumerator alive across re-inits would also be fine,
        // but recreating keeps lifetimes obvious.
        _enumerator?.Dispose();
        _enumerator = null;
    }

    public void Dispose()
    {
        _disposed = true;
        TearDownAudioObjects();
    }

    /// <summary>
    /// NAudio's enum members are named AudioSessionStateActive etc.;
    /// map to the short names the rest of the app (and diagnostics) uses.
    /// </summary>
    internal static string ToShortState(AudioSessionState state) => state switch
    {
        AudioSessionState.AudioSessionStateActive => "Active",
        AudioSessionState.AudioSessionStateExpired => "Expired",
        _ => "Inactive",
    };

    /// <summary>
    /// One live session: owns the AudioSessionControl and forwards
    /// OnStateChanged to the monitor. Everything else is cached data.
    /// </summary>
    private sealed class TrackedSession : IAudioSessionEventsHandler
    {
        private readonly Action<string> _stateChanged;
        public AudioSessionControl Control { get; }
        public string InstanceId { get; }
        public uint ProcessId { get; }
        public string ProcessName { get; }
        public string DisplayName { get; }
        public bool IsSystemSounds { get; }
        public string State { get; set; }

        public TrackedSession(AudioSessionControl control, Action<string> stateChanged)
        {
            Control = control;
            _stateChanged = stateChanged;
            string instanceId, display;
            uint pid;
            AudioSessionState state;
            try
            {
                instanceId = control.GetSessionInstanceIdentifier;
                pid = control.GetProcessID;
                display = control.DisplayName;
                state = control.State;
                IsSystemSounds = control.IsSystemSoundsSession;
            }
            catch
            {
                instanceId = Guid.NewGuid().ToString();
                pid = 0;
                display = string.Empty;
                state = AudioSessionState.AudioSessionStateExpired;
            }
            InstanceId = instanceId;
            ProcessId = pid;
            State = ToShortState(state);
            DisplayName = string.IsNullOrWhiteSpace(display) ? ResolveProcessName(pid) : display;
            ProcessName = ResolveProcessName(pid);
            try { Control.RegisterEventClient(this); } catch { /* session already dead */ }
        }

        private static string ResolveProcessName(uint pid)
        {
            try
            {
                using var p = Process.GetProcessById((int)pid);
                return p.ProcessName;
            }
            catch { return pid == 0 ? "System" : $"PID {pid}"; }
        }

        public void Dispose()
        {
            try { Control.UnRegisterEventClient(this); } catch { }
            try { Control.Dispose(); } catch { }
        }

        // IAudioSessionEventsHandler — must stay cheap; just forward the id.
        public void OnStateChanged(AudioSessionState state) => _stateChanged(InstanceId);
        public void OnVolumeChanged(float volume, bool isMuted) { }
        public void OnChannelVolumeChanged(uint channelCount, IntPtr newVolumes, uint channelIndex) { }
        public void OnDisplayNameChanged(string displayName) { }
        public void OnGroupingParamChanged(ref Guid groupingId) { }
        public void OnIconPathChanged(string iconPath) { }
        public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason) => _stateChanged(InstanceId);
    }
}

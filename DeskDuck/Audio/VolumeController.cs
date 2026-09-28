using System.Diagnostics;
using DeskDuck.Models;
using NAudio.CoreAudioApi;

namespace DeskDuck.Audio;

/// <summary>
/// Reads/writes the per-session volume of the target music app only.
/// Never touches master or device volume. Each operation re-enumerates
/// sessions so closed/restarted targets can never leave stale COM objects.
/// </summary>
public sealed class VolumeController : IDisposable
{
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _device;
    private AudioSessionManager? _manager;
    private bool _disposed;

    public void Invalidate()
    {
        // Must never throw: called from error paths and shutdown.
        try { _device?.Dispose(); } catch { }
        _manager = null;
        _device = null;
        try { _enumerator?.Dispose(); } catch { }
        _enumerator = null;
    }

    private bool EnsureManager()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            if (!_enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                return false;
            if (_device is null)
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _manager = _device.AudioSessionManager;
            }
            return _manager is not null;
        }
        catch
        {
            Invalidate();
            return false;
        }
    }

    /// <summary>Volume of the first target session found (0–1), or null if absent.</summary>
    public float? GetTargetVolume(string target)
    {
        if (_disposed || !EnsureManager()) return null;
        try
        {
            _manager!.RefreshSessions();
            for (int i = 0; i < _manager.Sessions.Count; i++)
            {
                using var session = _manager.Sessions[i];
                if (ProcessNames.Matches(ResolveProcessName(session), target))
                    return session.SimpleAudioVolume.Volume;
            }
        }
        catch { Invalidate(); }
        return null;
    }

    /// <summary>Sets volume on ALL sessions of the target process. No-op if absent.</summary>
    public void SetTargetVolume(string target, float volume)
    {
        if (_disposed || !EnsureManager()) return;
        volume = Math.Clamp(volume, 0f, 1f);
        try
        {
            _manager!.RefreshSessions();
            for (int i = 0; i < _manager.Sessions.Count; i++)
            {
                using var session = _manager.Sessions[i];
                if (ProcessNames.Matches(ResolveProcessName(session), target))
                {
                    try { session.SimpleAudioVolume.Volume = volume; } catch { /* session died mid-loop */ }
                }
            }
        }
        catch { Invalidate(); }
    }

    private static string ResolveProcessName(AudioSessionControl session)
    {
        try
        {
            using var p = Process.GetProcessById((int)session.GetProcessID);
            return p.ProcessName;
        }
        catch { return string.Empty; }
    }

    public void Dispose()
    {
        _disposed = true;
        Invalidate();
    }
}

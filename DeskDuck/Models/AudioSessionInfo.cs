using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeskDuck.Models;

/// <summary>
/// Normalized snapshot of one Windows audio session.
/// Kept free of NAudio/COM references so the UI can bind to it safely.
/// LevelDb is updated in place by the meter poll (hence INotifyPropertyChanged).
/// </summary>
public sealed class AudioSessionInfo : INotifyPropertyChanged
{
    public string SessionId { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Active / Inactive / Expired, as reported by Windows.</summary>
    public string State { get; set; } = "Inactive";

    /// <summary>Current peak level in dBFS, floored at -60 (silence).</summary>
    private double _levelDb = -60;
    public double LevelDb
    {
        get => _levelDb;
        set
        {
            if (_levelDb == value) return;
            _levelDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LevelText));
        }
    }

    public string LevelText => $"{_levelDb:0} dB";

    public bool IsSystemSounds { get; init; }
    public bool IsTarget { get; set; }
    public bool IsConfiguredTrigger { get; set; }

    public string Role => IsTarget ? "TARGET" : IsConfiguredTrigger ? "TRIGGER" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// One trigger app's live level, fed to the duck engine each evaluation.
/// PeakDb is the loudest of the app's ACTIVE sessions (-60 when none).
/// </summary>
public sealed record TriggerReading(string Name, double PeakDb, bool IsActive);

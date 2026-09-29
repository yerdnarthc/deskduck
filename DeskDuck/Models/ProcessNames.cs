using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace DeskDuck.Models;

/// <summary>
/// One discoverable audio-producing application (grouped by process name).
/// Used for the target dropdown and the trigger checklist.
/// Instances are kept stable across refreshes (updated in place, never
/// rebuilt) so ComboBox selection and edit text survive session changes.
/// </summary>
public sealed class DiscoveredApp : INotifyPropertyChanged
{
    public string ProcessName { get; init; } = string.Empty;

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
        }
    }

    private bool _hasActiveSession;
    public bool HasActiveSession
    {
        get => _hasActiveSession;
        set
        {
            if (_hasActiveSession == value) return;
            _hasActiveSession = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Exe path behind this app (for icon lookup). May be empty.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    private ImageSource? _icon;

    /// <summary>App icon, resolved by the ViewModel. Null = text only.</summary>
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (_icon == value) return;
            _icon = value;
            OnPropertyChanged();
        }
    }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == ProcessName
        ? ProcessName
        : $"{DisplayName} ({ProcessName})";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Case-insensitive process-name matching. "Spotify.exe" == "spotify".
/// </summary>
public static class ProcessNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        name = name.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }

    public static bool Matches(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
}

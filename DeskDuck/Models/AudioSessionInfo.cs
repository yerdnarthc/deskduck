namespace DeskDuck.Models;

/// <summary>
/// Normalized snapshot of one Windows audio session.
/// Kept free of NAudio/COM references so the UI can bind to it safely.
/// </summary>
public sealed class AudioSessionInfo
{
    public string SessionId { get; init; } = string.Empty;
    public uint ProcessId { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Active / Inactive / Expired, as reported by Windows.</summary>
    public string State { get; set; } = "Inactive";

    public bool IsSystemSounds { get; init; }
    public bool IsTarget { get; set; }
    public bool IsConfiguredTrigger { get; set; }

    public string Role => IsTarget ? "TARGET" : IsConfiguredTrigger ? "TRIGGER" : string.Empty;
}

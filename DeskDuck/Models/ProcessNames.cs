namespace DeskDuck.Models;

/// <summary>
/// One discoverable audio-producing application (grouped by process name).
/// Used for the target dropdown and the trigger checklist.
/// </summary>
public sealed class DiscoveredApp
{
    public string ProcessName { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool HasActiveSession { get; set; }

    public string Label => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == ProcessName
        ? ProcessName
        : $"{DisplayName} ({ProcessName})";
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

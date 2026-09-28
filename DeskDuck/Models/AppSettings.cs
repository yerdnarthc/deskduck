namespace DeskDuck.Models;

/// <summary>
/// Persisted user configuration. Only the core MVP settings;
/// optional extras (tray, autostart) are intentionally left out.
/// </summary>
public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Canonical process name of the music app, e.g. "Spotify". No ".exe".</summary>
    public string TargetProcessName { get; set; } = "Spotify";

    public List<string> TriggerProcesses { get; set; } = new() { "FL Studio", "Godot" };

    /// <summary>Relative multiplier applied to the target volume while ducked. 0.05–1.00.</summary>
    public double DuckFactor { get; set; } = 0.25;

    public int AttackMilliseconds { get; set; } = 100;
    public int HoldMilliseconds { get; set; } = 400;
    public int ReleaseMilliseconds { get; set; } = 800;

    /// <summary>
    /// "Activity" = duck while a trigger session is ACTIVE (ignores loudness).
    /// "Level" = duck while a trigger session's peak level is above ThresholdDb.
    /// </summary>
    public string TriggerMode { get; set; } = "Activity";

    /// <summary>Peak level (dBFS) a trigger must reach to duck. -60 to 0.</summary>
    public double ThresholdDb { get; set; } = -30;

    public void Normalize()
    {
        TargetProcessName = ProcessNames.Normalize(TargetProcessName);
        if (string.IsNullOrWhiteSpace(TargetProcessName))
            TargetProcessName = "Spotify";

        TriggerProcesses = TriggerProcesses
            .Select(ProcessNames.Normalize)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        DuckFactor = Math.Clamp(DuckFactor, 0.05, 1.00);
        AttackMilliseconds = Math.Clamp(AttackMilliseconds, 0, 5000);
        HoldMilliseconds = Math.Clamp(HoldMilliseconds, 0, 10000);
        ReleaseMilliseconds = Math.Clamp(ReleaseMilliseconds, 0, 10000);

        if (!string.Equals(TriggerMode, "Level", StringComparison.OrdinalIgnoreCase))
            TriggerMode = "Activity";
        ThresholdDb = Math.Clamp(ThresholdDb, -60, 0);
    }
}

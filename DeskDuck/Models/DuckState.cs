namespace DeskDuck.Models;

/// <summary>
/// Duck engine states. HOLDING waits after triggers go inactive;
/// DUCKING/RELEASING are the smooth volume transitions.
/// </summary>
public enum DuckState
{
    Normal,
    Ducking,
    Ducked,
    Holding,
    Releasing,
    Disabled
}

namespace PowerTray.Models;

/// <summary>
/// The active power plan and standby timeout as observed from Windows.
/// </summary>
public sealed record PowerSettingsSnapshot(
    Guid ActiveScheme,
    PowerSource PowerSource,
    int SleepTimeoutSeconds);

/// <summary>
/// Identifies which power source's standby setting is active.
/// </summary>
public enum PowerSource
{
    /// <summary>Wall power is active.</summary>
    Ac,

    /// <summary>Battery power is active.</summary>
    Battery
}

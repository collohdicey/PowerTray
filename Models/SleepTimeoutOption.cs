namespace PowerTray.Models;

/// <summary>
/// Describes one sleep timeout that can be selected from the tray menu.
/// </summary>
public sealed record SleepTimeoutOption(int Minutes, string Label)
{
    /// <summary>
    /// Gets the standard timeout choices offered by PowerTray.
    /// </summary>
    public static IReadOnlyList<SleepTimeoutOption> Presets { get; } =
    [
        new(1, "1 minute"),
        new(2, "2 minutes"),
        new(3, "3 minutes"),
        new(5, "5 minutes"),
        new(10, "10 minutes"),
        new(15, "15 minutes"),
        new(20, "20 minutes"),
        new(30, "30 minutes"),
        new(45, "45 minutes"),
        new(60, "1 hour"),
        new(120, "2 hours"),
        new(180, "3 hours"),
        new(240, "4 hours"),
        new(300, "5 hours"),
        new(0, "Never")
    ];

    /// <summary>
    /// Formats a Windows standby timeout for a compact user-facing label.
    /// </summary>
    public static string FormatMinutes(int minutes)
    {
        if (minutes <= 0)
        {
            return "Never";
        }

        if (minutes < 60)
        {
            return minutes == 1 ? "1 minute" : $"{minutes} minutes";
        }

        int hours = minutes / 60;
        int remainingMinutes = minutes % 60;
        string hoursText = hours == 1 ? "1 hour" : $"{hours} hours";

        if (remainingMinutes == 0)
        {
            return hoursText;
        }

        string minutesText = remainingMinutes == 1 ? "1 minute" : $"{remainingMinutes} minutes";
        return $"{hoursText}, {minutesText}";
    }

    /// <summary>
    /// Formats the seconds returned by the native Windows standby APIs.
    /// </summary>
    public static string FormatSeconds(int seconds)
    {
        if (seconds <= 0)
        {
            return "Never";
        }

        int minutes = seconds / 60;
        int remainingSeconds = seconds % 60;
        if (remainingSeconds == 0)
        {
            return FormatMinutes(minutes);
        }

        string secondsText = remainingSeconds == 1 ? "1 second" : $"{remainingSeconds} seconds";
        return minutes == 0
            ? secondsText
            : $"{FormatMinutes(minutes)}, {secondsText}";
    }
}

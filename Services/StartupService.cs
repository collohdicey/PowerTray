using System.Security;
using Microsoft.Win32;

namespace PowerTray.Services;

/// <summary>
/// Manages the current user's startup registration without requiring elevation.
/// </summary>
public sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PowerTray";

    /// <summary>
    /// Returns whether the current executable is registered to start at sign-in.
    /// </summary>
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            string? command = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(command) && PointsToCurrentExecutable(command);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new StartupServiceException("Windows did not allow PowerTray to read its startup registration.", exception);
        }
    }

    /// <summary>
    /// Enables or disables startup for the current user.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                    ?? throw new IOException("The Windows startup registry key could not be opened.");
                key.SetValue(ValueName, QuoteExecutablePath(Application.ExecutablePath), RegistryValueKind.String);
                return;
            }

            using RegistryKey? existingKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new StartupServiceException(
                enabled
                    ? "Windows did not allow PowerTray to register itself at sign-in."
                    : "Windows did not allow PowerTray to remove its startup registration.",
                exception);
        }
    }

    private static string QuoteExecutablePath(string executablePath) => $"\"{executablePath}\"";

    private static bool PointsToCurrentExecutable(string command)
    {
        string executablePart = command.Trim();
        if (executablePart.StartsWith('"'))
        {
            int closingQuote = executablePart.IndexOf('"', 1);
            if (closingQuote > 1)
            {
                executablePart = executablePart[1..closingQuote];
            }
        }
        else
        {
            int firstSpace = executablePart.IndexOf(' ');
            if (firstSpace > 0)
            {
                executablePart = executablePart[..firstSpace];
            }
        }

        try
        {
            string expected = Path.GetFullPath(Application.ExecutablePath);
            string actual = Path.GetFullPath(executablePart);
            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

/// <summary>
/// Indicates that Windows rejected a startup registration operation.
/// </summary>
public sealed class StartupServiceException : Exception
{
    public StartupServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

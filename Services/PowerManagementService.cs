using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PowerTray.Models;

namespace PowerTray.Services;

/// <summary>
/// Reads and changes the active Windows standby timeout using PowrProf.dll.
/// </summary>
public sealed class PowerManagementService : IDisposable
{
    private static readonly Guid SleepSubgroupGuid = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
    private static readonly Guid StandbyIdleSettingGuid = new("29F6C1DB-86DA-48C5-9FDB-F2B67B1F44DA");
    private readonly SynchronizationContext? _uiContext;
    private bool _disposed;

    public PowerManagementService()
    {
        _uiContext = SynchronizationContext.Current;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// Raised when Windows reports a power-source, resume, or power-plan change.
    /// </summary>
    public event EventHandler? PowerSettingsChanged;

    /// <summary>
    /// Reads the current active scheme and timeout off the UI thread.
    /// The native standby timeout is represented in seconds.
    /// </summary>
    public Task<PowerSettingsSnapshot> ReadCurrentSettingsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(ReadCurrentSettings, cancellationToken);

    /// <summary>
    /// Changes only the active source's standby timeout, then reads it back from Windows.
    /// </summary>
    public Task<PowerSettingsSnapshot> ApplySleepTimeoutAsync(
        int minutes,
        CancellationToken cancellationToken = default)
    {
        if (minutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minutes));
        }

        return Task.Run(
            () => ApplySleepTimeout(minutes),
            cancellationToken);
    }

    /// <summary>
    /// Requests the normal Windows suspend state without forcing hibernation or shutdown.
    /// </summary>
    public void RequestSleep()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        bool accepted;
        try
        {
            accepted = Application.SetSuspendState(PowerState.Suspend, force: false, disableWakeEvent: false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            throw new PowerManagementException(
                "Windows could not enter sleep on this device.",
                exception);
        }

        if (!accepted)
        {
            throw new PowerManagementException(
                "Windows did not accept the sleep request. Sleep may be unavailable or restricted by policy.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        PowerSettingsChanged = null;
    }

    private static PowerSettingsSnapshot ReadCurrentSettings()
    {
        Guid activeScheme = ReadActiveScheme();
        PowerSource powerSource = ReadPowerSource();
        int timeoutSeconds = ReadTimeoutSeconds(activeScheme, powerSource);
        return new PowerSettingsSnapshot(activeScheme, powerSource, timeoutSeconds);
    }

    private static PowerSettingsSnapshot ApplySleepTimeout(int minutes)
    {
        Guid activeScheme = ReadActiveScheme();
        PowerSource powerSource = ReadPowerSource();
        Guid subgroup = SleepSubgroupGuid;
        Guid setting = StandbyIdleSettingGuid;
        // PowerRead/PowerWriteValueIndex use seconds for GUID_STANDBYIDLE.
        // The public menu remains minute-based, so convert only at this boundary.
        uint timeout = checked((uint)minutes * 60U);

        uint result = powerSource == PowerSource.Ac
            ? NativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref activeScheme, ref subgroup, ref setting, timeout)
            : NativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref activeScheme, ref subgroup, ref setting, timeout);

        ThrowIfFailed(result, powerSource == PowerSource.Ac
            ? "Windows could not change the AC sleep timeout."
            : "Windows could not change the battery sleep timeout.");

        result = NativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref activeScheme);
        ThrowIfFailed(result, "Windows could not activate the updated power scheme.");

        return ReadCurrentSettings();
    }

    private static Guid ReadActiveScheme()
    {
        uint result = NativeMethods.PowerGetActiveScheme(IntPtr.Zero, out IntPtr schemePointer);
        ThrowIfFailed(result, "Windows could not identify the active power scheme.");

        if (schemePointer == IntPtr.Zero)
        {
            throw new PowerManagementException("Windows returned an empty active power scheme.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(schemePointer);
        }
        finally
        {
            NativeMethods.LocalFree(schemePointer);
        }
    }

    private static int ReadTimeoutSeconds(Guid activeScheme, PowerSource powerSource)
    {
        Guid subgroup = SleepSubgroupGuid;
        Guid setting = StandbyIdleSettingGuid;
        uint result = powerSource == PowerSource.Ac
            ? NativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref activeScheme, ref subgroup, ref setting, out uint timeout)
            : NativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref activeScheme, ref subgroup, ref setting, out timeout);

        ThrowIfFailed(result, "Windows could not read the current sleep timeout.");
        if (timeout > int.MaxValue)
        {
            throw new PowerManagementException("Windows returned an unsupported sleep timeout value.");
        }

        return (int)timeout;
    }

    private static PowerSource ReadPowerSource()
    {
        if (!NativeMethods.GetSystemPowerStatus(out SystemPowerStatus status))
        {
            int error = Marshal.GetLastWin32Error();
            throw new PowerManagementException(
                "Windows could not determine whether the computer is using AC or battery power.",
                error);
        }

        // Desktop PCs and systems that report an unknown AC state should use the AC setting.
        return status.ACLineStatus == 0 ? PowerSource.Battery : PowerSource.Ac;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e) => NotifySettingsChanged();

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Power)
        {
            NotifySettingsChanged();
        }
    }

    private void NotifySettingsChanged()
    {
        if (_disposed)
        {
            return;
        }

        void RaiseChanged(object? state)
        {
            if (!_disposed)
            {
                PowerSettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if (_uiContext is null)
        {
            RaiseChanged(null);
        }
        else
        {
            _uiContext.Post(RaiseChanged, null);
        }
    }

    private static void ThrowIfFailed(uint result, string message)
    {
        if (result != 0)
        {
            throw new PowerManagementException(message, result);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        internal byte ACLineStatus;
        internal byte BatteryFlag;
        internal byte BatteryLifePercent;
        internal byte SystemStatusFlag;
        internal int BatteryLifeTime;
        internal int BatteryFullLifeTime;
    }

    private static class NativeMethods
    {
        [DllImport("PowrProf.dll", ExactSpelling = true)]
        internal static extern uint PowerGetActiveScheme(
            IntPtr userRootPowerKey,
            out IntPtr activePolicyGuid);

        [DllImport("PowrProf.dll", ExactSpelling = true)]
        internal static extern uint PowerReadACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid settingGuid,
            out uint acValueIndex);

        [DllImport("PowrProf.dll", ExactSpelling = true)]
        internal static extern uint PowerReadDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid settingGuid,
            out uint dcValueIndex);

        [DllImport("PowrProf.dll", ExactSpelling = true)]
        internal static extern uint PowerWriteACValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid settingGuid,
            uint acValueIndex);

        [DllImport("PowrProf.dll", ExactSpelling = true)]
        internal static extern uint PowerWriteDCValueIndex(
            IntPtr rootPowerKey,
            ref Guid schemeGuid,
            ref Guid subgroupGuid,
            ref Guid settingGuid,
            uint dcValueIndex);

        [DllImport("PowrProf.dll", ExactSpelling = true)]
        internal static extern uint PowerSetActiveScheme(
            IntPtr userRootPowerKey,
            ref Guid schemeGuid);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr LocalFree(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }
}

/// <summary>
/// Describes a failure returned by Windows power-management APIs.
/// </summary>
public sealed class PowerManagementException : Exception
{
    public PowerManagementException(string message)
        : base(message)
    {
    }

    public PowerManagementException(string message, uint errorCode)
        : base($"{message} (Windows error {errorCode}.)")
    {
        ErrorCode = errorCode;
    }

    public PowerManagementException(string message, int errorCode)
        : base($"{message} (Windows error {errorCode}.)")
    {
        ErrorCode = unchecked((uint)errorCode);
    }

    public PowerManagementException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public uint? ErrorCode { get; }
}

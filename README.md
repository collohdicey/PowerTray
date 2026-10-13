# PowerTray

PowerTray is a lightweight Windows 10/11 notification-area application for changing the active Windows sleep timeout, requesting sleep, and opening the Windows power settings page. It has no main window and runs as a tray-only `WinExe`.

## Prerequisites

- Windows 10 version 1809 or later, or Windows 11.
- Visual Studio 2022 with the **.NET desktop development** workload and the .NET 9 SDK. The project can be retargeted to a newer installed .NET Windows Desktop SDK when desired.
- x64 Windows for the configured x64 build and publish targets.

## Open and build in Visual Studio

1. Open `PowerTray.sln` in Visual Studio.
2. Select the `x64` platform and either `Debug` or `Release` from the toolbar.
3. Build with **Build > Build Solution** (`Ctrl+Shift+B`).
4. Start debugging with **Debug > Start Without Debugging** or `F5`. PowerTray appears only in the notification area.

The equivalent command-line Debug build from the solution directory is:

```powershell
dotnet build PowerTray.sln --configuration Debug --no-restore
```

If the SDK has not restored the project yet, omit `--no-restore`.

## Publish a self-contained single-file executable

The project is configured for `win-x64`, self-contained, single-file publishing. From the solution directory:

```powershell
dotnet publish PowerTray.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  --output .\publish\win-x64
```

The resulting `publish\win-x64\PowerTray.exe` runs without a separately installed .NET runtime. The executable icon and notification-area icon come from the embedded `Resources\powertray.ico` asset; no runtime image files are required.

## How sleep timeout selection works

PowerTray reads the active Windows power scheme with `PowerGetActiveScheme`, detects the current power source, and reads the corresponding standby idle value with `PowerReadACValueIndex` or `PowerReadDCValueIndex`. The timeout menu is refreshed each time it opens, so changes made in Windows Settings are reflected.

The menu choices are represented in minutes. At the native PowrProf boundary, Windows stores the standby idle value in seconds, so PowerTray converts selected minutes to seconds before writing and converts the result back for display. `Never` is written as `0`. If Windows contains a value outside the standard list, PowerTray adds a disabled `Current: ...` item and leaves it unchanged until the user selects a new value.

When a selection is applied, only the AC or battery value for the currently active source is modified. The other source remains untouched. PowerTray reads the value back from Windows after the write so the check mark represents the actual setting rather than application memory.

## Light and dark mode

The tray menu reads Windows' `AppsUseLightTheme` setting and applies a matching light or dark palette. The palette is checked again whenever the menu opens, so changing the Windows theme takes effect without restarting PowerTray. High-contrast mode uses the system light renderer for maximum accessibility.

## Start with Windows

`Start with Windows` is off by default. When enabled, PowerTray writes one quoted executable path to the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key. It does not require administrator access. Disabling the option removes the `PowerTray` value. The path is compared with the running executable so an old or different path is not reported as enabled.

## Architecture

- `Program.cs` configures WinForms, owns the single-instance mutex, and starts the application context.
- `TrayApplicationContext.cs` owns the `NotifyIcon`, menu, click handlers, refresh flow, and graceful disposal.
- `Services\PowerManagementService.cs` contains the native PowrProf and sleep APIs.
- `Services\StartupService.cs` contains per-user startup registration.
- `Models\SleepTimeoutOption.cs` contains timeout presets and formatting.
- `Helpers\SingleInstanceManager.cs` prevents duplicate processes.
- `Helpers\ShellMessageWindow.cs` restores the icon if Windows Explorer restarts.

## Known Windows power-management limitations

- Enterprise policy, OEM firmware, Modern Standby, or a device with sleep disabled can reject reads, writes, or the sleep request. PowerTray reports those failures and does not elevate itself.
- A desktop without a battery uses the AC timeout. An unknown AC-line report is treated as AC because there is no reliable battery timeout to apply in that state.
- Windows stores separate AC and battery standby values. Changing one does not intentionally change the other.
- The timeout is a Windows power-plan setting, so switching plans can change the value shown by PowerTray.
- `Sleep Now` requests normal suspend with wake events enabled. Windows, firmware, or policy remains responsible for deciding whether suspend is available.
- Opening `ms-settings:powersleep` depends on the Windows Settings URI being registered. If it cannot be launched, PowerTray shows an error instead of attempting an unrelated fallback.

Native power behavior, tray rendering, and startup registration should be exercised on representative Windows 10 and Windows 11 AC-only, AC-connected laptop, battery, managed-policy, and Modern Standby devices. A successful build validates compilation and packaging; it cannot emulate those device and policy combinations.

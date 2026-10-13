using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using PowerTray.Helpers;
using PowerTray.Models;
using PowerTray.Services;

namespace PowerTray;

/// <summary>
/// Owns the tray icon, its menu, and the lifetime of the tray-only application.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private const string IconResourceName = "PowerTray.Resources.powertray.ico";
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _sleepTimeoutMenuItem;
    private readonly ToolStripMenuItem _sleepNowMenuItem;
    private readonly ToolStripMenuItem _settingsMenuItem;
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly ToolStripMenuItem _currentTimeoutMenuItem;
    private readonly ToolStripSeparator _currentTimeoutSeparator;
    private readonly ToolStripMenuItem[] _timeoutMenuItems;
    private readonly PowerManagementService _powerManagementService;
    private readonly StartupService _startupService;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly SynchronizationContext _uiContext;
    private readonly object _refreshLock = new();
    private readonly Icon _trayIcon;
    private readonly ShellMessageWindow _shellMessageWindow;
    private PowerTrayMenuRenderer? _menuRenderer;
    private PowerTrayMenuRenderer? _timeoutMenuRenderer;
    private Task? _refreshTask;
    private bool _applyingTimeout;
    private bool _disposed;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(_uiContext);

        _powerManagementService = new PowerManagementService();
        _startupService = new StartupService();
        _trayIcon = LoadTrayIcon();

        _contextMenu = new ContextMenuStrip
        {
            Font = SystemFonts.MenuFont,
            AutoClose = true,
            ShowImageMargin = true,
            ShowCheckMargin = true,
            AutoSize = true,
            Padding = new Padding(2)
        };

        ToolStripMenuItem headerMenuItem = new("PowerTray")
        {
            Enabled = false,
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
            Image = _trayIcon.ToBitmap(),
            AccessibleName = "PowerTray"
        };

        _sleepTimeoutMenuItem = new ToolStripMenuItem("Sleep Timeout")
        {
            AccessibleName = "Sleep Timeout submenu"
        };
        _currentTimeoutMenuItem = new ToolStripMenuItem
        {
            Enabled = false,
            Visible = false,
            Checked = true
        };
        _currentTimeoutSeparator = new ToolStripSeparator { Visible = false };
        _sleepTimeoutMenuItem.DropDownItems.Add(_currentTimeoutMenuItem);
        _sleepTimeoutMenuItem.DropDownItems.Add(_currentTimeoutSeparator);

        _timeoutMenuItems = SleepTimeoutOption.Presets
            .Select(CreateTimeoutMenuItem)
            .ToArray();
        foreach (ToolStripMenuItem timeoutMenuItem in _timeoutMenuItems)
        {
            timeoutMenuItem.Click += OnTimeoutMenuItemClick;
        }
        _sleepTimeoutMenuItem.DropDownItems.AddRange(_timeoutMenuItems);
        _sleepTimeoutMenuItem.DropDownOpening += OnSleepTimeoutMenuOpening;

        _sleepNowMenuItem = new ToolStripMenuItem("Sleep Now")
        {
            Image = _trayIcon.ToBitmap(),
            AccessibleName = "Sleep Now"
        };
        _sleepNowMenuItem.Click += OnSleepNowClick;

        _settingsMenuItem = new ToolStripMenuItem("Power & Sleep Settings")
        {
            AccessibleName = "Power and Sleep Settings"
        };
        _settingsMenuItem.Click += OnSettingsClick;

        _startupMenuItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            AccessibleName = "Start with Windows"
        };
        _startupMenuItem.Click += OnStartupClick;

        ToolStripMenuItem exitMenuItem = new("Exit PowerTray")
        {
            AccessibleName = "Exit PowerTray"
        };
        exitMenuItem.Click += (_, _) => ExitThread();

        _contextMenu.Items.Add(headerMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_sleepTimeoutMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_sleepNowMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(_settingsMenuItem);
        _contextMenu.Items.Add(_startupMenuItem);
        _contextMenu.Items.Add(new ToolStripSeparator());
        _contextMenu.Items.Add(exitMenuItem);
        _contextMenu.Opening += OnContextMenuOpening;
        ApplyMenuTheme(force: true);

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "PowerTray — Quick Power Controls",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;

        _shellMessageWindow = new ShellMessageWindow(RestoreTrayIcon);
        _powerManagementService.PowerSettingsChanged += OnPowerSettingsChanged;

        try
        {
            _startupMenuItem.Checked = _startupService.IsEnabled();
        }
        catch (StartupServiceException exception)
        {
            Debug.WriteLine(exception);
        }

        _ = RefreshSleepTimeoutMenuAsync(showError: false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _lifetimeCancellation.Cancel();
            _powerManagementService.PowerSettingsChanged -= OnPowerSettingsChanged;
            _shellMessageWindow.Dispose();
            _notifyIcon.MouseClick -= OnNotifyIconMouseClick;
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
            _powerManagementService.Dispose();
            _trayIcon.Dispose();
            _lifetimeCancellation.Dispose();
        }

        base.Dispose(disposing);
    }

    private static ToolStripMenuItem CreateTimeoutMenuItem(SleepTimeoutOption option)
    {
        return new ToolStripMenuItem(option.Label)
        {
            Tag = option,
            CheckOnClick = false,
            AccessibleName = $"Set sleep timeout to {option.Label}"
        };
    }

    private static Icon LoadTrayIcon()
    {
        Assembly assembly = typeof(TrayApplicationContext).Assembly;
        using Stream? iconStream = assembly.GetManifestResourceStream(IconResourceName);
        if (iconStream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using Icon resourceIcon = new(iconStream);
        return (Icon)resourceIcon.Clone();
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _disposed || _contextMenu.Visible)
        {
            return;
        }

        _contextMenu.Show(Cursor.Position);
        _contextMenu.Focus();
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_disposed)
        {
            e.Cancel = true;
            return;
        }

        ApplyMenuTheme();

        try
        {
            _startupMenuItem.Checked = _startupService.IsEnabled();
        }
        catch (StartupServiceException exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private async void OnSleepTimeoutMenuOpening(object? sender, EventArgs e)
    {
        await RefreshSleepTimeoutMenuAsync(showError: true);
    }

    private async void OnTimeoutMenuItemClick(object? sender, EventArgs e)
    {
        if (_applyingTimeout || sender is not ToolStripMenuItem menuItem || menuItem.Tag is not SleepTimeoutOption option)
        {
            return;
        }

        _applyingTimeout = true;
        SetTimeoutItemsEnabled(false);
        try
        {
            PowerSettingsSnapshot snapshot = await _powerManagementService.ApplySleepTimeoutAsync(
                option.Minutes,
                _lifetimeCancellation.Token);
            ApplySettingsSnapshot(snapshot);
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (PowerManagementException exception)
        {
            ShowError(exception.Message);
            await RefreshSleepTimeoutMenuAsync(showError: false);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            ShowError("PowerTray could not change the sleep timeout. Please try again.");
        }
        finally
        {
            _applyingTimeout = false;
            SetTimeoutItemsEnabled(true);
        }
    }

    private void OnSleepNowClick(object? sender, EventArgs e)
    {
        try
        {
            _powerManagementService.RequestSleep();
        }
        catch (PowerManagementException exception)
        {
            ShowError(exception.Message);
        }
    }

    private void OnSettingsClick(object? sender, EventArgs e)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:powersleep",
                UseShellExecute = true
            });

            if (process is null)
            {
                throw new InvalidOperationException("Windows did not return a settings process.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or FileNotFoundException)
        {
            ShowError("PowerTray could not open Windows Power & Sleep settings. You can open Settings manually.");
            Debug.WriteLine(exception);
        }
    }

    private void OnStartupClick(object? sender, EventArgs e)
    {
        bool requestedState = _startupMenuItem.Checked;
        try
        {
            _startupService.SetEnabled(requestedState);
            _startupMenuItem.Checked = _startupService.IsEnabled();
        }
        catch (StartupServiceException exception)
        {
            _startupMenuItem.Checked = !requestedState;
            ShowError(exception.Message);
        }
    }

    private void OnPowerSettingsChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _uiContext.Post(_ => _ = RefreshSleepTimeoutMenuAsync(showError: false), null);
    }

    private Task RefreshSleepTimeoutMenuAsync(bool showError)
    {
        lock (_refreshLock)
        {
            if (_refreshTask is { IsCompleted: false })
            {
                return _refreshTask;
            }

            _refreshTask = RefreshSleepTimeoutMenuCoreAsync(showError);
            return _refreshTask;
        }
    }

    private async Task RefreshSleepTimeoutMenuCoreAsync(bool showError)
    {
        try
        {
            PowerSettingsSnapshot snapshot = await _powerManagementService.ReadCurrentSettingsAsync(
                _lifetimeCancellation.Token);
            if (!_disposed)
            {
                ApplySettingsSnapshot(snapshot);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (PowerManagementException exception)
        {
            if (showError)
            {
                ShowError(exception.Message);
            }
            else
            {
                Debug.WriteLine(exception);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            if (showError)
            {
                ShowError("PowerTray could not read the current Windows sleep timeout.");
            }
        }
    }

    private void ApplySettingsSnapshot(PowerSettingsSnapshot snapshot)
    {
        bool isPreset = false;
        foreach (ToolStripMenuItem menuItem in _timeoutMenuItems)
        {
            SleepTimeoutOption option = (SleepTimeoutOption)menuItem.Tag!;
            menuItem.Checked = checked(option.Minutes * 60) == snapshot.SleepTimeoutSeconds;
            isPreset |= menuItem.Checked;
        }

        _currentTimeoutMenuItem.Visible = !isPreset;
        _currentTimeoutSeparator.Visible = !isPreset;
        _currentTimeoutMenuItem.Text = $"Current: {SleepTimeoutOption.FormatSeconds(snapshot.SleepTimeoutSeconds)}";
        _currentTimeoutMenuItem.Checked = !isPreset;

        string source = snapshot.PowerSource == PowerSource.Ac ? "AC" : "battery";
        SetTooltip($"PowerTray — Quick Power Controls • Sleep: {SleepTimeoutOption.FormatSeconds(snapshot.SleepTimeoutSeconds)} ({source})");
    }

    private void ApplyMenuTheme(bool force = false)
    {
        bool isDark = WindowsTheme.IsDarkMode();
        if (!force && _contextMenu.Renderer is PowerTrayMenuRenderer currentRenderer && currentRenderer.IsDark == isDark)
        {
            return;
        }

        _menuRenderer = new PowerTrayMenuRenderer(isDark);
        _timeoutMenuRenderer = new PowerTrayMenuRenderer(isDark);

        _contextMenu.Renderer = _menuRenderer;
        _contextMenu.BackColor = _menuRenderer.BackgroundColor;
        _contextMenu.ForeColor = _menuRenderer.TextColor;

        ToolStripDropDown timeoutDropDown = _sleepTimeoutMenuItem.DropDown;
        timeoutDropDown.Renderer = _timeoutMenuRenderer;
        timeoutDropDown.BackColor = _timeoutMenuRenderer.BackgroundColor;
        timeoutDropDown.ForeColor = _timeoutMenuRenderer.TextColor;

    }

    private void SetTimeoutItemsEnabled(bool enabled)
    {
        foreach (ToolStripMenuItem menuItem in _timeoutMenuItems)
        {
            menuItem.Enabled = enabled;
        }
    }

    private void SetTooltip(string tooltip)
    {
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..63];
    }

    private void RestoreTrayIcon()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Visible = true;
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine(exception);
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "PowerTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

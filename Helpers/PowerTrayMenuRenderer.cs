using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace PowerTray.Helpers;

/// <summary>
/// Detects the Windows app theme without depending on undocumented dark-mode APIs.
/// </summary>
internal static class WindowsTheme
{
    private const string PersonalizationKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool IsDarkMode()
    {
        if (SystemInformation.HighContrast)
        {
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizationKey, writable: false);
            if (key?.GetValue("AppsUseLightTheme") is int appLightTheme)
            {
                return appLightTheme == 0;
            }

            return key?.GetValue("SystemUsesLightTheme") is int systemLightTheme && systemLightTheme == 0;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            Debug.WriteLine(exception);
            return false;
        }
    }
}

/// <summary>
/// Provides stable light and dark palettes for the tray menu.
/// </summary>
internal sealed class PowerTrayMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly bool _isDark;

    public PowerTrayMenuRenderer(bool isDark)
        : base(new PowerTrayColorTable(isDark))
    {
        _isDark = isDark;
    }

    public bool IsDark => _isDark;

    public Color BackgroundColor => _isDark
        ? Color.FromArgb(32, 32, 32)
        : Color.FromArgb(255, 255, 255);

    public Color TextColor => _isDark
        ? Color.FromArgb(242, 242, 242)
        : Color.FromArgb(32, 32, 32);
}

internal sealed class PowerTrayColorTable : ProfessionalColorTable
{
    private readonly bool _isDark;

    public PowerTrayColorTable(bool isDark)
        : base()
    {
        _isDark = isDark;
    }

    private Color Background => _isDark
        ? Color.FromArgb(32, 32, 32)
        : Color.FromArgb(255, 255, 255);

    private Color Border => _isDark
        ? Color.FromArgb(72, 72, 72)
        : Color.FromArgb(210, 210, 210);

    private Color Selected => _isDark
        ? Color.FromArgb(55, 105, 165)
        : Color.FromArgb(0, 120, 212);

    private Color Separator => _isDark
        ? Color.FromArgb(68, 68, 68)
        : Color.FromArgb(220, 220, 220);

    public override Color MenuBorder => Border;
    public override Color MenuItemBorder => Selected;
    public override Color MenuItemSelected => Selected;
    public override Color MenuItemSelectedGradientBegin => Selected;
    public override Color MenuItemSelectedGradientEnd => Selected;
    public override Color MenuItemPressedGradientBegin => Selected;
    public override Color MenuItemPressedGradientMiddle => Selected;
    public override Color MenuItemPressedGradientEnd => Selected;
    public override Color MenuStripGradientBegin => Background;
    public override Color MenuStripGradientEnd => Background;
    public override Color ToolStripDropDownBackground => Background;
    public override Color ImageMarginGradientBegin => Background;
    public override Color ImageMarginGradientMiddle => Background;
    public override Color ImageMarginGradientEnd => Background;
    public override Color SeparatorDark => Separator;
    public override Color SeparatorLight => Separator;
}

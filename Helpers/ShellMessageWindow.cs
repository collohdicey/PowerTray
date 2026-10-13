using System.Runtime.InteropServices;

namespace PowerTray.Helpers;

/// <summary>
/// Message-only window used to restore the tray icon after Explorer restarts.
/// </summary>
internal sealed class ShellMessageWindow : NativeWindow, IDisposable
{
    private const int HwndMessage = -3;
    private readonly int _taskbarCreatedMessage;
    private readonly Action _taskbarCreatedCallback;
    private bool _disposed;

    public ShellMessageWindow(Action taskbarCreatedCallback)
    {
        _taskbarCreatedCallback = taskbarCreatedCallback;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        CreateParams parameters = new()
        {
            Parent = new IntPtr(HwndMessage),
            Caption = "PowerTray.MessageWindow"
        };
        CreateHandle(parameters);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == _taskbarCreatedMessage)
        {
            _taskbarCreatedCallback();
            return;
        }

        base.WndProc(ref message);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string lpString);
}

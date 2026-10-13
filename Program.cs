using System.Diagnostics;
using PowerTray.Helpers;

namespace PowerTray;

internal static class Program
{
    /// <summary>
    /// Starts PowerTray without creating a main window.
    /// </summary>
    [STAThread]
    private static void Main()
    {
        using SingleInstanceManager? singleInstance = SingleInstanceManager.TryAcquire();
        if (singleInstance is null)
        {
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            using TrayApplicationContext context = new();
            Application.Run(context);
        }
        catch (Exception exception)
        {
            ShowFatalError(exception);
        }
        finally
        {
            Application.ThreadException -= OnThreadException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        }
    }

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e) => ShowFatalError(e.Exception);

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            ShowFatalError(exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Debug.WriteLine(e.Exception);
    }

    private static void ShowFatalError(Exception exception)
    {
        string message = $"PowerTray encountered an unexpected error and may need to close.\r\n\r\n{exception.Message}";
        try
        {
            MessageBox.Show(message, "PowerTray", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception displayException)
        {
            Debug.WriteLine(displayException);
        }
    }
}

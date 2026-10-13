namespace PowerTray.Helpers;

/// <summary>
/// Owns the per-session mutex that prevents duplicate PowerTray processes.
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private const string MutexName = @"Local\PowerTray.SingleInstance.7C2A6B6B";
    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstanceManager(Mutex mutex)
    {
        _mutex = mutex;
    }

    /// <summary>
    /// Attempts to acquire the application's single-instance mutex.
    /// </summary>
    public static SingleInstanceManager? TryAcquire()
    {
        Mutex mutex = new(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstanceManager(mutex);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

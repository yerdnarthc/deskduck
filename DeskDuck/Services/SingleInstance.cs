namespace DeskDuck.Services;

/// <summary>
/// Single-instance enforcement: a named session-local mutex guarantees one
/// DeskDuck owns the audio sessions, and a named event lets later launches
/// ask the owner to show its window before exiting. Duplicates would fight
/// over the same session volumes (last-writer-wins restores), so this runs
/// before anything else is created. A crashed previous owner surfaces as an
/// abandoned mutex, which simply means ownership is ours now.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    // Local\ = per Windows session (fast-user-switching friendly).
    private const string MutexName = @"Local\DeskDuck.SingleInstance.v1";
    private const string ShowEventName = @"Local\DeskDuck.ShowFirstInstance.v1";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly Thread _listener;
    private volatile bool _stop;
    private bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle showEvent, Action onShowRequested)
    {
        _mutex = mutex;
        _showEvent = showEvent;
        _listener = new Thread(() =>
        {
            while (!_stop)
            {
                // Timeout keeps the loop responsive to disposal; the thread is
                // background anyway, so it can never hang process shutdown.
                if (_showEvent.WaitOne(TimeSpan.FromSeconds(1)))
                    onShowRequested();
            }
        })
        {
            IsBackground = true,
            Name = "DeskDuck single-instance listener",
        };
        _listener.Start();
    }

    /// <summary>
    /// Returns the guard when this process is the first instance, else signals
    /// the owner to show itself and returns null (caller must exit promptly).
    /// </summary>
    public static SingleInstance? TryAcquire(Action onShowRequested)
    {
        Mutex mutex = new(initiallyOwned: false, MutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalOwner();
            mutex.Dispose();
            return null;
        }
        try
        {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Previous owner died holding it: ownership is ours now.
        }
        EventWaitHandle showEvent = new(initialState: false, EventResetMode.AutoReset, ShowEventName);
        return new SingleInstance(mutex, showEvent, onShowRequested);
    }

    private static void SignalOwner()
    {
        try
        {
            // The owner may still be starting up (event not created yet):
            // one retry before giving up — it will appear on its own anyway.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using EventWaitHandle showEvent = EventWaitHandle.OpenExisting(ShowEventName);
                    showEvent.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException) when (attempt == 0)
                {
                    Thread.Sleep(500);
                }
            }
        }
        catch
        {
            // Showing is a courtesy; exiting is the requirement.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stop = true;
        _showEvent.Dispose();
        try { _mutex.ReleaseMutex(); } catch { }
        _mutex.Dispose();
    }
}

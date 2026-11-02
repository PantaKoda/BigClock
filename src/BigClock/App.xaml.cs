using System.Runtime.InteropServices;
using System.Windows;

namespace BigClock;

public partial class App : Application
{
    public const string UpdatedArgument = "--updated";

    private const string MutexName = @"Local\BigClock.SingleInstance";
    private const string ShowEventName = @"Local\BigClock.Show";
    private const int AnyProcess = -1;

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // After an update the previous version may still be exiting, so wait for it.
        var justUpdated = e.Args.Contains(UpdatedArgument);
        _instanceMutex = new Mutex(false, MutexName);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!TryAcquireInstance(_instanceMutex, justUpdated ? TimeSpan.FromSeconds(15) : TimeSpan.Zero))
        {
            AllowSetForegroundWindow(AnyProcess);
            _showEvent.Set();
            Shutdown();
            return;
        }

        if (Environment.ProcessPath is { } exePath)
            Updater.DeleteLeftovers(exePath);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.BeginInvoke(window.BringToFront),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private static bool TryAcquireInstance(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true;
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}

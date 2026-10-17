using CustomTopBar.Interop;

namespace CustomTopBar.Core;

/// <summary>
/// Stellt sicher, dass pro Benutzersitzung nur eine Instanz läuft. Ein zweiter Start signalisiert
/// der laufenden Instanz über ein benanntes Event, ihr Einstellungsfenster zu öffnen.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CustomTopBar.SingleInstance.7C1F3E2A";
    private const string ActivateEventName = @"Local\CustomTopBar.Activate.7C1F3E2A";
    private const string ExitEventName = @"Local\CustomTopBar.Exit.7C1F3E2A";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registration;
    private EventWaitHandle? _exitEvent;
    private RegisteredWaitHandle? _exitRegistration;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: false, MutexName);
        try
        {
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            // Vorherige Instanz ist abgestürzt – der Mutex gehört jetzt uns.
            _ownsMutex = true;
        }
        return _ownsMutex;
    }

    public static void SignalExistingInstance()
    {
        // Der zweite Prozess wurde vom Benutzer gestartet und darf den Vordergrund weitergeben.
        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
        Signal(ActivateEventName);
    }

    /// <summary>"CustomTopBar.exe --exit": laufende Instanz sauber beenden (z. B. für Skripte oder Updates).</summary>
    public static bool SignalExit() => Signal(ExitEventName);

    public void ListenForActivation(Action onActivate, Action onExit)
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
            (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        _exitRegistration = ThreadPool.RegisterWaitForSingleObject(_exitEvent,
            (_, _) => onExit(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    private static bool Signal(string name)
    {
        if (!EventWaitHandle.TryOpenExisting(name, out var handle)) return false;
        using (handle) handle.Set();
        return true;
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activateEvent?.Dispose();
        _exitRegistration?.Unregister(null);
        _exitEvent?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Falscher Thread – Handle-Freigabe genügt.
            }
        }
        _mutex?.Dispose();
    }
}

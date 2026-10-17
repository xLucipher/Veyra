using System.Runtime.InteropServices;
using Veyra.Core;
using Veyra.Interop;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Bar;

/// <summary>
/// Windows-AppBar-Registrierung (SHAppBarMessage).
///
/// Die Leiste wird IMMER registriert, damit Explorer ihr ABN_FULLSCREENAPP-Benachrichtigungen schickt
/// (zusätzlicher, ereignisbasierter Auslöser für die Vollbild-Erkennung). Ohne ABM_SETPOS wird dabei
/// kein Bildschirmbereich reserviert. Nur im Modus "ReserveScreenSpace" wird per ABM_SETPOS der obere
/// Bereich reserviert, sodass maximierte Fenster unterhalb der Leiste beginnen.
/// </summary>
internal sealed class AppBarRegistration
{
    private readonly IntPtr _hwnd;
    private RECT _reservedRect;
    private bool _hasReservation;

    public AppBarRegistration(IntPtr hwnd)
    {
        _hwnd = hwnd;
        CallbackMessage = RegisterWindowMessage("Veyra.AppBarCallback");
    }

    public uint CallbackMessage { get; }

    public bool IsRegistered { get; private set; }

    public bool IsReserving => IsRegistered && _hasReservation;

    public void Register()
    {
        if (IsRegistered) return;
        var data = CreateData();
        data.uCallbackMessage = CallbackMessage;
        IsRegistered = SHAppBarMessage(ABM_NEW, ref data) != UIntPtr.Zero;
        _hasReservation = false;
        if (!IsRegistered) Log.Warning("AppBar", "ABM_NEW fehlgeschlagen");
    }

    /// <summary>Kann von jedem Thread aufgerufen werden (auch im Absturz-Handler).</summary>
    public void Unregister()
    {
        if (!IsRegistered) return;
        var data = CreateData();
        SHAppBarMessage(ABM_REMOVE, ref data);
        IsRegistered = false;
        _hasReservation = false;
    }

    /// <summary>Entfernt eine evtl. bestehende Reservierung und registriert neu (z. B. nach Explorer-Neustart).</summary>
    public void Reset()
    {
        Unregister();
        Register();
    }

    /// <summary>Reserviert den oberen Bereich und liefert das tatsächlich zugeteilte Rechteck.</summary>
    public RECT Reserve(RECT desired, bool force)
    {
        if (!IsRegistered) Register();
        if (!IsRegistered) return desired;

        var data = CreateData();
        data.uEdge = ABE_TOP;
        data.rc = desired;
        SHAppBarMessage(ABM_QUERYPOS, ref data);

        // QUERYPOS darf die Oberkante verschieben (andere AppBars), die Höhe bleibt unsere.
        data.rc.Bottom = data.rc.Top + desired.Height;

        if (!force && _hasReservation && data.rc.Equals(_reservedRect)) return _reservedRect;

        SHAppBarMessage(ABM_SETPOS, ref data);
        data.rc.Bottom = data.rc.Top + desired.Height;
        _reservedRect = data.rc;
        _hasReservation = true;
        Log.Info("AppBar", $"Bildschirmbereich reserviert: {_reservedRect}");
        return _reservedRect;
    }

    public void NotifyActivate()
    {
        if (!IsRegistered) return;
        var data = CreateData();
        SHAppBarMessage(ABM_ACTIVATE, ref data);
    }

    public void NotifyWindowPosChanged()
    {
        if (!IsRegistered) return;
        var data = CreateData();
        SHAppBarMessage(ABM_WINDOWPOSCHANGED, ref data);
    }

    private APPBARDATA CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<APPBARDATA>(),
        hWnd = _hwnd,
    };
}

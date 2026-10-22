using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Veyra.Core;

namespace Veyra.Gaming;

public enum FpsAccess
{
    /// <summary>Mitglied, und die Anmeldung kennt die Gruppe schon – Messung möglich.</summary>
    Granted,

    /// <summary>Mitglied, aber erst nach dem nächsten Anmelden wirksam.</summary>
    NeedsSignIn,

    /// <summary>Nicht Mitglied.</summary>
    Missing,
}

/// <summary>
/// Berechtigung für die FPS-Messung: Mitgliedschaft in der Gruppe „Leistungsprotokollbenutzer“ (S-1-5-32-559, der
/// Name hängt von der Sprache ab). Aufnehmen braucht einmal Adminrechte: Veyra startet sich dafür selbst mit
/// <c>--grant-fps &lt;SID&gt;</c> über die Benutzerkontensteuerung. Die SID des angemeldeten Kontos wird mitgegeben,
/// weil die erhöhte Instanz unter einem anderen (Admin-)Konto laufen kann. Gruppen gelten erst ab der nächsten Anmeldung.
/// </summary>
public static class FpsPermission
{
    public const string GrantArgument = "--grant-fps";
    private static readonly SecurityIdentifier GroupSid = new("S-1-5-32-559");
    private const int ERROR_MEMBER_IN_ALIAS = 1378;
    private const int ERROR_CANCELLED = 1223;

    /// <summary>Lokaler Name der Gruppe, z. B. „Leistungsprotokollbenutzer“ bzw. „Performance Log Users“.</summary>
    public static string GroupName
    {
        get
        {
            try
            {
                string name = GroupSid.Translate(typeof(NTAccount)).Value;
                return name[(name.IndexOf('\\') + 1)..];
            }
            catch (IdentityNotMappedException)
            {
                return "Leistungsprotokollbenutzer";
            }
        }
    }

    public static FpsAccess Check()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.Groups?.Contains(GroupSid) == true) return FpsAccess.Granted;
        return IsMember(identity.Name) ? FpsAccess.NeedsSignIn : FpsAccess.Missing;
    }

    /// <summary>Mitgliedschaft laut Benutzerverwaltung (nicht laut Anmeldung) – erkennt „schon aufgenommen, noch nicht angemeldet“.</summary>
    private static bool IsMember(string account)
    {
        if (NetUserGetLocalGroups(null, account, 0, LG_INCLUDE_INDIRECT, out var buffer, MAX_PREFERRED_LENGTH, out uint read, out _) != 0)
            return false;
        try
        {
            string group = GroupName;
            for (int i = 0; i < read; i++)
            {
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buffer, i * IntPtr.Size));
                if (string.Equals(name, group, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
    }

    /// <summary>Nimmt das angemeldete Konto auf (UAC-Abfrage). Rückgabe: Hinweistext für die Oberfläche.</summary>
    public static async Task<string> RequestAsync()
    {
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? "";
        if (Environment.ProcessPath is not { } exe || sid.Length == 0) return "Veyra konnte das eigene Konto nicht ermitteln.";
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe, $"{GrantArgument} {sid}") { UseShellExecute = true, Verb = "runas" });
            if (process is null) return "Der Schritt mit Adminrechten konnte nicht gestartet werden.";
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Log.Warning("Fps", $"Aufnahme in „{GroupName}“ fehlgeschlagen (Code {process.ExitCode})");
                return $"Das Konto konnte nicht aufgenommen werden (Fehler {process.ExitCode}).";
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            return "Abgebrochen – die Windows-Abfrage wurde nicht bestätigt.";
        }
        Log.Info("Fps", $"Konto in „{GroupName}“ aufgenommen – wirksam nach dem nächsten Anmelden");
        return Check() == FpsAccess.Granted
            ? "Erledigt."
            : "Erledigt. Bitte einmal ab- und wieder anmelden – erst dann übernimmt Windows die Gruppe.";
    }

    /// <summary>Erhöhte Instanz (<c>Veyra.exe --grant-fps &lt;SID oder Kontoname&gt;</c>): Konto aufnehmen, Exitcode 0 = Erfolg.</summary>
    public static int GrantFromCommandLine(string? account)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(account)) return 87; // ERROR_INVALID_PARAMETER
            var sid = account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                ? new SecurityIdentifier(account)
                : (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var member = pinned.AddrOfPinnedObject();
                int result = NetLocalGroupAddMembers(null, GroupName, 0, ref member, 1);
                return result == ERROR_MEMBER_IN_ALIAS ? 0 : result;
            }
            finally
            {
                pinned.Free();
            }
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException or SystemException)
        {
            return 1332; // ERROR_NONE_MAPPED
        }
    }

    private const uint LG_INCLUDE_INDIRECT = 1;
    private const uint MAX_PREFERRED_LENGTH = uint.MaxValue;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetLocalGroups(string? server, string user, uint level, uint flags, out IntPtr buffer, uint maxLength,
        out uint entriesRead, out uint totalEntries);

    /// <summary>Stufe 0: LOCALGROUP_MEMBERS_INFO_0 = ein Zeiger auf die SID.</summary>
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetLocalGroupAddMembers(string? server, string group, uint level, ref IntPtr members, uint count);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);
}

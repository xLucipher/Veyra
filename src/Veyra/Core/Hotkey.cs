using System.Windows.Input;

namespace Veyra.Core;

/// <summary>
/// Solange im Einstellungsfenster ein Tastenkürzel eingegeben wird, sind alle globalen Kürzel abgemeldet –
/// sonst fängt Windows bereits vergebene Kombinationen ab, bevor das Eingabefeld sie sieht.
/// </summary>
public static class HotkeyCapture
{
    public static bool IsActive { get; private set; }

    public static event EventHandler? Changed;

    public static void Set(bool active)
    {
        if (IsActive == active) return;
        IsActive = active;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}

/// <summary>
/// Globales Tastenkürzel, z. B. Strg + Umschalt + I. Gespeichert als "Ctrl+Shift+I" (Namen der WPF-<see cref="Key"/>-Werte),
/// angezeigt auf Deutsch. Auch Einzeltasten ohne Zusatztaste sind erlaubt (z. B. "PageUp") – Windows fängt sie dann
/// allerdings in allen Programmen ab (<see cref="IsSingleKey"/>, <see cref="BlocksTyping"/>).
/// </summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    private static readonly (ModifierKeys Modifier, string Name, string Display)[] ModifierNames =
    [
        (ModifierKeys.Control, "Ctrl", "Strg"),
        (ModifierKeys.Alt, "Alt", "Alt"),
        (ModifierKeys.Shift, "Shift", "Umschalt"),
        (ModifierKeys.Windows, "Win", "Win"),
    ];

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = new Hotkey(ModifierKeys.None, Key.None);
        if (string.IsNullOrWhiteSpace(text)) return false;

        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        foreach (var part in parts[..^1])
        {
            var match = ModifierNames.FirstOrDefault(m => string.Equals(m.Name, part, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null) return false;
            modifiers |= match.Modifier;
        }
        if (!Enum.TryParse(parts[^1], ignoreCase: true, out Key key) || !Enum.IsDefined(key)) return false;

        hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid;
    }

    /// <summary>Kein reines Zusatztasten-Kürzel. Jede andere Taste ist erlaubt, auch allein.</summary>
    public bool IsValid => Key != Key.None && !IsModifierKey(Key);

    /// <summary>Ohne Strg/Alt/Umschalt/Win: Die Taste löst in allen Programmen nur noch diese Aktion aus.</summary>
    public bool IsSingleKey => Modifiers == ModifierKeys.None;

    /// <summary>Taste (ggf. mit Umschalt), die zum Schreiben gebraucht wird – global belegt stört sie jede Texteingabe.</summary>
    public bool BlocksTyping => (Modifiers is ModifierKeys.None or ModifierKeys.Shift) && IsTypingKey(Key);

    /// <summary>Virtuelle Tastennummer und MOD_*-Flags für RegisterHotKey.</summary>
    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Alt) ? 0x1u : 0) |
        (Modifiers.HasFlag(ModifierKeys.Control) ? 0x2u : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? 0x4u : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? 0x8u : 0);

    /// <summary>Speicherformat, z. B. "Ctrl+Shift+I".</summary>
    public override string ToString() =>
        string.Join("+", ModifierNames.Where(m => Modifiers.HasFlag(m.Modifier)).Select(m => m.Name).Append(Key.ToString()));

    /// <summary>Anzeige, z. B. "Strg + Umschalt + I".</summary>
    public string DisplayText =>
        string.Join(" + ", ModifierNames.Where(m => Modifiers.HasFlag(m.Modifier)).Select(m => m.Display).Append(KeyDisplay(Key)));

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static bool IsTypingKey(Key key) =>
        key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9
            or Key.Space or Key.Return or Key.Tab or Key.Back or Key.Multiply or Key.Add or Key.Subtract or Key.Divide or Key.Decimal
        || key.ToString().StartsWith("Oem", StringComparison.Ordinal);

    private static string KeyDisplay(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num {(int)(key - Key.NumPad0)}",
        Key.Space => "Leertaste",
        Key.Return => "Eingabe",
        Key.Back => "Rücktaste",
        Key.Escape => "Esc",
        Key.Insert => "Einfg",
        Key.Delete => "Entf",
        Key.Home => "Pos1",
        Key.End => "Ende",
        Key.PageUp => "Bild auf",
        Key.PageDown => "Bild ab",
        Key.Left => "Links",
        Key.Right => "Rechts",
        Key.Up => "Oben",
        Key.Down => "Unten",
        Key.Pause => "Pause",
        Key.Scroll => "Rollen",
        Key.Multiply => "Num *",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Divide => "Num /",
        Key.Decimal => "Num ,",
        _ => KeyNameFromLayout(key) ?? key.ToString(),
    };

    /// <summary>Satzzeichentasten (Oem…) mit dem Zeichen des aktuellen Tastaturlayouts anzeigen, z. B. "ß" statt "OemOpenBrackets".</summary>
    private static string? KeyNameFromLayout(Key key)
    {
        if (!key.ToString().StartsWith("Oem", StringComparison.Ordinal)) return null;
        uint ch = Interop.NativeMethods.MapVirtualKeyW((uint)KeyInterop.VirtualKeyFromKey(key), 2) & 0x7FFF; // MAPVK_VK_TO_CHAR
        return ch is > 32 and < 0xFFFF ? char.ToUpperInvariant((char)ch).ToString() : null;
    }
}

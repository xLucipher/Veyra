using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Veyra.Core;

namespace Veyra.SettingsUI;

/// <summary>
/// Eingabefeld für ein Tastenkürzel: hineinklicken und die Kombination oder eine einzelne Taste (z. B. Bild auf) drücken.
/// Rücktaste/Entf ohne Zusatztaste
/// löscht das Kürzel, Tab wechselt wie gewohnt zum nächsten Feld. <see cref="Hotkey"/> ist das Speicherformat
/// ("Ctrl+Shift+I"), angezeigt wird die deutsche Schreibweise.
/// </summary>
public sealed class HotkeyBox : TextBox
{
    public const string EmptyText = "Kein Tastenkürzel";

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).ShowHotkey()));

    public HotkeyBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox)); // Fluent-Stil der TextBox übernehmen
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        ContextMenu = null;
        Cursor = Cursors.Hand;
        ToolTip = "Hineinklicken und eine Tastenkombination oder eine einzelne Taste drücken. Rücktaste oder Entf löscht.";
        ShowHotkey();
    }

    public string? Hotkey
    {
        get => (string?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        HotkeyCapture.Set(true);
        Text = "Taste oder Kombination drücken …";
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        HotkeyCapture.Set(false);
        ShowHotkey();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Alt-Kombinationen kommen als Key.System, die eigentliche Taste steht in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift) return; // Fokus wechseln
        e.Handled = true;

        if (modifiers == ModifierKeys.None && key is Key.Back or Key.Delete)
        {
            Hotkey = null;
            Text = EmptyText;
            return;
        }
        if (Core.Hotkey.IsModifierKey(key) || key is Key.ImeProcessed or Key.DeadCharProcessed) return; // auf die eigentliche Taste warten

        var hotkey = new Hotkey(modifiers, key);
        if (!hotkey.IsValid)
        {
            Text = "Diese Taste lässt sich nicht belegen";
            return;
        }
        Hotkey = hotkey.ToString();
        Text = hotkey.DisplayText;
    }

    private void ShowHotkey()
    {
        if (IsKeyboardFocused) return;
        Text = Core.Hotkey.TryParse(Hotkey, out var hotkey) ? hotkey.DisplayText : EmptyText;
    }
}

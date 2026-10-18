using System.Windows;

namespace Veyra.SettingsUI;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        Closed += (_, _) =>
        {
            Veyra.Core.HotkeyCapture.Set(false); // falls beim Schließen noch ein Tastenkürzel-Feld aktiv war
            viewModel.Dispose();
        };
    }

    /// <summary>Leiste auswählen (Rechtsklick → Einstellungen auf einer bestimmten Leiste).</summary>
    public void SelectBar(string barId) => ((SettingsViewModel)DataContext).SelectBarById(barId);
}

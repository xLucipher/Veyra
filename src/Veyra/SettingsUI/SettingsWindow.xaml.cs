using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Veyra.SettingsUI;

public partial class SettingsWindow : Window
{
    private DateTime _addWidgetClosedUtc;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
        viewModel.WidgetAdded += (_, row) => BringWidgetIntoView(row);
        // Linksbündig unter dem Button: Er steht in der linken Spalte, das Popup öffnet sich über den Inhalt nach rechts.
        AddWidgetPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            [new CustomPopupPlacement(new Point(0, targetSize.Height + 6), PopupPrimaryAxis.Horizontal)];
        Closed += (_, _) =>
        {
            Veyra.Core.HotkeyCapture.Set(false); // falls beim Schließen noch ein Tastenkürzel-Feld aktiv war
            viewModel.Dispose();
        };
    }

    private SettingsViewModel ViewModel => (SettingsViewModel)DataContext;

    /// <summary>Leiste auswählen (Rechtsklick → Einstellungen auf einer bestimmten Leiste).</summary>
    public void SelectBar(string barId) => ViewModel.SelectBarById(barId);

    /// <summary>Seite „Profile“ anzeigen (Profilauswahl → „Profile verwalten …“).</summary>
    public void ShowProfilesPage() => ViewModel.ShowGeneralPage(SettingsViewModel.ProfilesPageIndex);

    /// <summary>Enter im Namensfeld eines Profils speichert den Namen sofort.</summary>
    private void OnProfileNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not System.Windows.Controls.TextBox box) return;
        box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    private void OnAddWidgetClick(object sender, RoutedEventArgs e)
    {
        // Ein Klick auf den Button bei offenem Popup schließt es zuerst (StaysOpen=False) – nicht gleich wieder öffnen.
        if ((DateTime.UtcNow - _addWidgetClosedUtc).TotalMilliseconds < 250) return;
        ViewModel.IsAddWidgetOpen = true;
    }

    private void OnAddWidgetPopupOpened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => WidgetSearchBox.Focus());

    private void OnAddWidgetPopupClosed(object? sender, EventArgs e) => _addWidgetClosedUtc = DateTime.UtcNow;

    private void OnWidgetSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ViewModel.AddFirstSearchResult();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            ViewModel.IsAddWidgetOpen = false;
            e.Handled = true;
        }
    }

    /// <summary>Neu hinzugefügtes Widget (aufgeklappt am Ende der Liste) sichtbar machen.</summary>
    private void BringWidgetIntoView(object row) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => WidgetList.ScrollIntoView(row));
}

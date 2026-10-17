using System.ComponentModel;
using System.Windows;
using CustomTopBar.Core;

namespace CustomTopBar.Updates;

public enum UpdateDecision
{
    Later,
    Skip,
    Install,
}

/// <summary>"Update verfügbar – soll das Update installiert werden?" mit Ja / Später / Nein.</summary>
public partial class UpdateWindow : Window
{
    private readonly UpdateInfo _update;
    private readonly Action _exitApplication;
    private CancellationTokenSource? _download;
    private bool _installing;

    public UpdateWindow(UpdateInfo update, Action exitApplication)
    {
        InitializeComponent();
        _update = update;
        _exitApplication = exitApplication;

        VersionText.Text = $"Version {update.Version} ist verfügbar – installiert ist {UpdateService.CurrentVersion}.";
        NotesText.Text = string.IsNullOrWhiteSpace(update.Notes) ? "Keine Beschreibung vorhanden." : CleanMarkdown(update.Notes);
    }

    /// <summary>Entscheidung des Benutzers; Schließen über das X zählt als "Später".</summary>
    public UpdateDecision Decision { get; private set; } = UpdateDecision.Later;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Während des Downloads schließen = abbrechen.
        _download?.Cancel();
        base.OnClosing(e);
    }

    private void OnLaterClick(object sender, RoutedEventArgs e)
    {
        Decision = UpdateDecision.Later;
        Close();
    }

    private void OnNoClick(object sender, RoutedEventArgs e)
    {
        Decision = UpdateDecision.Skip;
        Close();
    }

    private void OnReleaseLinkClick(object sender, RoutedEventArgs e) => UpdateService.OpenReleasePage(_update.HtmlUrl);

    private async void OnYesClick(object sender, RoutedEventArgs e)
    {
        if (_installing) return;

        if (!UpdateService.CanWriteInstallDirectory(out var folder))
        {
            ShowError($"In den Installationsordner kann ohne Administratorrechte nicht geschrieben werden:\n{folder}\n\n" +
                      "Lade das Update über die Release-Seite herunter oder installiere CustomTopBar in einen Ordner deines Benutzerprofils " +
                      "(z. B. %LocalAppData%\\Programs\\CustomTopBar).");
            return;
        }

        _installing = true;
        Decision = UpdateDecision.Install;
        YesButton.IsEnabled = false;
        NoButton.IsEnabled = false;
        LaterButton.Content = "Abbrechen";
        ErrorText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = "Update wird heruntergeladen …";

        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress.Value = p;
                ProgressText.Text = $"Update wird heruntergeladen … {p:P0}";
            });
            string folderWithNewFiles = await UpdateService.DownloadAndExtractAsync(_update, progress, _download.Token);

            ProgressText.Text = "Installiere – die Leiste startet gleich neu …";
            Progress.IsIndeterminate = true;
            UpdateService.LaunchInstaller(folderWithNewFiles);
            await Task.Delay(400);
            _exitApplication();
        }
        catch (OperationCanceledException)
        {
            Log.Info("Update", "Download abgebrochen");
        }
        catch (Exception ex)
        {
            Log.Error("Update", "Update fehlgeschlagen", ex);
            _installing = false;
            Decision = UpdateDecision.Later;
            YesButton.IsEnabled = true;
            NoButton.IsEnabled = true;
            LaterButton.Content = "Später";
            ProgressPanel.Visibility = Visibility.Collapsed;
            ShowError($"Das Update konnte nicht installiert werden: {ex.Message}");
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    /// <summary>Release-Notizen sind Markdown – für die Anzeige die gröbsten Zeichen entfernen.</summary>
    private static string CleanMarkdown(string text) =>
        string.Join('\n', text.Replace("\r", "").Split('\n').Select(line =>
        {
            string l = line.TrimStart('#', ' ').Replace("**", "").Replace("`", "");
            return l.StartsWith("- ") || l.StartsWith("* ") ? "•  " + l[2..] : l;
        }));
}

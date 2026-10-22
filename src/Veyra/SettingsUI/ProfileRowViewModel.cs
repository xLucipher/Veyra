using System.Windows;
using System.Windows.Input;
using Veyra.Core;

namespace Veyra.SettingsUI;

/// <summary>Eine Zeile auf der Seite „Profile“. Alle Aktionen wirken sofort (siehe <see cref="SettingsViewModel"/>).</summary>
public sealed class ProfileRowViewModel : ObservableObject
{
    private readonly Action<string, string> _rename;
    private string _name;

    public ProfileRowViewModel(string id, string name, string summary, bool isActive, int index, int count,
        Action<string> activate, Action<string, string> rename, Action<string> duplicate, Action<string, int> move, Action<string> delete)
    {
        Id = id;
        _name = name;
        _rename = rename;
        Summary = summary;
        IsActive = isActive;
        Number = index < 9 ? $"{index + 1}" : "";
        ActivateCommand = new RelayCommand(() => activate(Id), () => !IsActive);
        DuplicateCommand = new RelayCommand(() => duplicate(Id));
        MoveUpCommand = new RelayCommand(() => move(Id, -1), () => index > 0);
        MoveDownCommand = new RelayCommand(() => move(Id, 1), () => index < count - 1);
        DeleteCommand = new RelayCommand(() =>
        {
            var answer = MessageBox.Show($"Profil „{Name}“ löschen? Seine Flächen und Widgets gehen dabei verloren.",
                "Profil löschen", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes) delete(Id);
        }, () => count > 1);
    }

    public string Id { get; }

    /// <summary>Name; eine Änderung wird beim Verlassen des Feldes (oder mit Enter) gespeichert.</summary>
    public string Name
    {
        get => _name;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == _name) return;
            _name = value.Trim();
            // Erst nach der Bindungsaktualisierung: Das Umbenennen baut die Liste samt diesem Feld neu auf.
            string name = _name;
            Application.Current.Dispatcher.BeginInvoke(() => _rename(Id, name));
        }
    }

    public string Summary { get; }
    public bool IsActive { get; }

    /// <summary>Schnellwahl-Nummer im Overlay (1–9).</summary>
    public string Number { get; }

    public Visibility ActiveVisibility => IsActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility InactiveVisibility => IsActive ? Visibility.Collapsed : Visibility.Visible;

    public ICommand ActivateCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand DeleteCommand { get; }
}

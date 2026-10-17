# Veyra – Entwicklung

Technische Dokumentation für Build, Aufbau und Releases. Für Installation und Bedienung siehe [README](../README.md).

## Build und Start

**Voraussetzungen:** Windows 10/11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (zum Ausführen genügt die .NET 10 Desktop Runtime). Keine Administratorrechte, keine externen NuGet-Pakete.

```bash
dotnet build Veyra.sln -c Release
```

```bash
dotnet run --project src/Veyra -c Release
```

Eigenständige Ausgabe in einen Ordner (z. B. für den Autostart):

```bash
dotnet publish src/Veyra -c Release -r win-x64 --self-contained false -o publish
```

Danach `publish\Veyra.exe` starten. In Visual Studio einfach `Veyra.sln` öffnen und F5 drücken.

**Kommandozeile**

| Aufruf | Wirkung |
|---|---|
| `Veyra.exe` erneut starten | öffnet das Einstellungsfenster der laufenden Instanz |
| `Veyra.exe --exit` | beendet die laufende Instanz sauber (für Skripte, Installer, Updates) |

---

## Architektur

```
src/Veyra/
├─ App.xaml(.cs)              Einstieg: Einzelinstanz, Logging, Konfiguration, Fehlerbehandlung
├─ Bar/                       Taskleistenfenster und UI
│  ├─ BarManager.cs           Lebenszyklus: Fenster/Widgets erzeugen, positionieren, Systemereignisse, Ein-/Ausblenden
│  ├─ TopBarWindow.xaml(.cs)  WPF-Fenster + Win32-Belange (Stile, WndProc, SetWindowPos)
│  ├─ TopBarViewModel.cs      Darstellung und Kontextmenü-Befehle
│  ├─ BarLayoutPanel.cs       Widget-Layout: feste Bereiche, kollisionsfreie freie Widgets, Drag & Drop
│  ├─ BarPlacement.cs         Geometrie (physische Pixel, DPI-skaliert); fixiert (oben, volle Breite) oder dynamisch (frei, eigene Größe)
│  └─ AppBarRegistration.cs   SHAppBarMessage: Benachrichtigungen + optionale Bereichsreservierung
├─ Widgets/                   Widgets der Leiste
│  ├─ IBarWidget.cs           Vertrag: View, ApplySettings, Start/Stop (pausiert bei Vollbild), Refresh
│  ├─ Clock/                  Uhr (ClockWidget, ClockView) und Kalender-Flyout
│  └─ Audio/                  Audiogeräte-Buttons (Wiedergabe/Mikrofon)
├─ Audio/                     Core Audio per COM: Geräte auflisten, Standardgerät setzen, Änderungen melden
├─ Updates/                   Update-Prüfung über GitHub Releases, Update-Fenster, Installation
├─ Assets/                    App-Icon und Logo (erzeugt aus assets/branding)
├─ Fullscreen/                Vollbild-Erkennung
│  ├─ FullscreenDetector.cs   Ereignisgesteuerte Heuristik mit Entprellung
│  └─ WindowInspector.cs      Fenster-Abfragen (Klasse, Prozess, Cloaking, Stile)
├─ Monitors/                  Monitorverwaltung (EnumDisplayMonitors, DPI, Bildwiederholrate)
├─ Configuration/             AppSettings (Modell + Validierung), SettingsService (Laden/Speichern/Live-Reload), AutoStart
├─ SettingsUI/                Einstellungsfenster (MVVM), Auswahllisten (SettingsOptions), Farbwähler (ColorField)
├─ Interop/                   P/Invoke-Deklarationen, WinEvent-Hook-Wrapper
└─ Core/                      Logging, Einzelinstanz, MVVM-Basisklassen, Farb-Hilfen

assets/branding/              Original-Logos (logo.png, logo_name.png) und README-Banner
installer/                    Inno-Setup-Skript (Veyra.iss) und Assistenten-Bilder
build/                        release.ps1 (Update-Paket, Installer, GitHub-Release), branding.ps1 (Grafiken)
```

Bewusst flach gehalten: keine DI-Container, keine Service-Locator, keine Abstraktion ohne zweiten Anwendungsfall. Die einzige Schnittstelle ist `IBarWidget`, weil dort tatsächlich viele Implementierungen folgen.

### Eingesetzte Win32-APIs

| Zweck | API | Begründung |
|---|---|---|
| Nicht in Alt+Tab / Taskleiste, kein Fokusdiebstahl | `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`, WPF `ShowActivated=False` | Leiste wird nie aktiviert, Klicks gehen nicht auf Kosten des aktiven Fensters |
| Always on Top | `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` | wird bei jedem Vordergrundwechsel erneut gesetzt (billig), damit andere Topmost-Fenster die Leiste nicht dauerhaft verdecken |
| Positionierung | `SetWindowPos` in physischen Pixeln | Per-Monitor-DPI-v2 (Manifest) → keine WPF-DIP-Umrechnungsfehler bei gemischten Skalierungen |
| Multi-Monitor / DPI | `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, `EnumDisplaySettings`, `WM_DISPLAYCHANGE`, `WM_DPICHANGED` | Monitore werden über den Gerätenamen identifiziert; Handles werden nach jeder Änderung neu ermittelt |
| Vollbild-Erkennung | `SetWinEventHook` (Foreground, Minimize, MoveSizeEnd), `RegisterShellHookWindow`, `ABN_FULLSCREENAPP`, `SHQueryUserNotificationState`, `GetTopWindow`/`GetWindow` (Z-Reihenfolge), `DwmGetWindowAttribute(DWMWA_CLOAKED)` | ereignisbasiert, siehe unten |
| Reservierter Bereich | `SHAppBarMessage` (`ABM_NEW/QUERYPOS/SETPOS/REMOVE`) | Windows rechnet den Arbeitsbereich selbst um |
| Explorer-Neustart | `RegisterWindowMessage("TaskbarCreated")` | AppBar neu registrieren, neu positionieren |
| Sperren/Entsperren, Standby | `WTSRegisterSessionNotification`, `WM_POWERBROADCAST` | nach Entsperren/Aufwachen neu positionieren und neu bewerten |
| Systemzeit/Zeitzone | `WM_TIMECHANGE` | Uhr sofort aktualisieren |
| Fenster-Optik | `DwmSetWindowAttribute` (keine runden Ecken, von „Desktop anzeigen“-Peek ausgenommen) | |

---

## Vollbild-Erkennung

**Auslöser – kein hochfrequentes Polling:**

- WinEvents: Vordergrundwechsel, Minimieren/Wiederherstellen, Ende von Verschieben/Größenänderung
- Shell-Hook: Fensteraktivierung, „Rude App“-Aktivierung (Vollbild), Fenster geschlossen, Monitorwechsel
- `ABN_FULLSCREENAPP` von Explorer (die Leiste ist dafür immer als AppBar registriert – ohne Bereich zu reservieren)
- eine billige Sicherheitsprüfung alle 2 s (konfigurierbar, abschaltbar) für Übergänge ohne Systemereignis

Ereignis-Salven werden in einem 40-ms-Fenster zu einer Auswertung zusammengefasst.

**Entscheidung** (nur für den Monitor der Leiste):

1. Meldet die Shell `QUNS_RUNNING_D3D_FULL_SCREEN` und liegt das Vordergrundfenster auf diesem Monitor → **exklusives Vollbild**.
2. Sonst wird die Z-Reihenfolge von oben durchlaufen; das erste echte Anwendungsfenster auf dem Monitor entscheidet. Übersprungen werden: eigene Leiste, unsichtbare/minimierte/gecloakte Fenster (andere virtuelle Desktops), Click-through-Overlays (`WS_EX_TRANSPARENT`, z. B. GPU-Overlays), Tool- und No-Activate-Fenster, vollständig transparente Layered-Fenster, Shell-Oberflächen (Alt+Tab, Task-Ansicht, Startmenü, Benachrichtigungen) sowie Topmost-Fenster, die selbst kein Vollbild sind (z. B. Bild-im-Bild). Erreicht die Suche den Desktop → kein Vollbild.
3. Ein Fenster ist **randloses Vollbild**, wenn es den gesamten Monitor überdeckt **und** keine Titelleiste hat oder sein Client-Bereich den Monitor vollständig füllt.

**Warum reine Größenvergleiche nicht reichen:** Ein maximiertes Fenster ist bei automatisch ausgeblendeter Windows-Taskleiste genauso groß wie ein Vollbildfenster. Deshalb gilt ein *maximiertes Fenster mit Größenrahmen* (`IsZoomed` + `WS_THICKFRAME`) grundsätzlich nicht als Vollbild – außer sein Client-Bereich füllt den Monitor, es ist im Vordergrund **und** die Shell meldet `QUNS_BUSY` (deckt Browser ab, die für F11 maximiert bleiben).

**Flackerschutz:** Ausblenden passiert sofort (die Leiste liegt nie sichtbar über einem Spiel), Einblenden erst, wenn „kein Vollbild“ 300 ms stabil ist. Alt+Tab-Umschalter und Startmenü werden übersprungen, sodass das darunterliegende Spiel weiter erkannt wird.

**Gaming-Verhalten:** Während die Leiste ausgeblendet ist, ist auch der Uhr-Timer gestoppt; es laufen nur noch die Ereignis-Hooks und die 2-s-Sicherheitsprüfung (wenige Mikrosekunden). Mit Software-Rendering (Standard) nutzt die Leiste die GPU überhaupt nicht.

### Bekannte Einschränkungen

- **Monitor bei exklusivem Vollbild:** `SHQueryUserNotificationState` sagt nicht, *auf welchem* Monitor das Vollbild läuft. Die Leiste nimmt den Monitor des Vordergrundfensters an.
- **Spiele mit `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE`-Hauptfenster** (sehr selten) werden übersprungen → in die Liste *„Immer als Vollbild behandeln“* eintragen.
- **Apps, die für ihr Vollbild maximiert bleiben, ohne dass die Shell `QUNS_BUSY` meldet**, werden nicht erkannt → ebenfalls über *„Immer als Vollbild behandeln“* lösbar (dann gilt das Fenster als Vollbild, sobald es den Arbeitsbereich ausfüllt – also auch im normal maximierten Zustand).
- **Fehlerkennungen** (z. B. ein randloses, monitorgroßes Tool) → in *„Nie als Vollbild behandeln“* eintragen.
- Prozessnamen von Prozessen mit höherer Integritätsstufe oder geschützten Anti-Cheat-Prozessen sind evtl. nicht lesbar; die geometrische Erkennung funktioniert trotzdem, nur die Ausnahmelisten greifen dann nicht.
- Vollbild auf einem **anderen** Monitor blendet die Leiste bewusst nicht aus.
- Übergänge ohne jedes Systemereignis (z. B. F11 im Browser ohne Fokuswechsel) erkennt meist `ABN_FULLSCREENAPP`; andernfalls greift spätestens die Sicherheitsprüfung nach ≤ 2 s.

---

## Widget-Layout

Feste Widgets (Uhr) sitzen in *Links*, *Mitte* oder *Rechts* mit Randabstand und Mindestabstand. Freie Widgets werden per Drag & Drop platziert; ihre Position wird als Anteil der Leistenbreite gespeichert.

**Layout-Regeln** (`BarLayoutPanel`):

1. Feste Widgets werden zuerst platziert. Ihr Platz plus Mindestabstand ist gesperrt, kein freies Widget kann ihn belegen oder das feste Widget verschieben. Mehrere feste Widgets im selben Bereich stehen nebeneinander.
2. Freie Widgets kommen in der Reihenfolge ihrer Wunschposition in die nächstgelegene freie Lücke, die groß genug ist. Dadurch können sie sich nie überlappen. Wächst ein Widget (z. B. ein neues Gerät), rückt es in die nächste passende Lücke.
3. Ein gezogenes Widget wird zuletzt platziert. Es weicht allen anderen aus und verdrängt niemanden. Beim Loslassen wird die tatsächlich erreichte Position gespeichert.
4. Passt ein freies Widget nirgends mehr hin, wird es ausgeblendet statt zu überlappen.

Audiogeräte kommen aus Windows Core Audio (`IMMDeviceEnumerator`, Änderungen per `IMMNotificationClient`); das Standardgerät wird über die seit Windows 7 stabile Schnittstelle `IPolicyConfig` gesetzt. Symbole: [AudioIcons.cs](../src/Veyra/Audio/AudioIcons.cs) (Segoe Fluent Icons plus eigene Vektorpfade, automatische Wahl nach Gerätename und Formfaktor).

---

## Update-Dienst

Abfrage von `api.github.com/repos/xLucipher/Veyra/releases/latest`, Vergleich mit der Assembly-Version, Download des `*-win-x64.zip`-Anhangs mit Größen- und SHA-256-Prüfung, Austausch per Skript nach `--exit` und Neustart. Protokoll: `%TEMP%\Veyra-update\install-update.log`. In Debug-Builds lässt sich die API-Adresse für Tests mit `VEYRA_UPDATE_API` umbiegen (`VEYRA_UPDATE_DELAY_S` verkürzt die Startverzögerung).

## Release veröffentlichen

**Voraussetzung:** [Inno Setup 6](https://jrsoftware.org/isinfo.php) für den Installer, z. B. so installiert:

```bash
winget install JRSoftware.InnoSetup --scope user
```

1. In `src/Veyra/Veyra.csproj` die `<Version>` erhöhen, committen und pushen.
2. Paket und Installer bauen und als GitHub-Release veröffentlichen (Tag `v<Version>`, Setup und ZIP als Anhang):

```bash
.\build\release.ps1 -Publish -NotesFile .\notes.md
```

Ohne `-Publish` entstehen nur `artifacts\Veyra-Setup-<Version>.exe` (Installer für Erstinstallationen) und `artifacts\Veyra-<Version>-win-x64.zip` (Paket für die Update-Funktion) zum Testen. Ohne Notizdatei erzeugt GitHub die Release-Notizen aus den Commits. Das Skript veröffentlicht nur von einem sauberen, gepushten Stand.

Der Installer ([installer/Veyra.iss](../installer/Veyra.iss)) lässt sich auf einem Rechner mit installierter Runtime mit `Veyra-Setup-<Version>.exe /SIMULATEMISSINGRUNTIME` testen. Dann erscheint die Seite für die fehlende Runtime.

## Logo und Grafiken

Die Original-Logos liegen in [assets/branding](../assets/branding) (`logo.png`, `logo_name.png`). Daraus erzeugt [build/branding.ps1](../build/branding.ps1) das App-Icon (`src/Veyra/Assets/Veyra.ico`), das Fensterlogo, das README-Banner und die Bilder des Installer-Assistenten. Das Skript muss nur neu laufen, wenn sich die Logos ändern.

---

## Konfiguration

Datei: `%AppData%\Veyra\settings.json` – wird beim ersten Start mit Standardwerten angelegt. Kommentare (`//`) und abschließende Kommas sind erlaubt. Änderungen (über das Einstellungsfenster **oder** direkt in der Datei) werden ohne Neustart übernommen.

**Einstellungsfenster:** dunkles Fluent-Design (WPF-`ThemeMode="Dark"`, in .NET enthalten – keine Zusatzpakete). Formate, Sprache, Schriftart/-gewicht, Verzögerungen und Monitor werden per Dropdown mit Live-Vorschau gewählt; Farben über eine Palette mit Farbfeldern oder als eigener Hex-Wert; Größen per Auswahl oder eigenem Wert; Vollbild-Ausnahmen aus der Liste laufender Programme.

**Fehlerhafte Datei:** Beim Start wird sie als `settings.json.invalid-<Zeitstempel>` gesichert und durch Standardwerte ersetzt; im laufenden Betrieb bleiben die bisherigen Einstellungen aktiv (Warnung im Log). Ungültige Einzelwerte (Farben, Formate, Bereiche) werden auf Standard bzw. Grenzwerte korrigiert.

| Bereich | Schlüssel | Standard | Bedeutung |
|---|---|---|---|
| Appearance | `Mode` | `FullWidth` | `FullWidth` = fixiert (oben, volle Breite) · `Floating` = dynamisch (frei beweglich) |
| | `Height` | `48` | Höhe in geräteunabhängigen Pixeln (×1,5 bei 150 % Skalierung) |
| | `BackgroundColor` | `#202127` | `#RGB`, `#RRGGBB`, `#AARRGGBB` |
| | `ForegroundColor` / `FontFamily` | `#FFFFFF` / `Segoe UI` | Standard für alle Widgets |
| | `BorderColor` / `BorderThickness` | `#2E3038` / `1` | fixiert: Trennlinie unten, dynamisch: Rahmen rundherum (0 = aus) |
| | `Width` / `FloatingHeight` | `1200` / `48` | dynamisch: Größe in px |
| | `FloatingLeft` / `FloatingTop` | *(leer)* / *(leer)* | dynamisch: Position relativ zum Monitor; links leer = zentriert, oben leer = `MarginTop` |
| | `Layer` | `Topmost` | dynamisch: `Topmost`, `Normal` oder `Desktop` (hinter allen Fenstern) |
| | `LockPosition` | `false` | dynamisch: Verschieben/Größenänderung per Maus sperren |
| | `BackgroundOpacity` / `CornerRadius` | `1` / `12` | dynamisch: Deckkraft 0–1 (0 = durchsichtig, Klicks gehen durch) und Eckenradius |
| Behavior | `AlwaysOnTop` | `true` | |
| | `ReserveScreenSpace` | `false` | `true` = AppBar (maximierte Fenster beginnen unter der Leiste), `false` = Overlay |
| | `AutoHide`, `AutoHideDelayMs` | `false`, `400` | *geplant, in V1 ohne Wirkung* |
| | `HideOnFullscreen` | `true` | bei exklusivem Vollbild ausblenden |
| | `HideOnBorderlessFullscreen` | `true` | bei randlosem Vollbild ausblenden |
| | `FullscreenShowDelayMs` | `300` | Entprellung beim Wiedereinblenden |
| | `FullscreenSafetyCheckIntervalMs` | `2000` | Sicherheitsprüfung, `0` = aus |
| | `FullscreenExcludedProcesses` | `[]` | z. B. `["vlc"]` – nie Vollbild |
| | `FullscreenForcedProcesses` | `[]` | z. B. `["MeinSpiel"]` – Vollbild, sobald Arbeitsbereich gefüllt |
| Clock | `TimeFormat` | `HH:mm:ss` | allein maßgeblich: `HH:mm` = ohne Sekunden, `HH:mm:ss` = mit; 12 h: `h:mm tt` (fehlen der Sprache AM/PM-Bezeichner, wird „AM“/„PM“ verwendet) |
| | `ShowSeconds` | *(abgeleitet)* | wird beim Speichern aus `TimeFormat` gesetzt, nur informativ |
| | `ShowDate` / `DateFormat` | `false` / `dddd, d. MMMM yyyy` | |
| | `ShowWeekNumber` / `WeekNumberFormat` | `false` / `KW {0}` | ISO-8601-Kalenderwoche |
| | `FontSize`, `FontFamily`, `FontWeight`, `FontColor` | `20`, *(leer = Standard)*, `SemiBold`, `#FFFFFF` | `FontWeight`: Normal, Medium, SemiBold, Bold |
| | `DateFontSize`, `DateFontColor` | `11`, `#B4B7C2` | |
| | `Culture` | *(leer = System)* | z. B. `de-DE` |
| System | `StartWithWindows` | `false` | HKCU-Run-Eintrag, ohne Adminrechte |
| | `MonitorSelection` | `Primary` | oder Gerätename, z. B. `\\.\DISPLAY2` (siehe Einstellungen → Monitore) |
| | `EnableLogging` | `true` | Info-/Diagnose-Log; Fehler werden immer protokolliert |
| | `UseSoftwareRendering` | `true` | keine GPU-Nutzung; wirkt nach Neustart |

**Logs:** `%AppData%\Veyra\logs\topbar-JJJJMMTT.log` (7 Tage Aufbewahrung, max. 20 MB pro Tag). Vollbild-Übergänge werden mit Fensterklasse, Prozess und Rechteck protokolliert – ideal zum Pflegen der Ausnahmelisten.

---

## Stabilität

| Situation | Verhalten |
|---|---|
| Explorer-Neustart | `TaskbarCreated` → AppBar neu registrieren, ggf. Bereich neu reservieren, neu positionieren |
| Monitor an-/abgesteckt, Auflösung oder DPI geändert | `WM_DISPLAYCHANGE`/`WM_DPICHANGED` (gebündelt) → Monitor neu auflösen, bei fehlendem Monitor auf den primären ausweichen und automatisch zurückkehren |
| Sperren/Entsperren, Standby | nach Entsperren/Aufwachen neu positionieren und Vollbild neu bewerten |
| Mehrfachstart | benannter Mutex; der zweite Start öffnet die Einstellungen der laufenden Instanz |
| Vollbild-App stürzt ab | Shell-Hook „Fenster zerstört“ bzw. Sicherheitsprüfung → Leiste erscheint wieder |
| Unerwartete Ausnahme | UI-Fehler werden protokolliert, die Leiste läuft weiter; bei Prozessabbruch wird eine AppBar-Reservierung noch freigegeben |

---

## Roadmap

- **Dynamischer Modus:** optional Mica/Acrylic-Hintergrund über `DWMWA_SYSTEMBACKDROP_TYPE`, Einrasten an Bildschirmrändern.
- **Auto-Hide:** Leiste außerhalb des sichtbaren Bereichs parken, Einblenden über einen 1-px-Auslöserstreifen am oberen Rand; während Vollbild deaktiviert.
- **Mehrere Leisten:** `BarManager` je Monitor instanziieren (Detektor und Fenster sind bereits monitorbezogen).
- **Weitere Widgets:** `IBarWidget` implementieren, Typ in `WidgetType`/`WidgetCatalog` eintragen und in `BarManager.CreateWidgets` erzeugen; fest oder frei legt `WidgetEntry.IsFixedType` fest. Kandidaten: CPU/RAM (PerformanceCounter-freie `GetSystemTimes`/`GlobalMemoryStatusEx`), Lautstärke (Core Audio), Medien (`GlobalSystemMediaTransportControlsSessionManager`), virtuelle Desktops.

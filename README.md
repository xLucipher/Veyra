# CustomTopBar

Eine ressourcenschonende, konfigurierbare **Zusatz-Taskleiste am oberen Bildschirmrand** für Windows 11 – inspiriert von KDE-Plasma-Panels. Sie ergänzt die Windows-Taskleiste, ohne sie zu verändern.

**Version 1:** dunkle, durchgehende Leiste mit exakt zentrierter Digitaluhr, zuverlässiger Vollbild-Erkennung (exklusiv **und** randlos), JSON-Konfiguration mit Live-Übernahme, Einstellungsfenster, optionaler AppBar-Reservierung und Logging.

---

## Build und Start

**Voraussetzungen:** Windows 10/11 x64, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (zum Ausführen genügt die .NET 10 Desktop Runtime). Keine Administratorrechte, keine externen NuGet-Pakete.

```bash
dotnet build CustomTopBar.sln -c Release
```

```bash
dotnet run --project src/CustomTopBar -c Release
```

Eigenständige Ausgabe in einen Ordner (z. B. für den Autostart):

```bash
dotnet publish src/CustomTopBar -c Release -r win-x64 --self-contained false -o publish
```

Danach `publish\CustomTopBar.exe` starten. In Visual Studio einfach `CustomTopBar.sln` öffnen und F5 drücken.

**Bedienung**

| Aktion | Wirkung |
|---|---|
| Rechtsklick auf die Leiste | Kontextmenü: *Einstellungen*, *Leiste neu laden*, *Beenden* |
| `CustomTopBar.exe` erneut starten | öffnet das Einstellungsfenster der laufenden Instanz (es läuft nie mehr als eine Instanz) |
| `CustomTopBar.exe --exit` | beendet die laufende Instanz sauber, z. B. für Skripte oder vor einem Update |
| Einstellungen → Allgemein → *CustomTopBar beenden* | beendet die Anwendung auch dann, wenn die Leiste gerade wegen Vollbild ausgeblendet ist |

---

## Architektur

```
src/CustomTopBar/
├─ App.xaml(.cs)              Einstieg: Einzelinstanz, Logging, Konfiguration, Fehlerbehandlung
├─ Bar/                       Taskleistenfenster und UI
│  ├─ BarManager.cs           Lebenszyklus: Fenster/Widgets erzeugen, positionieren, Systemereignisse, Ein-/Ausblenden
│  ├─ TopBarWindow.xaml(.cs)  WPF-Fenster + Win32-Belange (Stile, WndProc, SetWindowPos)
│  ├─ TopBarViewModel.cs      Darstellung und Kontextmenü-Befehle
│  ├─ BarLayoutPanel.cs       Widget-Layout: feste Bereiche, kollisionsfreie freie Widgets, Drag & Drop
│  ├─ BarPlacement.cs         Geometrie (physische Pixel, DPI-skaliert); Erweiterungspunkt für Floating
│  └─ AppBarRegistration.cs   SHAppBarMessage: Benachrichtigungen + optionale Bereichsreservierung
├─ Widgets/                   Widgets der Leiste
│  ├─ IBarWidget.cs           Vertrag: View, ApplySettings, Start/Stop (pausiert bei Vollbild), Refresh
│  ├─ Clock/                  Uhr (ClockWidget, ClockView) und Kalender-Flyout
│  └─ Audio/                  Audiogeräte-Buttons (Wiedergabe/Mikrofon)
├─ Audio/                     Core Audio per COM: Geräte auflisten, Standardgerät setzen, Änderungen melden
├─ Updates/                   Update-Prüfung über GitHub Releases, Update-Fenster, Installation
├─ Fullscreen/                Vollbild-Erkennung
│  ├─ FullscreenDetector.cs   Ereignisgesteuerte Heuristik mit Entprellung
│  └─ WindowInspector.cs      Fenster-Abfragen (Klasse, Prozess, Cloaking, Stile)
├─ Monitors/                  Monitorverwaltung (EnumDisplayMonitors, DPI, Bildwiederholrate)
├─ Configuration/             AppSettings (Modell + Validierung), SettingsService (Laden/Speichern/Live-Reload), AutoStart
├─ SettingsUI/                Einstellungsfenster (MVVM), Auswahllisten (SettingsOptions), Farbwähler (ColorField)
├─ Interop/                   P/Invoke-Deklarationen, WinEvent-Hook-Wrapper
└─ Core/                      Logging, Einzelinstanz, MVVM-Basisklassen, Farb-Hilfen
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

## Widgets

Widgets werden unter **Einstellungen → Widgets** hinzugefügt, aktiviert oder entfernt. Dasselbe Widget kann mehrfach vorkommen; jede Instanz hat eine eigene ID und eigene Einstellungen.

| Art | Widgets | Platzierung |
|---|---|---|
| **Fest** | Uhr | Bereich *Links*, *Mitte* oder *Rechts*, dazu **Abstand zum Rand** (nur Links/Rechts) und **Mindestabstand zu anderen Widgets**. In der Mitte ist die Uhr immer exakt zentriert. |
| **Frei** | Wiedergabegeräte, Mikrofone | **Per Drag & Drop direkt auf der Leiste** verschieben (ab 6 px Bewegung; ein normaler Klick bleibt ein Klick). Die Position wird als Anteil der Leistenbreite gespeichert und passt sich so an andere Auflösungen an. |

**Layout-Regeln** (`BarLayoutPanel`):

1. Feste Widgets werden zuerst platziert. Ihr Platz plus Mindestabstand ist gesperrt, kein freies Widget kann ihn belegen oder das feste Widget verschieben. Mehrere feste Widgets im selben Bereich stehen nebeneinander.
2. Freie Widgets kommen in der Reihenfolge ihrer Wunschposition in die nächstgelegene freie Lücke, die groß genug ist. Dadurch können sie sich nie überlappen. Wächst ein Widget (z. B. ein neues Gerät), rückt es in die nächste passende Lücke.
3. Ein gezogenes Widget wird zuletzt platziert. Es weicht allen anderen aus und verdrängt niemanden. Beim Loslassen wird die tatsächlich erreichte Position gespeichert.
4. Passt ein freies Widget nirgends mehr hin, wird es ausgeblendet statt zu überlappen.

**Audio-Widgets:** Ein Button je Gerät, das jeweils aktive ist mit einer Akzentlinie markiert. Ein Klick setzt das Gerät als Standard für Medien, Spiele und Kommunikation. Die Geräte kommen aus Windows (Core Audio), deshalb funktioniert das auf jedem Rechner. Neue oder entfernte Geräte und Standardwechsel werden per Ereignis erkannt, ohne Polling. Pro Widget wählst du *alle aktiven Geräte* oder einzelne per Häkchen, z. B. ein Widget je Gerät. Symbol und Kurzname je Gerät stellst du unter **Einstellungen → Audiogeräte** ein.

**Gerätesymbole:** Ein Klick auf die Symbolauswahl öffnet ein Raster mit den Gruppen *Wiedergabe* (Lautsprecher, Regal- und Stereo-Lautsprecher, Soundbar, Soundbar mit Subwoofer, Kopfhörer, Over-Ear, Earbuds, Monitor/TV), *Mikrofone* (Mikrofon, Studio-, Podcast-, USB-Tisch-, Hand- und Ansteckmikrofon, Webcam, Audio-Interface/XLR) und *Allgemein* (Headset, Bluetooth, virtuelles Gerät, Stream/Mix, Lautstärke, Musik, Spiele, Telefon, Laptop). Die eigenen Symbole sind Vektorgrafiken im Linienstil von Segoe Fluent Icons ([AudioIcons.cs](src/CustomTopBar/Audio/AudioIcons.cs)) und brauchen keine Bilddateien. Bei *Automatisch* wird das Symbol zuerst am Gerätenamen erkannt (z. B. „Kopfhörer“, „Soundbar“, „Virtual“/„VB-Audio“, „XLR“/„Focusrite“, „Yeti“/„Wave:“, „Webcam“/„Brio“, „AirPods“/„Buds“), sonst am Formfaktor, den Windows meldet. Zum Setzen des Standardgeräts dient die nicht offiziell dokumentierte, seit Windows 7 stabile Schnittstelle `IPolicyConfig`, die auch die Windows-Soundsteuerung und Tools wie EarTrumpet nutzen.

**Uhr:** Ein Klick öffnet einen Monatskalender direkt unter der Uhr. Ein Klick daneben oder Esc schließt ihn.

**Farben:** Ein Klick auf das Farbfeld öffnet einen Farbkreis. Darin klicken oder gedrückt halten und ziehen, darunter sitzt ein Helligkeitsregler. Alternativ gibt es Vorlagen und das Hex-Feld.

## Updates

Die Leiste aktualisiert sich über **GitHub Releases** dieses Repositorys. Weil es öffentlich ist, braucht es dafür keine Anmeldung und keinen Token.

- **Prüfung:** 20 s nach dem Start und danach alle 6 Stunden. Während die Leiste wegen eines Vollbildspiels ausgeblendet ist, wird nie gefragt, sondern einige Minuten später erneut geprüft.
- **Gibt es eine neuere Version**, erscheint das Fenster „Update verfügbar“ mit den Neuerungen und drei Möglichkeiten:
  - **Ja, installieren:** Das ZIP wird heruntergeladen und geprüft (Größe und SHA-256 aus GitHub). Dann beendet sich die Leiste, ein kleines Skript ersetzt die Dateien im Installationsordner, und die Leiste startet neu. Die Einstellungen in `%AppData%` bleiben unberührt.
  - **Später:** Die Leiste fragt frühestens in 24 Stunden wieder.
  - **Nein:** Diese Version wird übersprungen, erst eine neuere wird wieder angeboten.
- **Einstellungen → Allgemein → Updates:** automatische Prüfung an oder aus, *Jetzt nach Updates suchen* (fragt auch bei übersprungener Version) und die Release-Seite.
- **Installationsordner:** Er muss ohne Adminrechte beschreibbar sein, z. B. `%LocalAppData%\Programs\CustomTopBar`. Unter „Programme“ verweist das Update-Fenster stattdessen auf die Release-Seite.
- **Protokoll der Installation:** `%TEMP%\CustomTopBar-update\install-update.log`.

### Release veröffentlichen

1. In `src/CustomTopBar/CustomTopBar.csproj` die `<Version>` erhöhen, committen und pushen.
2. Paket bauen und als GitHub-Release veröffentlichen (Tag `v<Version>`, ZIP als Anhang):

```bash
.\build\release.ps1 -Publish -NotesFile .\notes.md
```

Ohne `-Publish` entsteht nur `artifacts\CustomTopBar-<Version>-win-x64.zip` zum Testen. Ohne Notizdatei erzeugt GitHub die Release-Notizen aus den Commits. Das Skript veröffentlicht nur von einem sauberen, gepushten Stand.

## Konfiguration

Datei: `%AppData%\CustomTopBar\settings.json` – wird beim ersten Start mit Standardwerten angelegt. Kommentare (`//`) und abschließende Kommas sind erlaubt. Änderungen (über das Einstellungsfenster **oder** direkt in der Datei) werden ohne Neustart übernommen.

**Einstellungsfenster:** dunkles Fluent-Design (WPF-`ThemeMode="Dark"`, in .NET enthalten – keine Zusatzpakete). Formate, Sprache, Schriftart/-gewicht, Verzögerungen und Monitor werden per Dropdown mit Live-Vorschau gewählt; Farben über eine Palette mit Farbfeldern oder als eigener Hex-Wert; Größen per Auswahl oder eigenem Wert; Vollbild-Ausnahmen aus der Liste laufender Programme.

**Fehlerhafte Datei:** Beim Start wird sie als `settings.json.invalid-<Zeitstempel>` gesichert und durch Standardwerte ersetzt; im laufenden Betrieb bleiben die bisherigen Einstellungen aktiv (Warnung im Log). Ungültige Einzelwerte (Farben, Formate, Bereiche) werden auf Standard bzw. Grenzwerte korrigiert.

| Bereich | Schlüssel | Standard | Bedeutung |
|---|---|---|---|
| Appearance | `Mode` | `FullWidth` | `FullWidth` · `Floating` *(geplant – fällt auf FullWidth zurück)* |
| | `Height` | `48` | Höhe in geräteunabhängigen Pixeln (×1,5 bei 150 % Skalierung) |
| | `BackgroundColor` | `#202127` | `#RGB`, `#RRGGBB`, `#AARRGGBB` |
| | `ForegroundColor` / `FontFamily` | `#FFFFFF` / `Segoe UI` | Standard für alle Widgets |
| | `BorderColor` / `BorderThickness` | `#2E3038` / `1` | dezente Trennlinie unten (0 = aus) |
| | `Width`, `MarginTop`, `CornerRadius`, `BackgroundOpacity` | | *für Floating vorbereitet, in V1 ohne Wirkung* |
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

**Logs:** `%AppData%\CustomTopBar\logs\topbar-JJJJMMTT.log` (7 Tage Aufbewahrung, max. 20 MB pro Tag). Vollbild-Übergänge werden mit Fensterklasse, Prozess und Rechteck protokolliert – ideal zum Pflegen der Ausnahmelisten.

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

## Status der Akzeptanzkriterien (Version 1)

Auf dem Entwicklungsrechner geprüft (Windows 11, 5120×1440 @ 120 Hz, 100 %):

| Kriterium | Status |
|---|---|
| Leiste oben sichtbar, Uhr sekundengenau und exakt mittig | ✅ geprüft (Screenshot) |
| Maximierte Fenster überdecken die Leiste nicht | ✅ geprüft (Leiste bleibt sichtbar; mit `ReserveScreenSpace` beginnen sie darunter) |
| Ausblenden bei randlosem Vollbild, Wiedereinblenden danach | ✅ geprüft mit monitorgroßem randlosem Testfenster |
| Ausblenden bei exklusivem Vollbild | ⚠️ implementiert (`QUNS_RUNNING_D3D_FULL_SCREEN` + Geometrie), mit echtem Spiel noch zu testen |
| Alt+Tab ohne störendes Verhalten | ✅ durch Design (kein Fokus, Umschalter wird übersprungen, Entprellung); manuell zu bestätigen |
| Keine Administratorrechte | ✅ `asInvoker`, nur HKCU |
| Konfiguration bleibt erhalten, Live-Übernahme, defekte JSON | ✅ geprüft |
| Einstellungsfenster, Einzelinstanz, vollständiges Beenden | ✅ geprüft |
| AppBar-Bereich reservieren und beim Beenden freigeben | ✅ geprüft (Arbeitsbereich vorher/nachher identisch) |
| Leerlauf-Ressourcen | ✅ 0 ms CPU in 10 s, ca. 27 MB privater Speicher |
| Explorer-Neustart, DPI-Wechsel, Monitor-Hotplug | ⚠️ implementiert, manuell zu testen |

---

## Roadmap

- **Floating-Modus (B):** `BarPlacement` um Breite/Zentrierung/Abstand erweitern; Fenster mit `DWMWCP_ROUND` und optional `DWMWA_SYSTEMBACKDROP_TYPE` (Mica/Acrylic) statt WPF-Transparenz – bleibt performant.
- **Auto-Hide:** Leiste außerhalb des sichtbaren Bereichs parken, Einblenden über einen 1-px-Auslöserstreifen am oberen Rand; während Vollbild deaktiviert.
- **Mehrere Leisten:** `BarManager` je Monitor instanziieren (Detektor und Fenster sind bereits monitorbezogen).
- **Weitere Widgets:** `IBarWidget` implementieren, Typ in `WidgetType`/`WidgetCatalog` eintragen und in `BarManager.CreateWidgets` erzeugen; fest oder frei legt `WidgetEntry.IsFixedType` fest. Kandidaten: CPU/RAM (PerformanceCounter-freie `GetSystemTimes`/`GlobalMemoryStatusEx`), Lautstärke (Core Audio), Medien (`GlobalSystemMediaTransportControlsSessionManager`), virtuelle Desktops.

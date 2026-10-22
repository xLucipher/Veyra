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
| `Veyra.exe --grant-fps <SID oder Konto>` | nimmt das Konto in die Gruppe „Leistungsprotokollbenutzer“ auf und beendet sich (braucht Adminrechte; startet der FPS-Button bzw. der Installer per UAC) |

---

## Architektur

**Begriff:** In der Oberfläche heißt jedes Veyra-Fenster **Fläche** – fixiert als Leiste oben über die volle Breite oder dynamisch mit freier Größe und Position. Im Code und in der `settings.json` heißt sie weiterhin `Bar` (`Bars[]`, `BarController`, `BarSettings`). Automatisch vergebene alte Namen („Leiste 2“) werden beim Laden zu „Fläche 2“.

```
src/Veyra/
├─ App.xaml(.cs)              Einstieg: Einzelinstanz, Logging, Konfiguration, Fehlerbehandlung
├─ Bar/                       Taskleistenfenster und UI
│  ├─ BarManager.cs           alle Flächen: je Eintrag in `Bars` ein BarController, Hinzufügen/Entfernen zur Laufzeit
│  ├─ WidgetServices.cs       gemeinsame Datenquellen der Widgets (erst bei Bedarf erzeugt, von allen Flächen geteilt)
│  ├─ BarController.cs        eine Fläche: Fenster/Widgets erzeugen, positionieren, Systemereignisse, eigene Vollbild-Erkennung
│  ├─ TopBarWindow.xaml(.cs)  WPF-Fenster + Win32-Belange (Stile, WndProc, SetWindowPos)
│  ├─ TopBarViewModel.cs      Darstellung und Kontextmenü-Befehle
│  ├─ BarLayoutPanel.cs       Widget-Layout: feste Bereiche, kollisionsfreie freie Widgets, Drag & Drop
│  ├─ BarPlacement.cs         Geometrie (physische Pixel, DPI-skaliert); fixiert (oben, volle Breite) oder dynamisch (frei, eigene Größe)
│  └─ AppBarRegistration.cs   SHAppBarMessage: Benachrichtigungen + optionale Bereichsreservierung
├─ Widgets/                   Widgets der Fläche
│  ├─ IBarWidget.cs           Vertrag: View, ApplySettings, Start/Stop (pausiert bei Vollbild), Refresh
│  ├─ FlyoutWindow.cs         Basis aller Widget-Popups: Platzierung unter/über dem Widget, Schließen bei Klick daneben oder Esc, optional mit der Maus verschiebbar (Kalender, Timer)
│  ├─ Clock/                  Uhr (ClockWidget, ClockView), Kalender-Flyout, Weltuhr
│  ├─ Audio/                  Audiogeräte-Buttons (Wiedergabe/Mikrofon)
│  ├─ Metrics/                CPU, GPU, RAM, GPU-Temperatur, VRAM, GPU-Takt, GPU-Lüfter, Akku, Laufwerk: Wert, Balken oder Verlauf; Geräte-Akku (Bluetooth, Razer); FPS
│  ├─ Media/                  Medien: Titel, Cover, Zurück/Wiedergabe/Weiter; Auswahl der Wiedergabe (MediaFlyout), durchlaufender Text (MarqueeText)
│  ├─ Content/                Bild, Notiz (mit Bearbeiten-Popup), Trennlinie
│  ├─ ClipboardHistory/       Zwischenablage-Verlauf (Popup mit Suche, Anheften, Einfügen)
│  ├─ Launcher/               Schnellstart (Shell-Symbole, Drag & Drop)
│  ├─ Weather/                Wetter (Popup mit Stunden und 7 Tagen)
│  ├─ Brightness/             Helligkeit je Bildschirm und Nachtmodus
│  ├─ Network/                Netzwerk (Verbindung + Detail-Popup) und Upload-/Download-Monitor, öffentliche IP
│  ├─ Volume/                 Lautstärke: Mausrad, Regler-Popup, Stummschalten; VolumeOverlay: Anzeige nach Tastenkürzeln
│  └─ Timers/                 Timer, Stoppuhr (samt Popups) und Countdown
├─ Audio/                     Core Audio per COM: Geräte auflisten, Standardgerät setzen, Änderungen melden; VolumeService: Endpunkt-Lautstärke (IAudioEndpointVolume); FeedbackTones: Signaltöne beim Stummschalten des Mikrofons
├─ Network/                   NetworkStatusService (Verbindung, WLAN, Konnektivität), NetworkTrafficMonitor (Byte-Zähler), PublicIpService (ipify), Interop (iphlpapi, wlanapi, Netzwerkliste)
├─ Media/                     MediaSessionService: Windows-Mediensteuerung (GlobalSystemMediaTransportControls) direkt über die WinRT-Vtables, ohne Zusatzpakete; gewählte Quelle statt der „aktuellen“ von Windows; MediaSourceNames: Anzeigename, bei Browsern der Dienst aus dem Fenstertitel
├─ Timing/                    TimerService: Zustand aller Timer und Stoppuhren, ein Zeitgeber auf den nächsten Ablauf
├─ ClipboardHistory/          ClipboardHistoryService: Verlauf der Zwischenablage (AddClipboardFormatListener)
├─ Weather/                   WeatherService: Open-Meteo (Ortssuche, Vorhersage), WMO-Wettercodes
├─ Peripherals/               DeviceBatteryService: Akkustände; BluetoothBattery (SetupAPI), RazerHid (HID-Feature-Reports)
├─ Gaming/                    FPS: PresentTracer (ETW-Echtzeitsitzung „Veyra FPS“), FpsService (FPS, Frametime, 1 % Low), FpsPermission (Gruppe, UAC)
├─ Sensors/                   Systemwerte ohne Zusatzsoftware: GetSystemTimes, GlobalMemoryStatusEx, PDH (GPU), DXGI, D3DKMT (GPU-Temperatur, -Takt, -Lüfter), GetSystemPowerStatus (Akku), GetDiskFreeSpaceEx (Laufwerke)
├─ Updates/                   Update-Prüfung über GitHub Releases, Update-Fenster, Installation
├─ Assets/                    App-Icon und Logo (erzeugt aus assets/branding)
├─ Profiles/                  Layout-Profile: Profilauswahl als Overlay, globales Tastenkürzel, Umschalten (ProfileSwitcher)
├─ Fullscreen/                Vollbild-Erkennung
│  ├─ FullscreenDetector.cs   Ereignisgesteuerte Heuristik mit Entprellung
│  └─ WindowInspector.cs      Fenster-Abfragen (Klasse, Prozess, Cloaking, Stile)
├─ Monitors/                  Monitorverwaltung (EnumDisplayMonitors, DPI, Bildwiederholrate), Monitornamen (DisplayNames), BrightnessService (DDC/CI), NightLight (Gamma-Tabelle)
├─ Configuration/             AppSettings (Modell + Validierung), SettingsService (Laden/Speichern/Live-Reload), ProfileOperations, AutoStart
├─ SettingsUI/                Einstellungsfenster (MVVM), Auswahllisten (SettingsOptions), Farbwähler (ColorField)
├─ Interop/                   P/Invoke-Deklarationen, WinEvent-Hook-Wrapper, TaskbarList (Fenster bei der Taskleiste als Vollbild melden), PasteHelper (Einfügen per SendInput)
└─ Core/                      Logging, Einzelinstanz, MVVM-Basisklassen, Farb-Hilfen

assets/branding/              Original-Logos (logo.png, logo_name.png) und README-Banner
assets/screenshots/           Bilder der GitHub-Startseite (README)
installer/                    Inno-Setup-Skript (Veyra.iss) und Assistenten-Bilder
build/                        release.ps1 (Update-Paket, Installer, GitHub-Release), branding.ps1 (Grafiken)
```

Bewusst flach gehalten: keine DI-Container, keine Service-Locator, keine Abstraktion ohne zweiten Anwendungsfall. Die einzige Schnittstelle ist `IBarWidget`, weil dort tatsächlich viele Implementierungen folgen.

### Eingesetzte Win32-APIs

| Zweck | API | Begründung |
|---|---|---|
| Nicht in Alt+Tab / Taskleiste, kein Fokusdiebstahl | `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE → MA_NOACTIVATE`, WPF `ShowActivated=False` | Fläche wird nie aktiviert, Klicks gehen nicht auf Kosten des aktiven Fensters |
| Always on Top | `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` | wird bei jedem Vordergrundwechsel erneut gesetzt (billig), damit andere Topmost-Fenster die Fläche nicht dauerhaft verdecken |
| Positionierung | `SetWindowPos` in physischen Pixeln | Per-Monitor-DPI-v2 (Manifest) → keine WPF-DIP-Umrechnungsfehler bei gemischten Skalierungen |
| Multi-Monitor / DPI | `EnumDisplayMonitors`, `GetMonitorInfo`, `GetDpiForMonitor`, `EnumDisplaySettings`, `WM_DISPLAYCHANGE`, `WM_DPICHANGED` | Monitore werden über den Gerätenamen identifiziert; Handles werden nach jeder Änderung neu ermittelt |
| Vollbild-Erkennung | `SetWinEventHook` (Foreground, Minimize, MoveSizeEnd), `RegisterShellHookWindow`, `ABN_FULLSCREENAPP`, `SHQueryUserNotificationState`, `GetTopWindow`/`GetWindow` (Z-Reihenfolge), `DwmGetWindowAttribute(DWMWA_CLOAKED)` | ereignisbasiert, siehe unten |
| Reservierter Bereich | `SHAppBarMessage` (`ABM_NEW/QUERYPOS/SETPOS/REMOVE`) | Windows rechnet den Arbeitsbereich selbst um |
| Explorer-Neustart | `RegisterWindowMessage("TaskbarCreated")` | AppBar neu registrieren, neu positionieren |
| Sperren/Entsperren, Standby | `WTSRegisterSessionNotification`, `WM_POWERBROADCAST` | nach Entsperren/Aufwachen neu positionieren und neu bewerten |
| Systemzeit/Zeitzone | `WM_TIMECHANGE` | Uhr sofort aktualisieren |
| Fenster-Optik | `DwmSetWindowAttribute` (keine runden Ecken, von „Desktop anzeigen“-Peek ausgenommen) | |
| Lautstärke | `IAudioEndpointVolume` (`SetMasterVolumeLevelScalar`, `SetMute`, `RegisterControlChangeNotify`) | jede Änderung – auch durch Lautstärketasten oder andere Programme – kommt als Rückruf, kein Abfragen |
| Lautstärke je App | `IAudioSessionManager2` (`GetSessionEnumerator`, `RegisterSessionNotification`), `IAudioSessionControl2`, `ISimpleAudioVolume`, `IAudioSessionEvents`; Namen über `SHLoadIndirectString` bzw. Dateibeschreibung, Symbole über `ExtractIconEx` | nur solange das Regler-Popup offen ist; neue Apps und Änderungen im Windows-Mixer kommen als Ereignis |
| Tastenkürzel | `RegisterHotKey` (mit `MOD_NOREPEAT`, außer bei Lauter/Leiser) | Audio-Widgets: `Hotkey`; Lautstärke, Timer, Stoppuhr: `Hotkeys` je Aktion; wirken auch bei ausgeblendeter Fläche |
| Netzwerkverbindung | `GetBestInterface` (Schnittstelle der Standardroute), `NotifyAddrChange` (über `NetworkChange`), `NotifyNetworkConnectivityHintChange`, `GetAdaptersAddresses` (über `NetworkInterface`) | ereignisbasiert; Internetzugang wie in der Windows-Taskleiste |
| WLAN | `WlanQueryInterface` (SSID, Signal, Raten, Standard, Kanal), `WlanRegisterNotification` (Verbindung, Signaländerung) | ab Windows 11 24H2 nur mit Standortzugriff für Desktop-Apps – sonst Name aus der Netzwerkliste (`INetworkListManager`, COM) |
| Datenrate | `GetIfTable2` (ein Aufruf je Messung, Filter-Schnittstellen werden nicht doppelt gezählt) | nur solange ein sichtbares Widget misst; Intervall = kürzestes angefordertes |
| Benachrichtigung, Signalton | `Shell_NotifyIcon` (`NIF_INFO`, Symbol nur während der Anzeige), `PlaySound` mit Ereignisnamen des Soundschemas | erscheint als Windows-Benachrichtigung samt Mitteilungszentrale, respektiert „Nicht stören“; keine WinRT-/NuGet-Abhängigkeit |
| Medien | `GlobalSystemMediaTransportControlsSessionManager` über `RoGetActivationFactory` und direkte Vtable-Aufrufe (Plätze laut `Windows.Media.winmd`), Cover per `CreateStreamOverRandomAccessStream` | keine CsWinRT-/SDK-Abhängigkeit; Abfrage einmal pro Sekunde auf einem eigenen MTA-Thread, nur solange ein Medien-Widget läuft, Cover nur bei Titelwechsel |
| Akku, Laufwerke | `GetSystemPowerStatus`, `GetDiskFreeSpaceEx`, `GetDriveType` | Akku im Sekundentakt des `SystemMonitor`, Laufwerke höchstens alle 30 s; Netzlaufwerke werden nicht abgefragt |
| Zwischenablage | `AddClipboardFormatListener` (`WM_CLIPBOARDUPDATE`), Formate `ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory`, `Clipboard Viewer Ignore`; Einfügen: `SetForegroundWindow` + `SendInput` (Strg + V) | ereignisbasiert; vertrauliche Inhalte (Passwortmanager) werden übersprungen; Verlauf nur im Arbeitsspeicher, Bilder höchstens 5 |
| Geräte-Akku Bluetooth | `SetupDiGetClassDevs` + `SetupDiGetDeviceProperty` (`DEVPKEY_Bluetooth_Battery` = `{104EA319-6EE2-4701-BD47-8DDBF425BBE5},2`), Container-ID, `CM_Get_DevNode_Status` (`DN_DEVICE_DISCONNECTED`) | derselbe Wert wie unter Einstellungen → Bluetooth; getrennte Geräte werden ausgelassen |
| Geräte-Akku Razer | HID (`HidD_SetFeature`/`HidD_GetFeature`, 90-Byte-Report, Klasse `0x07`, Befehle `0x80` Akkustand, `0x84` Laden), Gerät ohne Lese-/Schreibrecht geöffnet | nur Lese-Befehle; Transaktions-ID je Gerät ausprobiert und gemerkt; Status `0x05` (kein Akku) → 30 min Ruhe, `0x04` (Gerät aus oder per Bluetooth verbunden) → 2 min; ein Lesevorgang gleichzeitig, im Hintergrund alle 60 s |
| Helligkeit | dxva2 (`GetPhysicalMonitorsFromHMONITOR`, `GetMonitorBrightness`, `SetMonitorBrightness`), Laptop: `IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS` auf `\\.\LCD`; Monitornamen über `QueryDisplayConfig`/`DisplayConfigGetDeviceInfo` | DDC/CI braucht rund 50 ms je Befehl → eigener Thread, je Monitor wird nur der letzte Zielwert gesendet; gleich heißende Monitore werden durchnummeriert |
| Nachtmodus | `GetDeviceGammaRamp`/`SetDeviceGammaRamp` je Monitor | Farbtemperatur → Gamma-Tabelle; lehnt Windows sie ab, wird in 200-K-Schritten wärmer begrenzt; ursprüngliche Tabelle wird beim Ausschalten, Beenden und im Absturzpfad zurückgeschrieben; nach Anzeigeänderungen, Entsperren und alle 2 min erneut gesetzt |
| Wetter, öffentliche IP | HTTPS: `api.open-meteo.com`, `geocoding-api.open-meteo.com`; `api.ipify.org` (IPv4), `api6.ipify.org` (IPv6); `NetworkChange.NetworkAddressChanged` | nur solange ein Widget läuft; Wetter wird 5 min zwischengespeichert (mehrere Widgets, gleicher Ort) |
| FPS | ETW-Echtzeitsitzung (`StartTrace`, `EnableTraceEx2`, `OpenTrace`/`ProcessTrace`): Microsoft-Windows-DXGI `Present_Start` (ID 42), Microsoft-Windows-D3D9 `Present_Start` (ID 1); gezählt je Prozess und Swapchain (erstes Feld des Ereignisses) | ohne Adminrechte nur für Mitglieder von S-1-5-32-559 („Leistungsprotokollbenutzer“), wirksam ab der nächsten Anmeldung; gemessen wird das Vordergrundprogramm, zweimal pro Sekunde ausgewertet, nur solange ein FPS-Widget läuft; eine verwaiste Sitzung (Absturz) wird beim Start beendet. Aufnahme in die Gruppe: `NetLocalGroupAddMembers` in einer per UAC erhöhten Instanz (`--grant-fps`) |

---

## Vollbild-Erkennung

**Auslöser – kein hochfrequentes Polling:**

- WinEvents: Vordergrundwechsel, Minimieren/Wiederherstellen, Ende von Verschieben/Größenänderung
- Shell-Hook: Fensteraktivierung, „Rude App“-Aktivierung (Vollbild), Fenster geschlossen, Monitorwechsel
- `ABN_FULLSCREENAPP` von Explorer (die Fläche ist dafür immer als AppBar registriert – ohne Bereich zu reservieren)
- eine billige Sicherheitsprüfung alle 2 s (konfigurierbar, abschaltbar) für Übergänge ohne Systemereignis

Ereignis-Salven werden in einem 40-ms-Fenster zu einer Auswertung zusammengefasst.

**Entscheidung** (nur für den Monitor der Fläche):

1. Meldet die Shell `QUNS_RUNNING_D3D_FULL_SCREEN` und liegt das Vordergrundfenster auf diesem Monitor → **exklusives Vollbild**.
2. Sonst wird die Z-Reihenfolge von oben durchlaufen; das erste echte Anwendungsfenster auf dem Monitor entscheidet. Übersprungen werden: eigene Fläche, unsichtbare/minimierte/gecloakte Fenster (andere virtuelle Desktops), Click-through-Overlays (`WS_EX_TRANSPARENT`, z. B. GPU-Overlays), Tool- und No-Activate-Fenster, vollständig transparente Layered-Fenster, Shell-Oberflächen (Alt+Tab, Task-Ansicht, Startmenü, Benachrichtigungen) sowie Topmost-Fenster, die selbst kein Vollbild sind (z. B. Bild-im-Bild). Erreicht die Suche den Desktop → kein Vollbild.
3. Ein Fenster ist **randloses Vollbild**, wenn es den gesamten Monitor überdeckt **und** keine Titelleiste hat oder sein Client-Bereich den Monitor vollständig füllt.
4. Reserviert die Fläche Bildschirmbereich, gilt auch ein rahmenloses Vordergrundfenster als randloses Vollbild, das Windows unter den reservierten Bereich geschoben bzw. auf den Arbeitsbereich verkleinert hat (volle Breite, reicht bis zum unteren Rand des Arbeitsbereichs, beginnt höchstens knapp unter der Fläche). Das Ergebnis trägt dann `Squeezed = true`.

**Vollbild und reservierter Bildschirmbereich** (`BarController`):

- **Fläche im Spiel ausgeblendet:** Solange ein erkanntes Vollbildfenster lebt (auch per Alt+Tab im Hintergrund), wird der Arbeitsbereich nicht verändert – sonst schöbe Windows das Spiel unter die Fläche. Die Reservierung wird erst nachgeholt, wenn das Fenster geschlossen oder minimiert ist. Wurde ein Spiel bereits verdrängt (`Squeezed`), gibt die Fläche beim Ausblenden die Reservierung frei und legt das Fenster wieder auf den ganzen Monitor.
- **Fläche im Spiel sichtbar** (`HideOnBorderlessFullscreen = false`) **mit Reservierung:** Randlose Spiele ignorieren den Arbeitsbereich. Die Fläche legt das Spielfenster deshalb selbst unter sich (Monitorhöhe minus Fläche, z. B. 1920 × 1040 bei 40 px) und prüft das bei jeder Auswertung erneut, weil Spiele ihre Größe gern zurücksetzen (höchstens 3 Korrekturen in 10 s). Weil das Fenster den Monitor dann nicht mehr abdeckt, meldet die Fläche es per `ITaskbarList2::MarkFullscreenWindow` als Vollbild – so tritt die Windows-Taskleiste wie bei jedem Vollbildspiel zurück. Endet die Reservierung – abgeschaltet, Fläche entfernt, neu aufgebaut (z. B. Profilwechsel von fixiert zu dynamisch) oder beendet –, bekommt das Spiel den ganzen Monitor zurück und die Markierung wird entfernt; aber nur, solange es noch so liegt, wie die Fläche es hingelegt hat.
- Holt sich ein Spiel selbst als Topmost nach vorne, legt sich die sichtbare Fläche wieder darüber (höchstens 5× pro Sekunde, damit es kein endloses Hin und Her gibt). Exklusives Vollbild lässt kein Fenster darüber zu.

**Warum reine Größenvergleiche nicht reichen:** Ein maximiertes Fenster ist bei automatisch ausgeblendeter Windows-Taskleiste genauso groß wie ein Vollbildfenster. Deshalb gilt ein *maximiertes Fenster mit Größenrahmen* (`IsZoomed` + `WS_THICKFRAME`) grundsätzlich nicht als Vollbild – außer sein Client-Bereich füllt den Monitor, es ist im Vordergrund **und** die Shell meldet `QUNS_BUSY` (deckt Browser ab, die für F11 maximiert bleiben).

**Flackerschutz:** Ausblenden passiert sofort (die Fläche liegt nie sichtbar über einem Spiel), Einblenden erst, wenn „kein Vollbild“ 300 ms stabil ist. Alt+Tab-Umschalter und Startmenü werden übersprungen, sodass das darunterliegende Spiel weiter erkannt wird.

**Gaming-Verhalten:** Während die Fläche ausgeblendet ist, sind alle Widgets angehalten (Uhr-Timer, Messungen, Netzwerk- und Lautstärke-Benachrichtigungen abgemeldet) – nur ein laufender Timer meldet seinen Ablauf weiterhin über einen einzigen Zeitgeber; es laufen nur noch die Ereignis-Hooks und die 2-s-Sicherheitsprüfung (wenige Mikrosekunden). Mit Software-Rendering (Standard) nutzt die Fläche die GPU überhaupt nicht.

### Bekannte Einschränkungen

- **Monitor bei exklusivem Vollbild:** `SHQueryUserNotificationState` sagt nicht, *auf welchem* Monitor das Vollbild läuft. Die Fläche nimmt den Monitor des Vordergrundfensters an.
- **Spiele mit `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE`-Hauptfenster** (sehr selten) werden übersprungen → in die Liste *„Immer als Vollbild behandeln“* eintragen.
- **Apps, die für ihr Vollbild maximiert bleiben, ohne dass die Shell `QUNS_BUSY` meldet**, werden nicht erkannt → ebenfalls über *„Immer als Vollbild behandeln“* lösbar (dann gilt das Fenster als Vollbild, sobald es den Arbeitsbereich ausfüllt – also auch im normal maximierten Zustand).
- **Fehlerkennungen** (z. B. ein randloses, monitorgroßes Tool) → in *„Nie als Vollbild behandeln“* eintragen.
- Prozessnamen von Prozessen mit höherer Integritätsstufe oder geschützten Anti-Cheat-Prozessen sind evtl. nicht lesbar; die geometrische Erkennung funktioniert trotzdem, nur die Ausnahmelisten greifen dann nicht.
- Vollbild auf einem **anderen** Monitor blendet die Fläche bewusst nicht aus.
- Übergänge ohne jedes Systemereignis (z. B. F11 im Browser ohne Fokuswechsel) erkennt meist `ABN_FULLSCREENAPP`; andernfalls greift spätestens die Sicherheitsprüfung nach ≤ 2 s.

---

## Widget-Layout

Feste Widgets (Uhr) sitzen in *Links*, *Mitte* oder *Rechts* mit Randabstand und Mindestabstand. Freie Widgets werden per Drag & Drop platziert; ihre Position wird als Anteil der Flächenbreite gespeichert.

**Layout-Regeln** (`BarLayoutPanel`):

1. Feste Widgets werden zuerst platziert. Ihr Platz plus Mindestabstand ist gesperrt, kein freies Widget kann ihn belegen oder das feste Widget verschieben. Mehrere feste Widgets im selben Bereich stehen nebeneinander.
2. Freie Widgets kommen in der Reihenfolge ihrer Wunschposition in die nächstgelegene freie Lücke, die groß genug ist. Dadurch können sie sich nie überlappen. Wächst ein Widget (z. B. ein neues Gerät), rückt es in die nächste passende Lücke.
3. Ein gezogenes Widget wird zuletzt platziert. Es weicht allen anderen aus und verdrängt niemanden. Beim Loslassen wird die tatsächlich erreichte Position gespeichert.
4. Passt ein freies Widget nirgends mehr hin, wird es ausgeblendet statt zu überlappen.

**Freie Platzierung:** Gespeichert wird der feste Abstand der linken Kante und der Oberkante zum Flächenrand in geräteunabhängigen Pixeln (`FreeOffsetLeft`/`FreeOffsetTop`). Wird die Fläche größer oder kleiner, bleiben die Widgets deshalb stehen; passt eines nicht mehr hinein, wird es am Rand gehalten und kehrt beim Vergrößern an seinen Platz zurück. Wird ein Widget breiter (laufender Timer, zusätzliches Gerät), wächst es nach rechts bzw. nach unten. Widgets ohne festen Abstand – neu hinzugefügt, nach „Freie Positionen zurücksetzen“ oder aus älteren Konfigurationen mit Anteilen der Flächengröße (`FreeLeft`/`FreeX`/`FreeY`) – werden nach dem ersten Anzeigen dort festgehalten, wo sie stehen, und gesammelt in einem Speichervorgang übernommen.

**Position sperren** (`LockPosition`) gilt in jedem Modus: Widgets lassen sich nicht mehr ziehen (Klicks funktionieren weiter); im dynamischen Modus ist zusätzlich das Fenster gegen Verschieben und Größenänderung gesperrt.

**Einzelne Widgets sperren** (`WidgetEntry.Locked`) geht nur über das Kontextmenü: Beim Öffnen sucht die Fläche das verschiebbare Widget unter dem Mauszeiger (`BarLayoutPanel.MovableWidgetIdAt`) und fügt „„Name“ sperren/entsperren“ hinter „Position sperren“ ein. Ein gesperrtes Widget lässt sich nicht ziehen; im dynamischen Modus verschiebt ein Ziehen darauf auch nicht die Fläche (Strg + Ziehen schon). Das Einstellungsfenster übernimmt den Sperrzustand immer aus der laufenden Konfiguration.

Audiogeräte kommen aus Windows Core Audio (`IMMDeviceEnumerator`, Änderungen per `IMMNotificationClient`); das Standardgerät wird über die seit Windows 7 stabile Schnittstelle `IPolicyConfig` gesetzt. Symbole: [AudioIcons.cs](../src/Veyra/Audio/AudioIcons.cs) (Segoe Fluent Icons plus eigene Vektorpfade, automatische Wahl nach Gerätename und Formfaktor).

---

## Update-Dienst

Abfrage der Release-Liste `api.github.com/repos/xLucipher/Veyra/releases` (ohne Entwürfe/Vorabversionen), Vergleich der neuesten Version mit der Assembly-Version, die Neuerungen aller übersprungenen Versionen werden zusammen angezeigt, Download des `*-win-x64.zip`-Anhangs mit Größen- und SHA-256-Prüfung, Austausch per Skript nach `--exit` und Neustart. Protokoll: `%TEMP%\Veyra-update\install-update.log`. In Debug-Builds lässt sich die API-Adresse für Tests mit `VEYRA_UPDATE_API` umbiegen (`VEYRA_UPDATE_DELAY_S` verkürzt die Startverzögerung).

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

Die Bilder der README und der Versionshinweise liegen in [assets/screenshots](../assets/screenshots). Die neuen Bilder (`readme-*`, `release-1.4-*`, `gaming`, `popups`) sind 1920 px breit und 500–680 px hoch, damit sie auf GitHub in voller Breite nicht zu viel Platz einnehmen. Widgets und Popups darin sind echte Veyra-Ansichten aus dem jeweiligen Build, gerendert mit Beispieldaten (FPS, Zwischenablage, einzelne Geräte) bzw. echten Daten (Wetter, Uhrzeit, Systemwerte). `fixed.png`, `dynamic.png` und `mic.png` sind Bildschirmfotos aus 1.4-Vorabversionen.

---

## Konfiguration

Datei: `%AppData%\Veyra\settings.json` – wird beim ersten Start mit Standardwerten angelegt. Kommentare (`//`) und abschließende Kommas sind erlaubt. Änderungen (über das Einstellungsfenster **oder** direkt in der Datei) werden ohne Neustart übernommen.

**Einstellungsfenster:** aufgebaut nach der Hierarchie Profil → Fläche → Widget. Die Seitenleiste zeigt oben das aktive Profil (Wechsel wirkt sofort), darunter die Flächen dieses Profils und unten die allgemeinen Bereiche (Profile verwalten, Vollbild & Spiele, Audiogeräte, System & Updates). Eine Fläche hat die Tabs *Aussehen*, *Widgets* (Liste links, Einstellungen des gewählten Widgets rechts – bei Uhren samt Format, Schrift und Datum) und *Position & Verhalten* (Modus, Monitor, Größe, Vordergrund, Reservierung, Ebene, Sperre, Anordnung). Alles unter einer Fläche gilt nur für sie, alles unter „Allgemein“ für alle. Dunkles Fluent-Design (WPF-`ThemeMode="Dark"`, in .NET enthalten – keine Zusatzpakete). Formate, Sprache, Schriftart/-gewicht, Verzögerungen und Monitor werden per Dropdown mit Live-Vorschau gewählt; Farben über eine Palette mit Farbfeldern oder als eigener Hex-Wert; Größen per Auswahl oder eigenem Wert; Vollbild-Ausnahmen aus der Liste laufender Programme.

**Profile:** `Profiles[]` (`Id`, `Name`, `Bars[]`) und `ActiveProfileId` auf oberster Ebene. Die Flächen des aktiven Profils stehen wie gewohnt in `Bars`; sein Eintrag in `Profiles` hat leere `Bars`. Beim Wechsel (`ProfileOperations.Switch`) wandern die aktuellen Flächen in das bisher aktive Profil und die des Zielprofils nach `Bars` – der restliche Code kennt nur `Bars`. Neue Profile sind Kopien mit denselben IDs, damit beim Wechsel Flächenfenster bestehen bleiben und laufende Timer erhalten. `ProfileHotkey` (Standard `Ctrl+Alt+P`, leer = keins) öffnet die Profilauswahl (`Profiles/ProfileOverlayWindow`), angemeldet an einem eigenen Nachrichtenfenster (`ProfileHotkeyHost`), unabhängig von den Flächen. Dateien ohne Profile erhalten beim Laden das Profil „Standard“ mit den bisherigen Flächen. Wird das Profil von außen gewechselt, während das Einstellungsfenster offen ist, lädt es neu, damit „Übernehmen“ nicht die Flächen des alten Profils ins neue schreibt.

**Ältere Dateien** (eine Fläche mit `Appearance`, `Widgets` und `System.MonitorSelection` auf oberster Ebene) werden beim Laden automatisch in `Bars[0]` übernommen; ein gemeinsames `Clock` auf oberster Ebene wird in jede Uhr kopiert. Beides wird im neuen Format gespeichert.

**Fehlerhafte Datei:** Beim Start wird sie als `settings.json.invalid-<Zeitstempel>` gesichert und durch Standardwerte ersetzt; im laufenden Betrieb bleiben die bisherigen Einstellungen aktiv (Warnung im Log). Ungültige Einzelwerte (Farben, Formate, Bereiche) werden auf Standard bzw. Grenzwerte korrigiert.

**Automatische Sicherung:** Vor jedem Schreiben (`SettingsService.Write` – also bei jedem Speichern, jeder Migration und beim Ersetzen einer fehlerhaften Datei) wird die bisherige `settings.json` nach `%AppData%\Veyra\backups\settings-<Zeitstempel>.json` kopiert, sofern sich der Inhalt ändert. Die letzten 10 Fassungen bleiben erhalten, ältere werden gelöscht. Ein Fehler beim Sichern verhindert das Speichern nie (nur Warnung im Log). Wiederherstellen: Veyra beenden, gewünschte Sicherung als `settings.json` zurückkopieren.

| Bereich | Schlüssel | Standard | Bedeutung |
|---|---|---|---|
| Bars[] | `Id`, `Name` | *(automatisch)*, `Fläche N` | eine Fläche = ein eigenes Fenster; es gibt immer mindestens eine |
| | `MonitorSelection` | `Primary` | oder Gerätename, z. B. `\\.\DISPLAY2` (Einstellungen → Fläche → Position & Verhalten) |
| | `Widgets` | Uhr + Audio | Widgets dieser Fläche (Typ, Bereich, Positionen, Geräte); Uhren mit eigenem `Clock`, Audio-Widgets mit `IconColor`/`LabelColor` (leer = `ForegroundColor` der Fläche), `IndicatorColor` (Strich unter dem Standardgerät, leer = `#3DAEE9`) und `Hotkey` (globales Tastenkürzel zum Durchschalten der Geräte, z. B. `Ctrl+Shift+I`); Mess-Widgets (`Cpu`, `Gpu`, `Ram`, `GpuTemperature`) mit `Metric` (`Style`: `Value`/`Bar`/`Graph`, `ShowCaption`, `WarnColors`, `ShowAmount` für RAM in GB, `Gpu` = Name der Grafikkarte, leer = automatisch) sowie `IconColor` (Bezeichnung, leer = `ForegroundColor` der Fläche, unabhängig vom Wert), `LabelColor` (Wert) und `IndicatorColor` (Balken/Verlauf); `Network` mit `Network` (`ShowName`, `ShowSignal`, `ShowSettingsButton`); `Volume` mit `Volume` (`Flow`: `Output`/`Input`, `ShowPercent`, `ShowBar`, `WheelStep` in %, `RightClickMutes`, `ShowApps` = Lautstärke je App im Regler, nur Wiedergabe; nur Mikrofon: `HighlightMuted`, `MuteSound`; `ShowOverlay` = Anzeige nach Tastenkürzeln); `Timer` mit `Timer` (`Timers[]` aus `Id`, `Name`, `DurationSeconds`; `ShowAllActive`, `ShowName`, `ShowProgress`, `Notify`, `Sound`: `Alarm`/`Notification`/`Reminder`/`None`); `Stopwatch` mit `Stopwatch` (`ShowMilliseconds`, `AlwaysShowHours`, `EnableLaps`); `NetworkTraffic` mit `Traffic` (`Direction`: `Both`/`Download`/`Upload`, `Stacked`, `ShowGraph`, `UseBits`, `Adapter` = Adapter-GUID, leer = alle, `IntervalMs` 250–10000). Farben dieser Widgets: `IconColor` (Symbol; beim Datenraten-Monitor Upload, leer = `#E8A23B`), `LabelColor` (Text; beim Timer nur die Restzeit, der Name folgt der Flächenfarbe), `IndicatorColor` (Balken, Timer-Fortschritt bzw. Download). Die blasse Hintergrundspur hinter Balken und Verläufen folgt immer der Flächenfarbe. Tastenkürzel dieser Widgets in `Hotkeys` je Aktion: Lautstärke `VolumeUp`, `VolumeDown`, `Mute`; Timer `StartPause`, `Reset`; Stoppuhr `StartPause`, `Lap`, `Reset` (z. B. `{ "VolumeUp": "Ctrl+Alt+Up" }`). Laufende Timer und Stoppuhren werden nicht gespeichert |
| Bars[].Appearance | `Mode` | `FullWidth` | `FullWidth` = fixiert (oben, volle Breite) · `Floating` = dynamisch (frei beweglich) |
| | `Height` | `48` | Höhe in geräteunabhängigen Pixeln (×1,5 bei 150 % Skalierung) |
| | `BackgroundColor` | `#202127` | `#RGB`, `#RRGGBB`, `#AARRGGBB` |
| | `ForegroundColor` / `FontFamily` | `#FFFFFF` / `Segoe UI` | Standard für alle Widgets |
| | `BorderColor` / `BorderThickness` | `#2E3038` / `1` | fixiert: Trennlinie unten, dynamisch: Rahmen rundherum (0 = aus) |
| | `Width` / `FloatingHeight` | `1200` / `48` | dynamisch: Größe in px |
| | `FloatingLeft` / `FloatingTop` | *(leer)* / *(leer)* | dynamisch: Position relativ zum Monitor; links leer = zentriert, oben leer = `MarginTop` |
| | `Layer` | `Topmost` | dynamisch: `Topmost`, `Normal` oder `Desktop` (hinter allen Fenstern) |
| | `LockPosition` | `false` | Widgets nicht verschiebbar (jeder Modus); dynamisch zusätzlich Verschieben/Größenänderung der Fläche gesperrt |
| | `FreeWidgetPlacement` | `false` | Widgets (auch die Uhr) frei in beide Richtungen platzieren; Positionen je Widget in `FreeOffsetLeft`/`FreeOffsetTop` (Abstand zum linken bzw. oberen Rand in px; null = noch nicht festgehalten). `FreeLeft`/`FreeX`/`FreeY` (Anteile der Flächengröße) stammen aus älteren Versionen und werden übernommen |
| | `BackgroundOpacity` | `1` | Deckkraft 0–1 in beiden Modi (0 = durchsichtig; dynamisch gehen Klicks durch, fixiert bleibt die Fläche klickbar). Fixiert unter 1 wird das Fenster transparent erzeugt |
| | `CornerRadius` | `12` | dynamisch: Eckenradius |
| | `AlwaysOnTop` | `true` | fixiert: immer über allen Fenstern (je Fläche; ältere Dateien übernehmen den früheren globalen Wert aus `Behavior`) |
| | `ReserveScreenSpace` | `true` | fixiert: `true` = AppBar (maximierte Fenster beginnen unter der Fläche; bei sichtbarer Fläche im randlosen Spiel wird auch das Spiel darunter gelegt), `false` = Overlay – je Fläche |
| Behavior | `AutoHide`, `AutoHideDelayMs` | `false`, `400` | *geplant, in V1 ohne Wirkung* |
| | `HideOnFullscreen` | `true` | bei exklusivem Vollbild ausblenden |
| | `HideOnBorderlessFullscreen` | `true` | bei randlosem Vollbild ausblenden |
| | `FullscreenShowDelayMs` | `300` | Entprellung beim Wiedereinblenden |
| | `FullscreenSafetyCheckIntervalMs` | `2000` | Sicherheitsprüfung, `0` = aus |
| | `FullscreenExcludedProcesses` | `[]` | z. B. `["vlc"]` – nie Vollbild |
| | `FullscreenForcedProcesses` | `[]` | z. B. `["MeinSpiel"]` – Vollbild, sobald Arbeitsbereich gefüllt |
| Bars[].Widgets[].Clock | `TimeFormat` | `HH:mm:ss` | allein maßgeblich: `HH:mm` = ohne Sekunden, `HH:mm:ss` = mit; 12 h: `h:mm tt` (fehlen der Sprache AM/PM-Bezeichner, wird „AM“/„PM“ verwendet) |
| | `ShowSeconds` | *(abgeleitet)* | wird beim Speichern aus `TimeFormat` gesetzt, nur informativ |
| | `ShowDate` / `DateFormat` | `false` / `dddd, d. MMMM yyyy` | |
| | `ShowWeekNumber` / `WeekNumberFormat` | `false` / `KW {0}` | ISO-8601-Kalenderwoche |
| | `FontSize`, `FontFamily`, `FontWeight`, `FontColor` | `20`, *(leer = Standard)*, `SemiBold`, `#FFFFFF` | `FontWeight`: Normal, Medium, SemiBold, Bold |
| | `DateFontSize`, `DateFontColor` | `11`, `#B4B7C2` | |
| | `Culture` | *(leer = System)* | z. B. `de-DE` |
| Bars[].Widgets[].Metric | `Style`, `ShowCaption`, `WarnColors` | `Bar`, `true`, `true` | Mess-Widgets (CPU, GPU, RAM, Temperatur, Akku, Laufwerk) |
| | `ShowAmount` | `false` | RAM: GB statt Prozent · Laufwerk: freier Speicher · Akku: Restlaufzeit |
| | `Gpu` / `Drive` | *(leer = automatisch)* / `C:\` | Grafikkarte bzw. Laufwerk |
| Bars[].Widgets[].Image | `Path`, `Height`, `Width`, `Opacity`, `CornerRadius`, `ClickTarget` | –, `32`, `0` (= automatisch), `100`, `0`, *(leer)* | Bild aus einer Datei; dekodiert nur in benötigter Größe |
| Bars[].Widgets[].Note | `Text`, `FontSize`, `FontFamily`, `Bold`, `MaxWidth`, `Alignment`, `Background`, `EditOnClick` | –, `13`, *(leer = Fläche)*, `false`, `260`, `Left`, *(leer = keiner)*, `true` | Notiz; Farbe in `LabelColor` |
| Bars[].Widgets[].Media | `ShowCover`, `ShowArtist`, `ShowControls`, `MaxTextWidth`, `HideWhenIdle` | `true`, `true`, `true`, `200`, `false` | Medien-Widget; Tastenkürzel `PlayPause`, `NextTrack`, `PreviousTrack` |
| Bars[].Widgets[].Countdown | `Title`, `Target`, `Style`, `ShowTitle`, `DoneText` | `Countdown`, heute + 7 Tage, `Days`, `true`, `Geschafft!` | `Style`: `Days`, `DaysHours`, `Clock`; Farben in `IconColor` (Titel) / `LabelColor` (Restzeit) |
| Bars[].Widgets[].Separator | `Style`, `Horizontal`, `Length`, `Thickness`, `Padding` | `Line`, `false`, `20`, `1`, `6` | `Style`: `Line`, `Dot`, `Space`; Farbe in `IconColor` |
| Bars[].Widgets[] | `Locked` | `false` | nur dieses Widget nicht verschiebbar (Kontextmenü); wird nur geschrieben, wenn gesetzt |
| Bars[].Widgets[].Clipboard | `MaxItems`, `PasteOnSelect`, `KeepImages`, `ShowCount`, `Pinned` | `25`, `true`, `true`, `false`, `[]` | Zwischenablage; `Pinned` = angeheftete Texte (höchstens 50); Tastenkürzel `ShowClipboard` |
| Bars[].Widgets[].WorldClock | `Zones[]` (`Id`, `TimeZoneId`, `Label`), `TimeFormat`, `ShowLabels`, `ShowAllInBar`, `ShowDayOffset` | New York, Tokio, `HH:mm`, `true`, `true`, `true` | Weltuhr; `TimeZoneId` = Windows-Zeitzonen-ID, z. B. `Eastern Standard Time`; Farben: `IconColor` Ortsnamen, `LabelColor` Uhrzeiten |
| Bars[].Widgets[].Launcher | `Items[]` (`Id`, `Path`, `Arguments`, `Name`), `ShowNames`, `IconSize` | –, `false`, `20` | Schnellstart; `Path` = Programm, Datei, Ordner oder Webadresse |
| Bars[].Widgets[].Weather | `LocationName`, `Latitude`, `Longitude`, `Unit`, `WindUnit`, `IntervalMinutes`, `ShowCondition`, `ShowHighLow`, `ShowLocation` | –, –, –, `Celsius`, `KilometersPerHour`, `30`, `false`, `false`, `false` | Wetter; `WindUnit`: `KilometersPerHour`, `MetersPerSecond`, `MilesPerHour` |
| Bars[].Widgets[].DeviceBattery | `Bluetooth`, `Razer`, `ShowNames`, `WarnLevel`, `NotifyLow` | `true`, `true`, `false`, `20`, `true` | Geräte-Akku; gezeigte Geräte in `Devices` (`bt:Name` bzw. `razer:Name`, leer = alle) |
| Bars[].Widgets[].PublicIp | `Show`, `ShowLabel`, `Mask`, `IntervalMinutes` | `IPv4`, `true`, `false`, `15` | öffentliche IP; `Show`: `IPv4`, `IPv6`, `Both` |
| Bars[].Widgets[].Brightness | `WheelStep`, `ShowPercent`, `NightTemperature`, `NightMode`, `NightSchedule`, `NightFrom`, `NightTo` | `5`, `true`, `3400`, `false`, `false`, `21:00`, `07:00` | Helligkeit & Nachtmodus; `NightTemperature` 2700–6500 K; Tastenkürzel `BrightnessUp`, `BrightnessDown`, `ToggleNightMode` |
| Bars[].Widgets[].Fps | `ShowFrameTime`, `ShowOnePercentLow`, `ShowProcess`, `WarnBelow` | `false`, `false`, `false`, `30` | FPS; unter `WarnBelow` rot, bis zum Doppelten orange, 0 = aus |
| System | `StartWithWindows` | `false` | HKCU-Run-Eintrag, ohne Adminrechte |
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
| Vollbild-App stürzt ab | Shell-Hook „Fenster zerstört“ bzw. Sicherheitsprüfung → Fläche erscheint wieder |
| Unerwartete Ausnahme | UI-Fehler werden protokolliert, die Fläche läuft weiter; bei Prozessabbruch wird eine AppBar-Reservierung noch freigegeben |

---

## Roadmap

- **Dynamischer Modus:** optional Mica/Acrylic-Hintergrund über `DWMWA_SYSTEMBACKDROP_TYPE`, Einrasten an Bildschirmrändern.
- **Auto-Hide:** Fläche außerhalb des sichtbaren Bereichs parken, Einblenden über einen 1-px-Auslöserstreifen am oberen Rand; während Vollbild deaktiviert.
- **Weitere Widgets:** `IBarWidget` implementieren, Typ in `WidgetType`/`WidgetCatalog` eintragen und in `BarController.CreateWidgets` erzeugen; fest oder frei legt `WidgetEntry.IsFixedType` fest. Kandidaten: virtuelle Desktops, Akkustand weiterer Razer-Headsets und von Logitech-Geräten (HID++).

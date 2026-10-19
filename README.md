<p align="center"><img src="assets/branding/logo_name_banner.png" alt="Veyra" width="520"></p>

<p align="center">
  Schlanke, konfigurierbare Zusatz-Taskleiste am oberen Bildschirmrand für Windows 11.<br>
  Sie ergänzt die Windows-Taskleiste, ohne sie zu verändern.
</p>

---

## Funktionen

- **Digitaluhr** exakt in der Bildschirmmitte, wahlweise mit Datum und Kalenderwoche. Ein Klick öffnet den Monatskalender. Mehrere Uhren lassen sich unabhängig voneinander gestalten (Format, Schrift, Farben, Sprache).
- **Audio-Schnellwechsel:** Buttons für Wiedergabegeräte und Mikrofone. Ein Klick macht das Gerät zum Standard, das aktive ist markiert. Pro Gerät lassen sich Symbol und Kurzname wählen, pro Widget die Farbe von Symbolen, Namen und der Markierung des aktiven Geräts sowie ein eigenes **Tastenkürzel**, das – auch im Spiel – reihum zum nächsten Gerät wechselt.
- **Systemauslastung:** Widgets für CPU, GPU, Arbeitsspeicher und GPU-Temperatur – als Zahl, mit Balken oder als Verlauf der letzten Minute, mit Warnfarben bei hoher Last. Bezeichnung (z. B. „CPU“) und Wert lassen sich getrennt einfärben. Alles direkt aus Windows gelesen, ohne Zusatzsoftware und ohne Adminrechte.
- **Netzwerk:** aktuelle Verbindung (LAN/WLAN) mit Symbol, Netzwerkname und Signalstärke; ohne Internetzugriff wird das Symbol orange. Ein Klick zeigt IP-Adressen, Gateway, DNS, Adapter und Geschwindigkeit, auf Wunsch mit Schnellzugriff auf die Windows-Netzwerkeinstellungen. Hinweis: Ab Windows 11 24H2 gibt Windows WLAN-Name und Signalstärke nur heraus, wenn Desktop-Apps auf den Standort zugreifen dürfen – sonst zeigt Veyra den Namen aus der Windows-Netzwerkliste.
- **Upload / Download:** aktuelle Datenrate in Echtzeit mit automatischer Einheit (KB/s, MB/s, GB/s oder Mbit/s), Download und Upload getrennt, nebeneinander oder untereinander, optional mit Verlaufsgrafik – für alle Adapter zusammen oder einen bestimmten, mit einstellbarem Intervall.
- **Lautstärke:** Systemlautstärke (oder Mikrofonpegel) als Symbol, Prozentwert und Balken. Mausrad ändert die Lautstärke, ein Klick öffnet einen kompakten Regler, Rechtsklick schaltet stumm. Änderungen über Tastatur, Windows oder andere Programme erscheinen sofort. Im Regler lässt sich zusätzlich die Lautstärke jeder App einzeln einstellen und stummschalten, wie im Windows-Lautstärkemixer.
- **Timer:** beliebig viele Timer mit eigener Dauer – starten, pausieren, fortsetzen, zurücksetzen, direkt im Popup anlegen und bearbeiten. Die Restzeit steht auf der Leiste; nach Ablauf gibt es eine Windows-Benachrichtigung und auf Wunsch einen Signalton, auch wenn die Leiste gerade bei Vollbild ausgeblendet ist.
- **Stoppuhr:** Stunden, Minuten, Sekunden und optional Millisekunden, Bedienung im Popup samt Rundenzeiten (schnellste und langsamste Runde markiert).
- **Widgets frei anordnen:** Die Uhr hat feste Positionen (links, Mitte, rechts). Alle anderen Widgets verschiebst du per Drag & Drop direkt auf der Leiste, ohne dass sie sich überlappen. Mit der **freien Platzierung** lassen sich alle Widgets, auch die Uhr, beliebig auf der Leiste ablegen, auch übereinander oder in die Ecken.
- **Automatisch ausgeblendet bei Vollbild:** bei Spielen (exklusiv und randlos), Videos und Browser-Vollbild. Danach erscheint die Leiste wieder.
- **Gaming-freundlich:** praktisch keine CPU-Last im Leerlauf, keine GPU-Nutzung, kein Fokusdiebstahl und kein Eintrag in Alt+Tab.
- **Fixiert oder dynamisch:** oben über die volle Breite angedockt – oder als frei bewegliches Fenster mit eigener Größe, abgerundeten Ecken und wählbarer Ebene (z. B. als durchsichtige Uhr im Hintergrund des Desktops). Leisten im Vordergrund bleiben auch über der Windows-Taskleiste.
- **Mehrere Leisten:** beliebig viele Leisten, jede mit eigener Darstellung, Ebene, Monitor und eigenen Widgets – z. B. eine durchsichtige Uhr auf dem Desktop und eine Audio-Leiste immer im Vordergrund.
- **Anpassbar:** Farben (inklusive Farbkreis), Hintergrund-Transparenz, Schriften, Höhe, Formate, Monitor und optional ein reservierter Bildschirmbereich. Änderungen wirken sofort.
- **Automatische Updates** über GitHub, mit Rückfrage.

## Installation

1. Unter [**Releases**](https://github.com/xLucipher/Veyra/releases/latest) die Datei `Veyra-Setup-<Version>.exe` herunterladen und starten.
2. Dem Assistenten folgen. Fehlt die benötigte **Microsoft .NET 10 Desktop Runtime**, bietet der Installer an, sie direkt von Microsoft zu installieren. Das passiert nur mit deiner Zustimmung.
3. Fertig. Veyra startet auf Wunsch direkt und bei jeder Anmeldung.

Es sind keine Administratorrechte nötig. Veyra erscheint wie jedes andere Programm im Startmenü und unter *Programme und Features*.

**Voraussetzungen:** Windows 10 (1809) oder Windows 11, 64 Bit.

## Bedienung

| Aktion | Wirkung |
|---|---|
| Rechtsklick auf die Leiste | *Einstellungen*, *Leiste neu laden*, *Beenden* |
| Klick auf Uhrzeit/Datum | Monatskalender öffnen |
| Klick auf einen Audio-Button | Gerät als Standard verwenden |
| Klick auf Netzwerk, Lautstärke, Timer oder Stoppuhr | Popup mit Details bzw. Bedienung (Esc oder Klick daneben schließt) |
| Mausrad über dem Lautstärke-Widget | Lautstärke ändern (Schrittweite einstellbar) |
| Rechtsklick auf das Lautstärke-Widget | stumm schalten bzw. Stummschaltung aufheben |
| Klick auf einen abgelaufenen Timer | Alarm beenden |
| Leertaste / R / Entf im Stoppuhr-Popup | Start/Pause / Runde / Zurücksetzen |
| Tastenkürzel von Lautstärke, Timer oder Stoppuhr | lauter/leiser (gedrückt halten wiederholt), stumm; Timer bzw. Stoppuhr starten/pausieren, Runde, zurücksetzen – global, auch im Spiel; Kombination oder einzelne Taste wie *Bild auf* (festlegen unter *Einstellungen → Widgets*) |
| Tastenkürzel eines Audio-Widgets | nächstes Gerät dieses Widgets als Standard (von links nach rechts, festlegen unter *Einstellungen → Widgets*) |
| Widget ziehen (außer Uhr) | Position auf der Leiste ändern |
| Dynamische Leiste ziehen / an den Rändern ziehen | verschieben / Größe ändern (sperrbar per Rechtsklick) |
| Strg + Ziehen (freie Platzierung) | dynamische Leiste verschieben, auch über einem Widget |
| Veyra im Startmenü erneut starten | öffnet die Einstellungen |

## Updates

Veyra sucht kurz nach dem Start und danach alle paar Stunden nach einer neuen Version. Während eines Vollbildspiels fragt Veyra nie. Gibt es ein Update, kannst du wählen:

- **Ja:** installieren, Veyra startet danach neu.
- **Später:** in 24 Stunden erneut fragen.
- **Nein:** diese Version überspringen.

Die automatische Prüfung lässt sich unter *Einstellungen → Allgemein* abschalten.

## Deinstallation

Über *Einstellungen → Apps → Installierte Apps* oder *Systemsteuerung → Programme und Features*. Veyra wird beendet, der Autostart entfernt, und du kannst wählen, ob deine Einstellungen erhalten bleiben.

---

Technische Details zu Build, Aufbau und Releases: [docs/ENTWICKLUNG.md](docs/ENTWICKLUNG.md)

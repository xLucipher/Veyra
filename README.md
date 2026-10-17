<p align="center"><img src="assets/branding/logo_name_banner.png" alt="Veyra" width="520"></p>

<p align="center">
  Schlanke, konfigurierbare Zusatz-Taskleiste am oberen Bildschirmrand für Windows 11.<br>
  Sie ergänzt die Windows-Taskleiste, ohne sie zu verändern.
</p>

---

## Funktionen

- **Digitaluhr** exakt in der Bildschirmmitte, wahlweise mit Datum und Kalenderwoche. Ein Klick öffnet den Monatskalender.
- **Audio-Schnellwechsel:** Buttons für Wiedergabegeräte und Mikrofone. Ein Klick macht das Gerät zum Standard, das aktive ist markiert. Pro Gerät lassen sich Symbol und Kurzname wählen.
- **Widgets frei anordnen:** Die Uhr hat feste Positionen (links, Mitte, rechts). Alle anderen Widgets verschiebst du per Drag & Drop direkt auf der Leiste, ohne dass sie sich überlappen. Mit der **freien Platzierung** lassen sich alle Widgets, auch die Uhr, beliebig auf der Leiste ablegen, auch übereinander oder in die Ecken.
- **Automatisch ausgeblendet bei Vollbild:** bei Spielen (exklusiv und randlos), Videos und Browser-Vollbild. Danach erscheint die Leiste wieder.
- **Gaming-freundlich:** praktisch keine CPU-Last im Leerlauf, keine GPU-Nutzung, kein Fokusdiebstahl und kein Eintrag in Alt+Tab.
- **Fixiert oder dynamisch:** oben über die volle Breite angedockt – oder als frei bewegliches Fenster mit eigener Größe, Transparenz, abgerundeten Ecken und wählbarer Ebene (z. B. als durchsichtige Uhr im Hintergrund des Desktops).
- **Anpassbar:** Farben (inklusive Farbkreis), Schriften, Höhe, Formate, Monitor und optional ein reservierter Bildschirmbereich. Änderungen wirken sofort.
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
| Audio-Widget ziehen | Position auf der Leiste ändern |
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

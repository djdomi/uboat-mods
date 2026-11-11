# UBOAT Toolkit (QoL + Cheats)

Ein Mod für QoL-Verbesserungen und Cheats (nur Einzelspieler). Jede Funktion lässt sich einzeln ein- und ausschalten.
Er ist für UBOAT 2026.1 gebaut und kompiliert ohne Fehler. **Im Spiel ist er noch nicht getestet.**

## Installation
1. Kopiere den Ordner `UboatToolkit` nach `%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\`
2. Schalte den Mod im Launcher unter „Mods“ ein.

## Bedienung
- **F10** öffnet das Menü mit den Reitern QoL, Cheat und Tools. Änderungen wirken sofort, mit „Speichern“ bleiben sie erhalten.
- Die Einstellungen stehen in `%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\UboatToolkit.ini`. Der Mod legt die Datei selbst an, sie ist optional.
- **F11** schaltet den Bremsen-Finder ein und aus, sofern er im Reiter Tools aktiviert ist.

## Funktionen
| Nr. | Funktion | Schlüssel in der INI |
|---|---|---|
| 11 | Funk sendet schneller (Standard 10-fach) | RadioSpeed |
| 12 | Funk kann vom Gegner nicht angepeilt werden | RadioUndetectable |
| 13 | Kompass ist leise (über die Datei `Data Sheets/General.xlsx`, nicht im Menü abschaltbar) | – |
| 14 | Matrosen dürfen an die Kartenstation | SailorsNavTable |
| 15 | Licht schaltet automatisch: nachts rot, wenn gejagt blau | AutoLight… |
| 16 | Suchscheinwerfer schaltet automatisch (ab Werk aus) | AutoSearchlight |
| 18 | Sehrohr und Fernglas zoomen weiter | OpticsZoom |
| 19 | Sehrohr und Fernglas: ruhiges Bild | OpticsStabilization |
| 20 | Sehrohr fährt bis 12 m aus | PeriscopeHeight |
| 21 | Crew läuft durcheinander durch, Türen bleiben unverändert | NoCrewCollisions |
| 22 | Luken schließen automatisch beim Tauchen und wenn du gejagt wirst | AutoHatches |
| 24 | Vorgeflutetes Deck: Die Crew kann Deckgeschütz und Flak bedienen | AwashGuns |
| 25 | Milchkühe sind ab Spielstart unterwegs | MilkCowsAtStart |
| 27 | 88 mm und Flak: Dreh-Tempo und Schuss pro Minute | DeckGun…/AAGun… |
| 28 | 88 mm und Flak: Magazingröße, für Dauerfeuer | …Magazine |
| Cheat | Sehrohr bis 100 m | CheatPeriscopeHeight |
| Cheat | Boot ist unentdeckbar | CheatUndetectable |
| Cheat | Munition für Deckgeschütz und Flak unendlich | CheatInfiniteGunAmmo |
| Tool | Performance-Logger: CSV-Datei in `...\UBOAT\UboatToolkit\logs` | PerfLogger, PerfHotspots |

## Mit dem Spielstand
Die Werte setzt der Mod als „Modifier“ auf die Spielwerte, die das Spiel selbst kennt. Wenn du eine Funktion ausschaltest, geht der Wert wieder auf den Spielwert zurück.

# Code-Mods für UBOAT: Spielregeln und Werte im Code ändern

Stand: 11.11.2026 · Ziel: Mods, die echten Spiel-Code verändern,
zum Beispiel deutlich mehr Torpedos.

Wichtig vorab: Dieses Dokument basiert vor allem auf Beiträgen einzelner
Spieler in Steam-Foren (2020 bis 2024). Offizielle Anleitungen waren nicht
abrufbar. Unsichere Angaben sind mit „(unsicher)“ markiert.

---

## 1. Das Wichtigste in Kürze

- Der Spiel-Code liegt in einer Datei namens `com.uboat.game.dll` (unsicher: Ort siehe Abschnitt 3).
- Mit einem Werkzeug namens **dotPeek** kann man diesen Code lesen.
- Eine Code-Mod hängt sich an bestehende Spiel-Funktionen und ändert deren Ergebnis.
  Dafür nutzt man die Bibliothek **Harmony**. Sie ist laut Forum schon im Spiel enthalten.
- Man braucht **Unity 2019.4**, eine Entwicklungsumgebung (Visual Studio) und Grundkenntnisse in C#.
- Code-Mods sind **riskant**: Nach jedem Spiel-Update können sie kaputtgehen.

---

## 2. Die Möglichkeiten, Code zu ändern

| Weg | Was man damit macht | Schwierigkeit |
|---|---|---|
| **Harmony-Patch** | Eine eigene Funktion läuft vor oder nach einer Spiel-Funktion. Man kann auch den Rückgabewert ändern. | Mittel |
| **Eigene Klassen** | Man schreibt neuen Code, der eigene Regeln umsetzt. | Hoch |
| **Eigene Daten-Skripte** | Werte laufen über Tabellen, das ist aber bereits Tabellen-Modding. | Niedrig |

Für „mehr Torpedos“ ist der **Harmony-Patch** der übliche Weg. Im Forum wird
ein Test-Mod genannt, der Torpedo-Funktionen mit Harmony verändert (unsicher, Quelle siehe unten).

Beispiel-Muster (vereinfacht, in Worten):
- „Vor der Funktion, die die Torpedoanzahl festlegt, läuft mein Code.“
- „Mein Code setzt die Anzahl auf einen höheren Wert.“
- „Danach läuft die Original-Funktion weiter.“

---

## 3. Was man dafür braucht

1. **Unity 2019.4.x** über Unity Hub (ein kostenloses Unity-Konto ist nötig).
   Genaue Version laut Forum: 2019.4.13f1 oder 2019.4.29f1 (unsicher, die Anleitungen widersprechen sich).
2. **Den Mod-Launcher** von UBOAT: „Create Mod“ mit der **Unity-Mod-Vorlage**.
3. **Visual Studio** zum Schreiben des C#-Codes. Die genaue Version ist nicht bekannt.
4. **dotPeek** (kostenlos, von JetBrains), um den Spiel-Code zu lesen.
5. **Die Spiel-Bibliotheken:** Im Unity-Editor unter
   `Edit > Preferences > UBOAT Modding` den Steam-Pfad des Spiels eintragen,
   dann `Tools > Update Game Assemblies`.

Der Spiel-Code liegt in diesem Ordner (laut Forum, unsicher):
`Steam\steamapps\common\UBOAT\UBOAT_Data\Managed\`

---

## 4. Schritt für Schritt (Plan für eine erste Torpedo-Mod)

1. Unity 2019.4 und Visual Studio installieren.
2. Im Launcher eine neue Mod mit der Unity-Vorlage anlegen.
3. Im Unity-Editor den Steam-Pfad eintragen und `Update Game Assemblies` ausführen.
4. Mit dotPeek `com.uboat.game.dll` öffnen und die Torpedo-Funktion suchen (Suche nach „Torpedo“).
5. Den Code-Teil für die Torpedo-Anzahl notieren.
6. In Visual Studio eine Klasse anlegen und `0harmony.dll` als Verweis hinzufügen (manuell, laut Forum nötig).
7. Eine Harmony-Patch-Funktion schreiben, die die Anzahl erhöht.
8. Im Unity-Editor `Tools > Deploy Mod` ausführen. Erscheint dieser Punkt nicht, ist die Einrichtung fehlerhaft.
9. Im Launcher die Mod einschalten und ein neues Spiel starten.
10. Testen: Hat sich die Torpedo-Anzahl geändert?

---

## 5. Typische Fehler (aus dem Forum)

| Fehlermeldung | Bedeutung und Lösung |
|---|---|
| `Unable to resolve reference 'Jint'` | Fehlende Dateien. Laut Forum `Jint.dll` und `Esprima.dll` aus dem `Managed`-Ordner in das Mod-Paket kopieren. |
| `Reference has errors: 'com.uboat.game'` | Spiel-Bibliotheken fehlen. Schritt 3.5 wiederholen. |
| `Couldn't locate mod "…" assembly file (.dll)` | Der Name der `.asmdef`-Datei muss gleich dem Mod-Ordner sein. |
| Tools > Deploy Mod fehlt | Die Einrichtung ist nicht vollständig. |

---

## 6. Risiken und Grenzen

- **Spiel-Updates:** Ändert der Hersteller den Code, passen Harmony-Patches oft nicht mehr. Die Mod kann dann abstürzen oder gar nichts bewirken.
- **Spielstände:** Viele Code-Änderungen wirken nur in neuen Kampagnen.
- **Versionskonflikte:** Die Unity-Version muss zur Spielversion passen. Die Forenangaben dazu sind widersprüchlich.
- **Mehrere Mods:** Zwei Mods, die dieselbe Funktion patchen, können sich gegenseitig stören.
- **Balance:** Viel mehr Torpedos verändern das Spiel stark. Das ist Geschmackssache, kann aber Fehler im Spielablauf auslösen.
- **Sicherheit:** Lade nur Mods von Quellen, denen du vertraust. Code-Mods laufen mit denselben Rechten wie das Spiel.

---

## 7. Lücken und offene Fragen

- Die offizielle Wiki-Anleitung zum Unity-Projekt konnte nicht gelesen werden.
- Der Aufbau der Mod-Datei `Manifest.json` ist nicht dokumentiert.
- Die genaue .NET-Version und die Visual-Studio-Version sind unbekannt.
- Wie man eine Code-Mod im Launcher testet, ist nur grob beschrieben.
- Der Quellcode-Ordner `src` im Beispiel-Repo konnte nicht gelesen werden (robots.txt).

---

## Quellen

- Steam: Finding Script Files: https://steamcommunity.com/app/494840/discussions/8/2248929785826497284/
- Steam: Requesting some help – starting create a scripting mod: https://steamcommunity.com/app/494840/discussions/8/5717866822625599253/
- Steam: modding tutorials: https://steamcommunity.com/app/494840/discussions/0/3038228835120667600/
- Steam: Editing the Officers' Skills (dotPeek): https://steamcommunity.com/app/494840/discussions/0/6811212011867289286/
- GitHub: https://github.com/shole/uboat-mods
- Harmony-Dokumentation: https://harmony.pardeike.net/articles/intro.html
- Wiki Modding (nicht erreichbar): https://uboat.fandom.com/wiki/Modding

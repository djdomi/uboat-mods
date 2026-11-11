# Recherche: Mods für UBOAT bauen

Stand: 11.11.2026 · Spiel: UBOAT (Steam-App 494840) von Deep Water Studio

Dieses Dokument erklärt in einfachen Worten, wie man Mods für UBOAT macht.
Es ist die Grundlage für alle weiteren Arbeiten in diesem Repo.

> **Hinweis zur Quellenlage:** Die Fandom-Wiki-Seiten zum Modding waren beim
> Abruf nicht erreichbar. Die Fakten stammen deshalb aus Steam-Diskussionen,
> Nexus Mods und einem öffentlichen Mod-Projekt auf GitHub. Einige Angaben
> sind von Nutzern und älter. Sie sind mit „(unsicher)“ markiert.

---

## 1. Kurz zusammengefasst

- UBOAT unterstützt Mods offiziell. Es gibt einen eigenen **Launcher mit Mod-Verwaltung** und den **Steam Workshop**.
- Es gibt **drei Wege** zu modden. Der einfachste ist das Ändern von Tabellen (Excel-Dateien). Dafür braucht man **keine Programmierkenntnisse**.
- Mods liegen in einem eigenen Ordner. Die Original-Dateien des Spiels muss man **nicht** anfassen.

---

## 2. Die drei Wege zu modden

### Weg A: Tabellen ändern („Data Sheets“) – für Einsteiger

Fast alle Spielwerte stehen in Excel-Dateien (`.xlsx`).
Zum Beispiel Geschwindigkeiten, Ausrüstung, Forschung oder Besatzung.

Bekannte Tabellen (laut Steam-Diskussionen):

| Datei | Was drin steht |
|---|---|
| `Entities.xlsx` (Blatt „Equipment“) | Ausrüstung und Upgrades |
| `General.xlsx` (Blatt „BackgroundTasks“) | Hintergrund-Aufgaben |
| `CharacterClasses.xlsx` | Besatzung, Fähigkeiten |
| `Crew.xlsx` | Besatzung (unsicher) |

**Wichtig:** Eine Mod kopiert nur die geänderten Zeilen in ihren eigenen
Ordner `Data Sheets`. Das Spiel mischt das dann mit den Original-Werten.
So überschreibt ein Spiel-Update die Mod nicht.

Werkzeug: Ein Tabellenprogramm, z. B. **LibreOffice Calc** (kostenlos) oder Excel.

### Weg B: Grafiken und 3D-Modelle tauschen – für Fortgeschrittene

Hier ändert man das Aussehen, z. B. Texturen (Bilder) oder Schiffs-Modelle.

- Werkzeug: **Unity** (die Spiel-Engine) über **Unity Hub**.
- Die Unity-Version muss genau passen. Ein aktuelles Projekt nutzt **Unity 2021.3.33f1**. Ältere Anleitungen nennen 2019.4 oder 2018.2. Das ist veraltet.
- Bilder sind `.png`, 3D-Modelle `.obj`.
- Das Tool **UBOAT Inspector** (Nexus Mods) kann Spiel-Grafiken exportieren.
- Für Materialien den Shader **„Standard (DWS)“** nehmen, nicht „Standard“.

### Weg C: Programm-Code ändern – nur für Programmierer

Hier ändert man das Verhalten des Spiels mit C#-Code.
Das braucht Unity, Programmierkenntnisse und Tools wie dotPeek
(zeigt den Spiel-Code an). Für dieses Projekt erst einmal nicht nötig.

---

## 3. Wo liegen die Dateien? (Windows)

| Was | Ordner |
|---|---|
| Eigene Mods | `C:\Users\<Name>\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\` |
| Workshop-Mods (abonniert) | `Steam\steamapps\workshop\content\494840\` |
| Original-Tabellen | Im Installationsordner des Spiels (nicht ändern!) |

Tipp: `AppData` ist versteckt. In der Adresszeile vom Explorer
`%USERPROFILE%\AppData\LocalLow` eintippen und Enter drücken.

---

## 4. So ist eine Mod aufgebaut

Ein Mod-Ordner sieht typischerweise so aus:

```
MeineMod\
├── Manifest.json    (Name, Version, Spielversion der Mod)
├── Preview.jpg      (Vorschaubild für Launcher und Workshop)
├── Data Sheets\     (geänderte Tabellen – Weg A)
├── Bundles\         (Grafiken/Modelle aus Unity – Weg B)
└── Source\          (Code – Weg C)
```

Man braucht nur die Ordner, die man wirklich nutzt.
Die `Manifest.json` erstellt der Launcher automatisch.
Ist sie veraltet, zeigt der Launcher die Mod als „outdated“ an.

---

## 5. Erste eigene Mod – Schritt für Schritt (Weg A)

1. **Spiel über Steam starten** und den **Launcher** wählen.
2. Im Launcher auf **„Mods“** klicken.
3. **Neue, leere Mod anlegen.** Der Launcher erstellt den Ordner im Mods-Verzeichnis (siehe Abschnitt 3).
4. In diesem Mod-Ordner einen Unterordner **`Data Sheets`** anlegen.
5. Die passende Original-Tabelle (z. B. `Entities.xlsx`) aus dem Spielordner **kopieren** – nicht verschieben.
6. Die Kopie in LibreOffice öffnen. **Nur die Zeilen behalten, die du ändern willst.** Die Kopfzeile und den Blattnamen nicht ändern.
7. Einen Wert ändern, z. B. eine Zahl. Als `.xlsx` speichern.
8. Datei in `MeineMod\Data Sheets\` legen.
9. Im Launcher die Mod **einschalten** und auf **„Play“** klicken.
10. **Neues Spiel** starten und prüfen, ob die Änderung wirkt.

Hochladen in den Workshop: Das geht über die Mod-Verwaltung im Launcher.
Eine genaue Anleitung dazu wurde in den Quellen nicht gefunden (unsicher).

---

## 6. Einschränkungen und Stolperfallen

- **Spielstände:** Viele Mods brauchen ein **neues Spiel**. Manche Werte werden beim Start einer Kampagne fest gespeichert (`.dat`-Dateien in `AppData\LocalLow`).
- **Updates:** Nach einem Spiel-Update können Mods kaputtgehen, vor allem Weg B und C.
- **Unity-Version:** Muss exakt zur Spielversion passen, sonst lädt die Mod nicht.
- **Abhängigkeiten:** Braucht eine Mod eine andere, müssen beide abonniert und eingeschaltet sein.
- **Manuell installierte Mods** werden nicht automatisch aktualisiert.
- **Original-Dateien nie direkt ändern.** Updates überschreiben sie.

---

## 7. Offene Punkte für später

- Wiki-Seiten (Datasheet Modding, Unity-Projekt) erneut lesen, wenn erreichbar.
- Genaue Felder der `Manifest.json` klären.
- Workshop-Upload Schritt für Schritt testen.
- Reihenfolge (Load Order) bei mehreren Mods klären.

---

## Quellen

- Steam-Shop: https://store.steampowered.com/app/494840/UBOAT/
- UBOAT Wiki: https://uboat.fandom.com/wiki/Uboat_Wiki
- Wiki Modding (nicht erreichbar): https://uboat.fandom.com/wiki/Modding
- Wiki Datasheet Modding (nicht erreichbar): https://uboat.fandom.com/wiki/Datasheet_Modding
- Steam: Wo liegen Mods? https://steamcommunity.com/app/494840/discussions/8/3476150562409435209/
- Steam: Getting started? https://steamcommunity.com/app/494840/discussions/8/1660069015241244868/
- Steam: Unity Version? https://steamcommunity.com/app/494840/discussions/8/4164121860550458999/
- Steam: Officer Skills / Data Sheets: https://steamcommunity.com/app/494840/discussions/0/6811212011867289286/
- Steam: Mod aus SteamCMD installieren: https://steamcommunity.com/app/494840/discussions/8/4638238612651005435/
- Steam: Upgrades modden: https://steamcommunity.com/app/494840/discussions/8/1747896471391511289/
- Workshop-Sammlung „UBOAT MODS“: https://steamcommunity.com/sharedfiles/filedetails/?id=3307578906
- Nexus Mods – UBOAT Inspector: https://www.nexusmods.com/uboat/mods/2
- GitHub – Beispiel-Mod-Projekt: https://github.com/shole/uboat-mods

# UBOAT: Speicher-Problem ("Memory Leak") – kurz erklärt

Stand: 11.11.2026

## Was passiert?

Je länger man spielt, desto mehr Arbeitsspeicher (RAM) braucht UBOAT.
Das Spiel wird langsam, ruckelt oder stürzt ab.

Spieler berichten:
- Nach ca. 1 Stunde ruckelt es. **Neustart hilft** für die nächste Stunde. [1]
- Bei jedem Speichern steigt der RAM um ca. 1 GB und sinkt nicht mehr (mit dem Mod "U-Boat Expanded", 2022). [2]
- Sehr alte Spielstände (150+ Stunden) werden immer langsamer. Die Logdatei wuchs dort auf über 6 GB. [3]

## Woran liegt es? (soweit bekannt)

Eine offizielle, vollständige Erklärung gibt es **nicht**.

- Die Entwickler haben mehrfach Speicher-Fehler behoben, z. B. in Version B129:
  "Major memory usage optimizations" und ein Fix für Leistung, die
  "in langen Spielen immer schlechter wurde". Für die volle Wirkung war oft
  **ein neues Spiel nötig**. [4]
- Bei einem alten Spielstand sagte ein Entwickler: "It's fixed I think" –
  aber das sei ein seltener Einzelfall. [3]
- ⚠️ Unsicher: Der Mod-Autor von "U-Boat Expanded" vermutet die
  automatische Aufräum-Funktion des Spiels ("garbage cleanup"). Das ist nur eine Vermutung. [2]
- ⚠️ Unsicher: Viele Mods können den Effekt verstärken. Belegt ist das nicht.

## Was hilft? (einfach zuerst)

1. **Spiel regelmäßig neu starten** (z. B. jede 1–2 Stunden). Das leert den RAM. Hilft sicher.
2. **Grafik-Einstellungen senken.** Laut Entwicklern bringt das am meisten bei niedrigen Einstellungen. [4]
3. **Windows-Auslagerungsdatei anlassen** und **mind. 10 GB frei** auf der Festplatte haben (Tipp der Entwickler). [5]
4. **Andere Programme schließen** (Browser, Discord usw.). [5]
5. **Weniger Mods** benutzen und testen, ob es ohne Mods besser ist.
6. Bei einem "kaputten" alten Spielstand: **älteren Speicherstand laden** oder neue Karriere starten. [3][4]
7. Wenn das Spiel spinnt: Steam-Cloud aus, Mod-Liste und Config-Dateien im Ordner
   `%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\` löschen, neu starten, Cloud wieder an.
   (Spieler-Tipp, vorher Sicherung machen!) [1]

## Kurz gesagt

Ein echtes "Heilmittel" gibt es nicht. **Neustarten, weniger Mods, Grafik runter**
sind die besten Mittel. Das Problem liegt im Spiel selbst – reparieren können es nur die Entwickler.

## Quellen

1. Steam-Forum: https://steamcommunity.com/app/494840/discussions/0/3273563387055045409/
2. U-Boat Expanded Diskussion: https://steamcommunity.com/workshop/filedetails/discussion/2114304535/3185736852419554985/
3. Steam-Forum (alter Spielstand): https://steamcommunity.com/app/494840/discussions/0/595144081821215076/
4. B129 Changelog: https://steamcommunity.com/app/494840/discussions/0/3088885996493591123/
5. Entwickler-Guide Abstürze: https://steamcommunity.com/app/494840/discussions/0/4626979317219361800/

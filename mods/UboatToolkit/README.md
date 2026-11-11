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
| 23 | Ballast: Aufgetaucht bläst der Diesel die Tanks leer, beim Tauchen fluten sie schneller | BallastDieselBlow, BallastFastDive |
| 24 | Vorgeflutetes Deck: Die Crew kann Deckgeschütz und Flak bedienen | AwashGuns |
| 25 | Milchkühe sind ab Spielstart unterwegs | MilkCowsAtStart |
| 27 | 88 mm und Flak: Dreh-Tempo und Schuss pro Minute | DeckGun…/AAGun… |
| 28 | 88 mm und Flak: Magazingröße, für Dauerfeuer | …Magazine |
| – | Geschossgeschwindigkeit für 88 mm und Flak | …ShellSpeed |
| – | KI-Schützen treffen besser, ohne Streuung (gleicht auch das Wackeln aus) | GunnerAccuracy |
| – | Flak schießt auch auf Schiffe | AAvsShips |
| – | Schussgeräusche begrenzen, gegen Tonprobleme bei schnellem Feuer | GunSoundGap |
| IC-H | Verletzte werden nicht automatisch weggetragen | NoAutoCarryWounded |
| IC-J | Atemgeräte halten länger | RebreatherDurability |
| 29 | Verletzte und Kranke werden automatisch versorgt. Ein freier Mann holt sich dafür einen Verbandskasten aus dem Lager | AutoFirstAid, AutoFirstAidDelay, AutoFetchMedKit, AutoFirstAidSkills (Sanitäts-Ausbildung, Funker, Läufer zuerst) |
| 30 | Schadenskontrolle automatisch: Bei Alarm und Schaden schalten Ingenieure um, danach wieder zurück | AutoDamageControl, AutoDamageControlCount, AutoDamageControlRadioman, AutoDamageControlSkills (Handwerker + Sparsam zuerst) |
| 31 | Werkstatt automatisch: Schrott kommt in die Werkstatt, Ersatzteile werden gebaut | AutoWorkshop |
| 32 | Land-Schutz: Zeitraffer stoppt, bevor das Boot auf Land fährt | AntiGrounding, AntiGroundingSeconds |
| 34 | Tiefenruder immer besetzt (freier Matrose geht hin) | KeepDepthStation, StationDelay |
| 35 | Kartentisch immer besetzt (spart Diesel) | KeepNavTable |
| 36 | Lüftung leiser, Absorber hält länger, E-Kompressor verbraucht keine Luft, unter Wasser Lüftung automatisch an, Notfall-Sauerstoff | VentilationNoise, AbsorberDuration, CompressorNoAir, AutoVentilation, EmergencyOxygen |
| 37 | Volle Besatzung ab Start (vor der ersten Feindfahrt) | FullCrewAtStart, FullCrewSize |
| 38 | Pumpe schaltet sich bei Wasser selbst an | AutoPump |
| 39 | Hafen: Verladen friert nicht mehr ein, hängendes Verladen wird neu angestoßen oder abgebrochen, Abbrechen per Schalter | PortLoadingFix (Grundfix, immer an), PortLoadingStuckSeconds, CancelPortLoading, FixMyDamnShipment (alles erzwingen), InstantPortLoading (alles sofort) |
| Cheat | Boot schneller | CheatSpeed |
| Cheat | Munition und Gegenstände fast ohne Gewicht (0,0000001) | CheatNoWeight |
| Cheat | Lager: viel mehr pro Platz stapeln | CheatStackSize |
| Cheat | Crew läuft schneller, Crew unsterblich, Vorräte gehen nie aus | CheatCrewWalkSpeed, CheatImmortalCrew, CheatNoSupplyUse |
| Cheat | Starke Pumpe, in jeder Tiefe, Boot läuft nie voll | CheatPumpPower, CheatPumpAnyDepth, CheatNeverFlood |
| 17 | Torpedo-Rechner: Das Spiel rechnet die Lösung automatisch (Vorschau an) | EasyTDC |
| 26 | Torpedos unendlich: Das Rohr lädt nach dem Schuss sofort nach | CheatInfiniteTorpedoes |
| Cheat | Immer T5 Zaunkönig nachladen, keine Versager | CheatAllT5, CheatNoDuds |
| Cheat | Sensoren sehen und hören weiter (Radar, Sonar, Horchgerät, Ausguck) | CheatSensorRange |
| Cheat | Kein Wasserdruck-Schaden in der Tiefe | CheatDeepDive |
| Cheat | Sehrohr bis 100 m | CheatPeriscopeHeight |
| Cheat | Rumpf verliert keine Gesundheit | CheatNoHullDamage |
| Cheat | Diesel, Batterie und Luft bleiben voll | CheatFullFuel, CheatFullBattery, CheatFullAir |
| Cheat | Crew wird nie müde, Disziplin bleibt voll, kein Stress | CheatCrewNeverTired, CheatFullDiscipline |
| Cheat | Mindestwert für Geld und Ruf | CheatMoney, CheatReputation |
| Cheat | Boot ist unentdeckbar | CheatUndetectable |
| Cheat | Munition für Deckgeschütz und Flak unendlich | CheatInfiniteGunAmmo |
| QoL | Zeitraffer stoppt bei neuem Schiff oder Flugzeug | StopTimeOnShip, StopTimeOnAircraft |
| QoL | Zeitraffer ignoriert Fischerboote, Verbündete (und auf Wunsch Neutrale) | IgnoreFishingBoats, IgnoreAlliedShips, IgnoreNeutralShips |
| QoL | Zurück ins Wasser, wenn das Boot an Land festsitzt | BackToWater, BackToWaterSeconds, BackToWaterNow |
| QoL | Auto-Speichern alle x Minuten | AutoSaveMinutes |
| QoL | Warnung + Zeitraffer-Stopp bei wenig Diesel, Batterie, Luft | LowWarnPercent |
| QoL | Karte: Restweg, Ankunftszeit, Diesel-Reichweite oben links | NavInfo |
| QoL | Tiefe per Taste: frei belegbar (Standard Strg+Bild runter/hoch 10 m, Umschalt 100 m) | DepthHotkeys |
| QoL | Alle leeren Rohre aus dem Lager laden (Strg+Umschalt+R) | ReloadAllNow |
| QoL | Wachen tauschen, Lager aufräumen, Milchkuh herholen (Einmal-Schalter) | SwapShiftsNow, SortStorageNow, CallMilkCow |
| QoL | Iron Coffins A: Ballast-Befehle immer da | BallastAlwaysAvailable |
| QoL | Iron Coffins E: Luken zeigen Luftdruck, sperren bei Unterschied + Druckausgleich | HatchPressure, HatchPressureLimit, HatchEqualize |
| QoL | Iron Coffins F: Leck abdichten wie mit zwei Matrosen | LeakTwoSailors |
| Cheat | Backup-Spielstand, wenn ein Cheat angeht | BackupBeforeCheats |
| Cheat | Preise, Reparatur-, Forschungstempo | CheatPriceFactor, CheatRepairSpeed, CheatResearchSpeed |
| Cheat | Alles freischalten, Torpedo-Tempo, Schnellreise (Strg+Umschalt+T), Echolot-Ping | CheatUnlockAll, CheatTorpedoSpeed, CheatQuickTravel, CheatSonarPing |
| Tool | Freie Tastenbelegung: Tab „Tasten“ im F10-Menü, „Ändern“ drücken, dann neue Taste (mit Strg/Umschalt/Alt). Esc = Abbruch, Rück = keine Taste. Steht auch in der INI unter [Tasten], z. B. KeyReloadAll=Ctrl+Shift+R | Key... |
| Tool | Spiel-Log klein halten (nur Warnungen/Fehler), GC-Zeit pro Bild einstellbar | GameLogFilter, GcSliceMs |
| Tool | Speicher aufräumen alle x Minuten in ruhigen Momenten (Pause/Zeitraffer) | MemoryCleanMinutes |
| Tool | Test-Log: alles, was der Mod tut, alle Fehler, Einstellungen, Bootzustand jede Minute. Datei: `%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\UboatToolkit\logs\toolkit-<Datum>.log` | DebugLog |
| Tool | Performance-Logger: CSV-Datei in `...\UBOAT\UboatToolkit\logs` | PerfLogger, PerfHotspots |

## Mit dem Spielstand
Die Werte setzt der Mod als „Modifier“ auf die Spielwerte, die das Spiel selbst kennt. Wenn du eine Funktion ausschaltest, geht der Wert wieder auf den Spielwert zurück.

# UBOAT Toolkit (QoL + Cheats)

Ein Mod für QoL-Verbesserungen und Cheats (nur Einzelspieler). Jede Funktion lässt sich einzeln ein- und ausschalten.
Er ist für UBOAT 2026.1 gebaut und kompiliert ohne Fehler. **Im Spiel ist er noch nicht getestet.**

## Installation
1. Kopiere den Ordner `UboatToolkit` nach `%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\Mods\`
2. Schalte den Mod im Launcher unter „Mods“ ein.

## Bedienung
- **F10** öffnet das Menü mit den Reitern QoL, Cheat, Crew, Tasten und Tools. Änderungen wirken sofort, mit „Speichern“ bleiben sie erhalten.
- Die Einstellungen stehen in `%USERPROFILE%\AppData\LocalLow\Deep Water Studio\UBOAT\UboatToolkit.ini`. Der Mod legt die Datei selbst an, sie ist optional.
- **F11** schaltet den Bremsen-Finder ein und aus, sofern er im Reiter Tools aktiviert ist.

## Funktionen
Sortiert nach Bereich. Alles einzeln schaltbar im F10-Menü und in der INI.


### Zeitraffer & Navigation
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | Zeitraffer stoppt bei neuem Schiff oder Flugzeug | StopTimeOnShip, StopTimeOnAircraft |
| QoL | Zeitraffer stoppt bei Schaden und Funkspruch | StopTimeOnDamage, StopTimeOnRadio |
| QoL | Zeitraffer ignoriert Fischerboote, Verbündete, auf Wunsch Neutrale | IgnoreFishingBoats, IgnoreAlliedShips, IgnoreNeutralShips |
| QoL | Land-Schutz: Zeitraffer stoppt vor Land | AntiGrounding, AntiGroundingSeconds |
| QoL | Zurück ins Wasser, wenn das Boot an Land festsitzt | BackToWater, BackToWaterSeconds, BackToWaterNow |
| QoL | Warnung + Zeitraffer-Stopp bei wenig Diesel, Batterie, Luft | LowWarnPercent |
| QoL | Kein automatischer Alarm | NoAutoAlarm |
| QoL | Auto-Speichern alle x Minuten | AutoSaveMinutes |
| QoL | Milchkühe ab Spielstart, nächste Milchkuh herholen | MilkCowsAtStart, CallMilkCow |
| Cheat | NEU: Horchgerät hört voll, auch aufgetaucht und bei Fahrt | CheatHydrophoneAlways |
| Cheat | NEU: Funk-Empfang in jeder Tiefe | CheatRadioAnyDepth |
| Cheat | Schnellreise ans Kursziel (Strg+Umschalt+T) | CheatQuickTravel, CheatQuickTravelNow |
| Cheat | Boot schneller | CheatSpeed |
| Cheat | Boot unentdeckbar | CheatUndetectable |
| Cheat | Sensoren sehen und hören weiter | CheatSensorRange |
| Cheat | Echolot pingt von selbst | CheatSonarPing |

### Crew
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | NEU: Skill-Editor im Tab „Crew“: jeden Skill per Klick geben oder nehmen | – |
| QoL | Tiefenruder und Kartentisch immer besetzt | KeepDepthStation, KeepNavTable, StationDelay |
| QoL | NEU: Echolot als Matrosen-Aufgabe (pingt nicht bei Alarm) | EchoSailor, EchoSailorSeconds, EchoSailorQuiet |
| QoL | NEU: 88 mm und Flak automatisch besetzen (aufgetaucht + Alarm) | AutoManGuns |
| QoL | NEU: Maschinenraum automatisch besetzen, wenn ein Motor läuft | AutoManEngines |
| QoL | NEU: Mehr Matrosen pro Offizier | MaxFollowers |
| QoL | NEU: Offiziere dürfen alle freien Matrosen mitnehmen | FollowersNoLimit |
| QoL | NEU: Kein Stress | NoStress |
| QoL | Matrosen dürfen an die Kartenstation | SailorsNavTable |
| QoL | Crew läuft durcheinander durch | NoCrewCollisions |
| QoL | Verletzte automatisch versorgen, Verbandskasten holen | AutoFirstAid, AutoFirstAidDelay, AutoFetchMedKit, AutoFirstAidSkills |
| QoL | Verletzte nicht automatisch wegtragen | NoAutoCarryWounded |
| QoL | Schadenskontrolle automatisch bei Alarm | AutoDamageControl, AutoDamageControlCount, AutoDamageControlSkills, AutoDamageControlRadioman |
| QoL | Volle Besatzung ab Start | FullCrewAtStart, FullCrewSize |
| QoL | Wachen tauschen | SwapShiftsNow |
| QoL | Tab „Crew“: Rollen per Klick, freie Skillpunkte | – |
| Cheat | NEU: Eine Fremdsprache = Sprachgenie (Linguist, alle Sprachen) | CheatLinguist |
| Cheat | NEU: Alle heilen (Knopf + Taste) | CheatHealNow, KeyHealAll |
| Cheat | NEU: Deck-Crew bleibt beim Tauchen draußen, schießt weiter, ertrinkt nie | CheatDeckCrewStays |
| Cheat | Crew läuft schneller, unsterblich + unverletzbar, nie müde | CheatCrewWalkSpeed, CheatImmortalCrew, CheatCrewNeverTired |
| Cheat | Disziplin immer voll, kein Stress | CheatFullDiscipline |
| Cheat | Skillpunkte extra oder unbegrenzt, Skills aller Klassen, ohne Stufen-Sperre | CheatExtraSkillPoints, CheatUnlimitedSkillPoints, CheatAllSkills, CheatSkillsAnyLevel |

### Boot & Technik
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | Luken schließen automatisch | AutoHatches |
| QoL | Luken zeigen Luftdruck, Sperre + Druckausgleich (Tempo einstellbar, mit Lüftung schneller) | HatchPressure, HatchPressureLimit, HatchEqualize, HatchEqualizeSpeed, VentEqualizeFaster |
| QoL | Ballast: Diesel bläst aus, schneller fluten, Befehle immer da | BallastDieselBlow, BallastDieselBlowRate, BallastFastDive, BallastAlwaysAvailable |
| QoL | NEU: Schnorchel automatisch auf Sehrohrtiefe | AutoSnorkel |
| QoL | NEU: Schnorchel oben: Lüftung an, Luftdruck schnell auf Normaldruck | SnorkelAir |
| QoL | Pumpe schaltet sich selbst an | AutoPump |
| QoL | Lecks abdichten wie mit zwei Matrosen | LeakTwoSailors |
| QoL | Lüftung leiser, Absorber länger, Kompressor ohne Luft, Notfall-Sauerstoff | VentilationNoise, AbsorberDuration, CompressorNoAir, AutoVentilation, EmergencyOxygen |
| QoL | Atemgeräte halten länger | RebreatherDurability |
| QoL | Strom sparen bei leerer Batterie | EnergySaverPercent |
| QoL | Licht und Suchscheinwerfer automatisch | AutoLight, AutoLightNightRed, AutoLightHuntedBlue, AutoSearchlight |
| QoL | Funk schneller und nicht anpeilbar | RadioSpeed, RadioUndetectable |
| QoL | Sehrohr + Fernglas: mehr Zoom, ruhiges Bild, höher | OpticsZoom, OpticsStabilization, PeriscopeHeight |
| QoL | Werkstatt automatisch | AutoWorkshop |
| Cheat | NEU: Panzer-Rumpf: weniger Schaden, Crew geschützt | CheatHullArmour, CheatCrewProtection |
| Cheat | Diesel, Batterie, Luft bleiben voll | CheatFullFuel, CheatFullBattery, CheatFullAir |
| Cheat | Kein Druckschaden, Rumpf ohne Schaden | CheatDeepDive, CheatNoHullDamage |
| Cheat | Starke Pumpe, jede Tiefe, nie volllaufen | CheatPumpPower, CheatPumpAnyDepth, CheatNeverFlood |
| Cheat | Sehrohr bis 100 m | CheatPeriscopeHeight |

### Waffen & Torpedos
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | Torpedo-Rechner rechnet selbst | EasyTDC |
| QoL | Torpedos schneller nachladen bis sofort | TorpedoLoadSpeed |
| QoL | Alle leeren Rohre laden (Strg+Umschalt+R) | ReloadAllNow |
| QoL | Rohr-Anzeige + Tooltip mit Torpedo-Typ | TubeDisplay |
| QoL | Vorgeflutet: Crew darf an 88 mm + Flak | AwashGuns |
| QoL | 88 mm + Flak: Drehtempo, Schuss/Min, Magazin, Geschosstempo | DeckGun…, AAGun… |
| QoL | KI-Schützen treffen besser, Flak auf Schiffe, Schussgeräusch-Grenze | GunnerAccuracy, AAvsShips, GunSoundGap |
| Cheat | NEU: Zielerfassung genau (Tempo, Kurs, Entfernung) | CheatExactTargets |
| Cheat | Torpedos unendlich, immer T5, keine Versager | CheatInfiniteTorpedoes, CheatAllT5, CheatNoDuds |
| Cheat | Torpedos schneller | CheatTorpedoSpeed |
| Cheat | 88 mm + Flak: Munition unendlich | CheatInfiniteGunAmmo |
| Cheat | Sink the convoy | CheatSinkConvoy, SinkConvoyRange, SinkConvoyTorpedoes |
| Cheat | Fernschuss aus der Koje (Strg+Umschalt+F) | CheatRemoteTorpedo |

### Hafen, Lager & Geld
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | Hafen: Verladen friert nicht ein, abbrechen, erzwingen, sofort | PortLoadingFix, PortLoadingStuckSeconds, CancelPortLoading, FixMyDamnShipment, InstantPortLoading |
| QoL | Lager aufräumen | SortStorageNow |
| Cheat | NEU: Ruf-Punkte und Upgrade-Punkte per Klick dazu | CheatAddReputation, CheatAddUpgradePoints, CheatAddReputationAmount |
| Cheat | NEU: Forschung sofort fertig, alles erforschen | CheatInstantResearch, CheatResearchAll |
| Cheat | Vorräte gehen nie aus | CheatNoSupplyUse |
| Cheat | Fast ohne Gewicht, mehr pro Platz stapeln | CheatNoWeight, CheatStackSize |
| Cheat | Mindest-Geld und Ruf | CheatMoney, CheatReputation |
| Cheat | Preise, Reparatur, Forschung schneller | CheatPriceFactor, CheatRepairSpeed, CheatResearchSpeed |
| Cheat | Alle Upgrades + Boote freischalten | CheatUnlockAll |

### Anzeigen & Erfolge
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | Tacho + Kilometerzähler | Speedometer |
| QoL | Karte: Restweg, Ankunft, Diesel-Reichweite | NavInfo |
| QoL | Kriegstagebuch als Textdatei | WarDiary |
| QoL | NEU: Erfolge immer erlaubt, auch nach Konsolen-Befehlen | AchievementsOn |
| Cheat | NEU: Alle Steam-Erfolge freischalten | CheatAllAchievements |
| Cheat | Aktive Cheats anzeigen, Backup vor Cheats | CheatShowActive, BackupBeforeCheats |

### Leistung & Tools
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | Schneller laden | FastLoading |
| Tool | NEU: Cache aufräumen beim Start (alter Daten-Cache, Absturzberichte, Logs) | AutoCleanCache, AutoCleanDays |
| Tool | Weniger Schiffe/Gruppen gleichzeitig in 3D | MaxShipsPerGroup, MaxGroupsPerBattle |
| Tool | Ferne Schiffe: KI seltener | FarAIInterval |
| Tool | Speicher aufräumen, GC-Zeit, Spiel-Log klein | MemoryCleanMinutes, GcSliceMs, GameLogFilter |
| Tool | Test-Log, Performance-Logger, Bremsen-Finder | DebugLog, PerfLogger, PerfHotspots, PerfQueue |

### Tasten
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| Tool | Alle Tasten frei belegbar (Tab „Tasten“), Tiefe per Taste | Key…, DepthHotkeys |

## Mit dem Spielstand
Die Werte setzt der Mod als „Modifier“ auf die Spielwerte, die das Spiel selbst kennt. Wenn du eine Funktion ausschaltest, geht der Wert wieder auf den Spielwert zurück.

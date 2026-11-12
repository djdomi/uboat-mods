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
| QoL | NEU: Maschinen-Tasten wirken nicht am Sehrohr/freier Kamera | TelegraphBlockScopes |
| QoL | NEU: Kurs-Tasten: fein/grob, Wende, mittschiffs, Tiefe (Standard aus) | CourseKeys |
| QoL | NEU: Fix: Wolfsrudel geht nicht verloren, wenn das BdU gerade funkt | FixWolfpackRetry |
| QoL | NEU: Kurs und Tiefe auf Tasten (Strg/Shift + Pfeile, Strg+F/R/P/C/H) | KeyTurn…, KeyAmidships, KeyDeeper…, KeyShallower…, KeyPeriscopeDepth, KeyMaxDepth, KeyHoldDepth |
| QoL | NEU: Konvois mit Flugzeugträger fahren normal | FixCarrierConvoy |
| QoL | NEU: Maschinen-Telegraf auf Tasten (Alt+0 bis Alt+5, Strg+Alt+1-4 zurück) | KeyGearStop, KeyGear1-5, KeyGearBack1-4 |
| QoL | NEU: Meeresboden immer laden bei flachem Wasser (unter ~300 m) | SeaFloorAlways |
| QoL | NEU: Berliner Zeit auf allen Uhren | BerlinTime |
| QoL | NEU: Karte zeigt genaue Wassertiefe unter der Maus | MapActualDepth |
| QoL | NEU: Kein Tempo-/Reichweiten-Verlust bei hohem Zeitraffer | FixSpeedRange |
| QoL | NEU: Alarmtauchen automatisch bei feindlichem Flugzeug | AutoCrashDive |
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
| QoL | NEU: Luken: Varianten, Lauf-/Sprint-Tempo | HatchVariant, HatchSprintSpeed, HatchWalkSpeed, HatchMaxSpeed |
| QoL | NEU: Regenkleidung: Modus, Regen-/Wind-Grenzen, Kommandant, Unterstand | RainGearMode, RainGearRain, RainGearCapWind, RainGearSkipperCap, RainGearSheltered |
| QoL | NEU: Wer festhängt, versucht es 1 Minute nicht mehr | CrewStuckCooldown |
| QoL | NEU: Deine Befehle gehen nicht verloren, "Alle Luken zu" bleibt zu | CrewOrderRoom, CrewHatchOrders |
| QoL | NEU: Wachposten reservieren, laufende Arbeit behalten | CrewWatchClaims, CrewKeepJobs |
| QoL | NEU: Männer ohne Aufgabe bleiben aus der Zentrale, Arbeit in der Nähe zuerst | CrewControlRoom, CrewNearbyJobs |
| QoL | NEU: Angriffssehrohr nur für den Kommandanten | CrewSkipperAttackScope |
| QoL | NEU: Ausgeruhte machen die Koje für Müde frei | CrewBedYield |
| QoL | NEU: Zwei Mann pro Koje, wenn alle belegt (Standard aus) | CrewBedSharing |
| QoL | NEU: Freie Matrosen legen sich in eine Koje statt im Gang zu stehen | CrewRestInBunks |
| QoL | NEU: Crew-KI: ruhigere, klügere Crew (Hauptschalter, ab nächstem Start) | CrewAIOverhaul |
| QoL | NEU: Ich-Sicht: mit Shift schneller durch Luken | FastHatch |
| QoL | NEU: Regenkleidung auch bei Sturm/Gischt (ab Windstärke 6) | RainGearWind |
| QoL | NEU: Neue/beförderte Offiziere bekommen 6-Stunden-Wachen | OfficerWatches |
| QoL | NEU: Offiziere gehen nicht von selbst ans Beobachtungs-Sehrohr | CrewScopeNoOfficers |
| QoL | NEU: Müde Männer nehmen die nächste freie Koje und reservieren sie | CrewNearestBed |
| QoL | NEU: Alarmtauchen: nur wenige Männer laufen zum Bug (Anzahl einstellbar) | CrewBowRunCap, CrewBowRunMen |
| QoL | NEU: Bei Alarm nur essen, wenn sehr hungrig | CrewNoEatAlarm |
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
| Cheat | NEU: Mehr Offiziere an Bord erlaubt (+0 bis +12) | ExtraOfficers |
| Cheat | NEU: Eine Fremdsprache = Sprachgenie (Linguist, alle Sprachen) | CheatLinguist |
| Cheat | NEU: Alle heilen (Knopf + Taste) | CheatHealNow, KeyHealAll |
| Cheat | NEU: Deck-Crew bleibt beim Tauchen draußen, schießt weiter, ertrinkt nie | CheatDeckCrewStays |
| Cheat | Crew läuft schneller, unsterblich + unverletzbar, nie müde | CheatCrewWalkSpeed, CheatImmortalCrew, CheatCrewNeverTired |
| Cheat | Disziplin immer voll, kein Stress | CheatFullDiscipline |
| Cheat | Skillpunkte extra oder unbegrenzt, Skills aller Klassen, ohne Stufen-Sperre | CheatExtraSkillPoints, CheatUnlimitedSkillPoints, CheatAllSkills, CheatSkillsAnyLevel |

### Boot & Technik
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | NEU: Realistische Sprungschicht 25-150 m, darunter schwerer zu orten + Meldung | SeaLayer, SeaLayerHearing, SeaLayerNote |
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
| QoL | NEU: Geräte: Umschauen, Zoom, Fadenkreuz, Drehknöpfe direkt, AOB live | StLookMode, StMinFov, StDialDirect, StAobLive |
| QoL | NEU: Sehrohr-Knopf öffnet das zuletzt benutzte Sehrohr | ScopeStationLast |
| QoL | NEU: Deckgeschütz: Gegner-Treffsicherheit, Einschießen, Treffer setzt Geschütz außer Gefecht | DgsNpcSpread, DgsNpcRanging, DgsKnockout |
| QoL | NEU: Vorgeflutet etwas Sicht behalten | LookoutAwashView |
| QoL | NEU: Ausguck meldet richtig: Sicht-Würfel, Dunkel-Meldungen, bekannte Kontakte, Wiederholung | LookoutFixRoll, LookoutDarkReports, LookoutKnownAgain, LookoutRetry |
| QoL | NEU: Nachts Konvois etwas weiter finden | LookoutConvoyNight |
| QoL | NEU: Zeitraffer bremst, solange ein ungesehenes Schiff in Reichweite ist | LookoutTimeGuard |
| QoL | NEU: Ausguck: Wetter nur einmal zählen, Nacht-Untergrenze, Nebel, Tag-Kürzung | LookoutWeatherOnce, LookoutWeatherFloor, LookoutFog, LookoutDaylightTrim |
| QoL | NEU: Fadenkreuz-Licht an/aus (Taste F) und Helligkeit | KeyReticleLight, ReticleBright |
| QoL | NEU: Sehrohr merkt sich Ansicht und Zoom | ScopeRemember |
| QoL | NEU: Deckgeschütz bei Seegang langsamer und ungenauer | DeckGunSeaState |
| QoL | NEU: Schornstein-Rauch am Horizont: Konvois früher sichtbar | LookoutSmoke |
| QoL | NEU: Ausguck sucht Schiffsgruppen weiter (einstellbar, Standard x1.69) | Lookout, LookoutGroupRange |
| QoL | NEU: Tab „Torpedos“: Typen für den Hafen-Nachschub gezielt wählen | (Datei toolkit-torpedos.txt) |
| QoL | NEU: Häfen haben eine gleichmäßige Torpedo-Mischung | TorpedoMix |
| QoL | Torpedo-Rechner rechnet selbst | EasyTDC |
| QoL | Torpedos schneller nachladen bis sofort | TorpedoLoadSpeed |
| QoL | Alle leeren Rohre laden (Strg+Umschalt+R) | ReloadAllNow |
| QoL | Rohr-Anzeige + Tooltip mit Torpedo-Typ | TubeDisplay |
| QoL | Vorgeflutet: Crew darf an 88 mm + Flak | AwashGuns |
| QoL | 88 mm + Flak: Drehtempo, Schuss/Min, Magazin, Geschosstempo | DeckGun…, AAGun… |
| QoL | KI-Schützen treffen besser, Flak auf Schiffe, Schussgeräusch-Grenze | GunnerAccuracy, AAvsShips, GunSoundGap |
| Cheat | NEU: Tab „Torpedos“: Munition (88 mm, Flak usw.) direkt ins Lager legen (10-1000) | – |
| Cheat | NEU: Tab „Torpedos“: gewählten Torpedo direkt ins Boot legen (1-30 Stück) | – |
| Cheat | NEU: Zielerfassung genau (Tempo, Kurs, Entfernung) | CheatExactTargets |
| Cheat | Torpedos unendlich, immer T5, keine Versager | CheatInfiniteTorpedoes, CheatAllT5, CheatNoDuds |
| Cheat | Torpedos schneller | CheatTorpedoSpeed |
| Cheat | 88 mm + Flak: Munition unendlich | CheatInfiniteGunAmmo |
| Cheat | Sink the convoy | CheatSinkConvoy, SinkConvoyRange, SinkConvoyTorpedoes |
| Cheat | Fernschuss aus der Koje (Strg+Umschalt+F) | CheatRemoteTorpedo |

### Hafen, Lager & Geld
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | NEU: Hafen-Gespräche hängen nicht mehr fest | FixPortDialogue |
| QoL | NEU: Kommandant tot/verwundet: Warnung vor dem Hafen, keine hängenden Gespräche | FixSkipperDead |
| QoL | NEU: Knopf „Hafen verlassen“ direkt nach dem Ablegen | LeavePortButton |
| QoL | Hafen: Verladen friert nicht ein, abbrechen, erzwingen, sofort | PortLoadingFix, PortLoadingStuckSeconds, CancelPortLoading, FixMyDamnShipment, InstantPortLoading |
| QoL | Lager aufräumen | SortStorageNow |
| Cheat | NEU: Lager wächst mit (fast voll = neue Plätze), Mindest-Plätze | AutoExpandStorage, AutoExpandStep, AutoExpandMax, StorageMinSlots |
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
| QoL | NEU: Am Rand der Ansicht kein Scrollen im Deck-Modus | NoEdgeDeck |
| QoL | NEU: Freie Kamera: weich fahren, Neigen, Mindesthöhe, Zoom-Reset (R) | CamSmoothMove, CamBank, CamMinAlt, KeyCamZoomReset |
| QoL | NEU: Ansagen zum Lichtwechsel stumm | MuteLightCallouts |
| QoL | NEU: Getaucht bleibt die Außenkamera unter Wasser | CamOrbitUnderwater |
| QoL | NEU: Mausrad-Zoom in Ich-Sicht und freier Kamera, Strg+Mausrad an Geräten | CamWheelZoom, StationZoom |
| QoL | NEU: Nacht draußen heller, Boot innen heller (in %) | NightBright, InteriorBright |
| QoL | NEU: Pause ohne Unschärfe und dunkle Einblendung | NoPauseBlur |
| QoL | NEU: Vergessene Belohnungen von Funk-Aufträgen werden ausgezahlt | FixRadioRewards |
| QoL | NEU: Patrouille: Tonnage zählt schon auf dem Weg | TonnageBeforePatrol |
| QoL | Tacho + Kilometerzähler | Speedometer |
| QoL | Karte: Restweg, Ankunft, Diesel-Reichweite | NavInfo |
| QoL | Kriegstagebuch als Textdatei | WarDiary |
| QoL | NEU: Erfolge immer erlaubt, auch nach Konsolen-Befehlen | AchievementsOn |
| Cheat | NEU: Entwickler-Konsole des Spiels einschalten | DevConsole |
| Cheat | NEU: Alle Steam-Erfolge freischalten | CheatAllAchievements |
| Cheat | Aktive Cheats anzeigen, Backup vor Cheats | CheatShowActive, BackupBeforeCheats |

### Leistung & Tools
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| QoL | NEU: Fixes für andere Mods: Preis-Menü, Uniformen, Bifrost (nur aktiv, wenn installiert) | FixSupplyPriceMenu, FixUniformRebuild, FixBifrost |
| QoL | NEU: Fix: Sehrohr ruckelt beim ersten Öffnen nicht | FixScopeStall, ScopePreload |
| QoL | NEU: Laden: Zeit-Budget, keine Müllabfuhr beim Laden | FastLoadBudgetMs, FastLoadNoGc |
| QoL | NEU: Fix: gewählte Crew-Sprache bleibt nach Updates | KeepVoiceLanguage |
| QoL | NEU: Fixes gegen Ruckler und Abstürze (Crew-Texturen) | FixAbortStall, FixTextureRace |
| QoL | NEU: Spielstand-Liste nach Karriere sortiert | SaveGroupCareer |
| QoL | NEU: Spielstände schneller laden | FastLoad |
| QoL | NEU: Kein Ruckeln beim Zoomen von Sehrohr/UZO | FixScopeStall |
| QoL | NEU: Kein Bild-Wegrutschen am Bildschirmrand | NoEdgeScroll |
| QoL | Schneller laden | FastLoading |
| Tool | NEU: Cache aufräumen beim Start (alter Daten-Cache, Absturzberichte, Logs) | AutoCleanCache, AutoCleanDays |
| Tool | Weniger Schiffe/Gruppen gleichzeitig in 3D | MaxShipsPerGroup, MaxGroupsPerBattle |
| Tool | Ferne Schiffe: KI seltener | FarAIInterval |
| Tool | Speicher aufräumen, GC-Zeit, Spiel-Log klein | MemoryCleanMinutes, GcSliceMs, GameLogFilter |
| Tool | Test-Log, Performance-Logger, Bremsen-Finder | DebugLog, PerfLogger, PerfHotspots, PerfQueue |

| Tools | NEU: Logs je Teil (Crew-KI, Ausguck, Kamera, Luken, ...) | CrewAILog, LookoutLog, CamLog, HatchLog |
### Tasten
| Art | Funktion | Schlüssel in der INI |
|---|---|---|
| Tool | Alle Tasten frei belegbar (Tab „Tasten“), Tiefe per Taste | Key…, DepthHotkeys |

## Mit dem Spielstand
Die Werte setzt der Mod als „Modifier“ auf die Spielwerte, die das Spiel selbst kennt. Wenn du eine Funktion ausschaltest, geht der Wert wieder auf den Spielwert zurück.

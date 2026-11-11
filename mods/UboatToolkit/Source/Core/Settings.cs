using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UboatToolkit
{
    // One setting. Bool settings use Value 0/1.
    public class Opt
    {
        public string Key, Tab, Label;
        public bool IsBool, IsKey;
        public string Text = "";
        public float Value, Default, Min, Max, Step;
        public bool On => Value > 0.5f;
    }

    // All settings of the mod. Stored in UboatToolkit.ini (optional; defaults are used without it).
    // The in-game menu (F10) and the INI file both read and write this list.
    public static class Settings
    {
        public const string QoL = "QoL";
        public const string Cheat = "Cheat";
        public const string Tools = "Tools";
        public const string KeysTab = "Tasten";

        public static readonly List<Opt> All = new List<Opt>();
        private static readonly Dictionary<string, Opt> byKey = new Dictionary<string, Opt>(StringComparer.OrdinalIgnoreCase);

        public static string Folder => Path.Combine(Application.persistentDataPath, "UboatToolkit");
        public static string FilePath => Path.Combine(Application.persistentDataPath, "UboatToolkit.ini");

        static Settings()
        {
            // ---------- QoL ----------
            Bool(QoL, "AwashGuns", "Vorgeflutet: Crew darf an Deckgeschütz + Flak (#24)", true);
            Num(QoL, "RadioSpeed", "Funk: so viel schneller senden (1 = normal) (#11)", 10, 1, 100, 1);
            Bool(QoL, "RadioUndetectable", "Funk: Gegner kann dich nicht anpeilen (#12)", true);
            Bool(QoL, "AutoLight", "Licht automatisch umschalten (#15)", true);
            Bool(QoL, "AutoLightNightRed", "  nachts Rotlicht", true);
            Bool(QoL, "AutoLightHuntedBlue", "  wenn gejagt Blaulicht", true);
            Bool(QoL, "AutoSearchlight", "Suchscheinwerfer automatisch (nachts, aufgetaucht) (#16)", false);
            Bool(QoL, "AutoHatches", "Luken automatisch schließen (Tauchen / gejagt) (#22)", true);
            Num(QoL, "OpticsZoom", "Sehrohr + Fernglas: mehr Zoom (1 = normal) (#18)", 2, 1, 10, 0.5f);
            Num(QoL, "OpticsStabilization", "Sehrohr + Fernglas: Ruhe (0 = Spiel, 1 = ganz ruhig) (#19)", 1, 0, 1, 0.1f);
            Num(QoL, "PeriscopeHeight", "Sehrohr: max. Ausfahrhöhe in m (0 = Spielwert) (#20)", 12, 0, 12, 0.5f);
            Bool(QoL, "SailorsNavTable", "Matrosen dürfen an die Kartenstation (#14)", true);
            Bool(QoL, "NoCrewCollisions", "Crew läuft durcheinander durch (#21)", true);
            Bool(QoL, "BallastDieselBlow", "Ballast: aufgetaucht Tanks mit Diesel-Abgas leerblasen (spart Pressluft) (#23)", true);
            Num(QoL, "BallastDieselBlowRate", "  Diesel-Ausblasen: % Tank pro Sekunde", 2, 0.5f, 20, 0.5f);
            Num(QoL, "BallastFastDive", "Ballast: schneller fluten beim Tauchen (1 = normal) (#23)", 1, 1, 5, 0.25f);
            Bool(QoL, "NoAutoCarryWounded", "Verletzte nicht automatisch wegtragen", false);
            Bool(QoL, "AutoFirstAid", "Verletzte/Kranke automatisch versorgen (Erste Hilfe) (#29)", true);
            Num(QoL, "AutoFirstAidDelay", "  Sekunden warten, bevor ein freier Mann hilft", 10, 0, 120, 5);
            Bool(QoL, "AutoFetchMedKit", "  Helfer holt sich selbst einen Verbandskasten aus dem Lager", true);
            Bool(QoL, "AutoFirstAidSkills", "  Skills beachten: Sanitäts-Ausbildung, Funker, Läufer zuerst", true);
            Bool(QoL, "AutoDamageControl", "Schadenskontrolle automatisch: bei Alarm + Schaden (#30)", true);
            Num(QoL, "AutoDamageControlCount", "  So viele Offiziere schalten um", 1, 1, 4, 1);
            Bool(QoL, "AutoDamageControlSkills", "  Skills beachten: Handwerker + Sparsam zuerst, Motor-Spezialist bleibt", true);
            Bool(QoL, "AutoDamageControlRadioman", "  Auch der Funker darf umschalten (sonst nur Ingenieure)", false);
            Bool(QoL, "AutoWorkshop", "Werkstatt automatisch: Schrott wird eingeräumt, Ersatzteile werden gebaut (#31)", true);
            Bool(QoL, "AntiGrounding", "Zeitraffer stoppt, bevor das Boot auf Land fährt (#32)", true);
            Num(QoL, "AntiGroundingSeconds", "  Warnzeit in Sekunden (echte Zeit) vor dem Land", 10, 3, 60, 1);
            Bool(QoL, "KeepDepthStation", "Tiefenruder immer besetzt: freier Matrose geht hin (#34)", true);
            Bool(QoL, "KeepNavTable", "Kartentisch immer besetzt (spart Diesel): freier Matrose geht hin (#35)", true);
            Num(QoL, "StationDelay", "  Sekunden leer, bevor jemand geschickt wird", 15, 0, 120, 5);
            Bool(QoL, "EchoSailor", "Echolot als Matrosen-Aufgabe: unter Wasser geht ein freier Matrose hin und pingt", false);
            Bool(QoL, "EchoSailorQuiet", "  Bei Alarm nicht pingen (verrät sonst das Boot)", true);
            Num(QoL, "EchoSailorSeconds", "  Alle x Sekunden (Spielzeit läuft im Zeitraffer schneller)", 60, 10, 600, 10);
            Bool(QoL, "AutoManGuns", "Deckgeschütz + Flak automatisch besetzen (aufgetaucht + Alarm)", false);
            Bool(QoL, "AutoManEngines", "Maschinenraum automatisch besetzen, wenn ein Motor läuft", false);
            Num(QoL, "MaxFollowers", "Matrosen pro Offizier höchstens (0 = Spiel-Wert)", 0, 0, 20, 1);
            Bool(QoL, "FollowersNoLimit", "Offiziere dürfen alle freien Matrosen mitnehmen (ohne 60-%-Grenze)", false);
            Bool(QoL, "NoStress", "Kein Stress in der Crew", false);
            Bool(QoL, "AchievementsOn", "Erfolge (Achievements) immer erlaubt, auch nach Konsolen-Befehlen", false);
            Num(QoL, "VentilationNoise", "Lüftung: Lautstärke/Geräusch x (1 = normal) (#36)", 0.3f, 0.05f, 1, 0.05f);
            Num(QoL, "AbsorberDuration", "  Absorber (Kalipatrone) hält x länger", 2, 1, 10, 0.5f);
            Bool(QoL, "CompressorNoAir", "  E-Kompressor verbraucht keine Luft im Boot", true);
            Bool(QoL, "AutoVentilation", "  Unter Wasser: Lüftung automatisch an + Absorber einsetzen", true);
            Bool(QoL, "EmergencyOxygen", "  Notfall-Sauerstoff: etwas mehr als der Verbrauch, wenn die Luft sinkt", true);
            Bool(QoL, "FullCrewAtStart", "Volle Besatzung ab Start (vor der ersten Feindfahrt) (#37)", true);
            Num(QoL, "FullCrewSize", "  Gewünschte Besatzung (Männer, in der INI ohne Obergrenze)", 90, 10, 200, 1);
            Bool(QoL, "StopTimeOnShip", "Zeitraffer stoppt bei neuem Schiff-Kontakt (Idee 1)", true);
            Bool(QoL, "StopTimeOnAircraft", "Zeitraffer stoppt bei Flugzeug (Idee 1)", true);
            Bool(QoL, "StopTimeOnDamage", "Zeitraffer stoppt bei Schaden (Treffer, neues Leck)", true);
            Bool(QoL, "StopTimeOnRadio", "Zeitraffer stoppt bei Funkspruch", true);
            Bool(QoL, "IgnoreFishingBoats", "  Zeitraffer ignoriert Fischerboote", true);
            Bool(QoL, "IgnoreAlliedShips", "  Zeitraffer ignoriert Verbündete (grün)", true);
            Bool(QoL, "IgnoreNeutralShips", "  Zeitraffer ignoriert Neutrale", false);
            Bool(QoL, "BackToWater", "Zurück ins Wasser: festgefahren an Land -> Boot an letzte sichere Stelle", true);
            Num(QoL, "BackToWaterSeconds", "  So viele Sekunden festgefahren, bevor es passiert", 8, 3, 60, 1);
            Bool(QoL, "BackToWaterNow", "  >> Jetzt zurück ins Wasser", false);
            Num(QoL, "AutoSaveMinutes", "Auto-Speichern alle x Minuten (0 = aus) (Idee 6)", 15, 0, 120, 5);
            Num(QoL, "LowWarnPercent", "Warnung + Zeitraffer-Stopp bei Diesel/Batterie/Luft unter x % (0 = aus) (Idee 8)", 15, 0, 50, 5);
            Bool(QoL, "NavInfo", "Karte: Ankunftszeit + Restweg + Diesel-Reichweite oben links (Idee 7)", true);
            Bool(QoL, "DepthHotkeys", "Tiefe per Taste (Tasten im Tab „Tasten“) (Idee 10)", true);
            Bool(QoL, "ReloadAllNow", "  >> Alle leeren Rohre aus dem Lager laden (Idee 2)", false);
            Bool(QoL, "SwapShiftsNow", "  >> Wachen tauschen: Gruppen mit gleicher Aufgabe tauschen Schichten (Idee 4)", false);
            Bool(QoL, "SortStorageNow", "  >> Lager aufräumen: gleiche Sachen stapeln, nach vorne rücken (Idee 5)", false);
            Bool(QoL, "CallMilkCow", "  >> Nächste Milchkuh zu mir holen", false);
            Bool(QoL, "BallastAlwaysAvailable", "Ballast-Befehle immer verfügbar (außer Ventil kaputt / Hafen)", true);
            Bool(QoL, "HatchPressure", "Luken zeigen Luftdruck beider Räume + sperren bei großem Unterschied", true);
            Num(QoL, "HatchPressureLimit", "  Sperre ab Druck-Unterschied (bar, 0 = nie sperren)", 0.5f, 0, 5, 0.1f);
            Bool(QoL, "HatchEqualize", "  Druckausgleich: Luft strömt langsam nach, bis die Luke wieder aufgeht", true);
            Num(QoL, "HatchEqualizeSpeed", "  Druckausgleich: % vom Unterschied pro Sekunde", 10, 1, 100, 1);
            Num(QoL, "VentEqualizeFaster", "  Lüftung an: Druckausgleich x schneller (1 = kein Bonus)", 3, 1, 10, 0.5f);
            Bool(QoL, "AutoSnorkel", "Schnorchel fährt automatisch aus auf Sehrohrtiefe (1-10 m) und wieder ein", true);
            Bool(QoL, "SnorkelAir", "  Schnorchel oben: Lüftung an + Luftdruck in allen Räumen schnell auf Normaldruck", true);
            Bool(QoL, "LeakTwoSailors", "Lecks: abdichten wie mit zwei Matrosen", true);
            Bool(QoL, "Speedometer", "Tacho + Kilometerzähler oben links (Knoten, Fahrt-km, gesamt)", true);
            Bool(QoL, "NoAutoAlarm", "Kein automatischer Alarm (nur wenn du ihn selbst gibst)", false);
            Bool(QoL, "WarDiary", "Kriegstagebuch: Kontakte, Funk, Schäden, Häfen in logs\\kriegstagebuch.txt", true);
            Bool(QoL, "TubeDisplay", "Rohr-Anzeige oben links: welcher Torpedo-Typ in welchem Rohr", true);
            Num(QoL, "EnergySaverPercent", "Strom sparen: unter Wasser bei Batterie unter x % Kompressor + Scheinwerfer aus (0 = aus)", 20, 0, 50, 5);
            Num(QoL, "TorpedoLoadSpeed", "Torpedos x schneller nachladen (1 = normal, 1000 = sofort)", 1, 1, 1000, 1);
            Bool(QoL, "FastLoading", "Schneller laden: Ladebildschirm nutzt die volle Rechenzeit", true);
            Bool(QoL, "AutoPump", "Pumpe schaltet sich bei Wasser im Boot selbst an (#38)", true);
            Bool(QoL, "PortLoadingFix", "Hafen-Grundfix: kein Einfrieren beim Verladen, hängende Aufträge werden gelöst (#39)", true);
            Num(QoL, "PortLoadingStuckSeconds", "  Hängt das Verladen so viele Sekunden: neu anstoßen, dann abbrechen", 30, 5, 300, 5);
            Bool(QoL, "CancelPortLoading", "  >> Verladen jetzt abbrechen (Schalter springt zurück)", false);
            Bool(QoL, "FixMyDamnShipment", "  >> FIX MY DAMN SHIPMENT! Alles Bestellte sofort ins Boot zwingen", false);
            Bool(QoL, "InstantPortLoading", "Hafen: alles sofort im Boot, keine Wartezeit", true);
            Num(QoL, "RebreatherDurability", "Atemgeräte halten x-mal länger", 2, 1, 10, 0.5f);
            Bool(QoL, "MilkCowsAtStart", "Milchkühe ab Spielstart unterwegs (#25)", true);
            Bool(QoL, "EasyTDC", "Torpedo-Rechner einfach: Lösung wird automatisch berechnet + angezeigt (#17)", true);
            Num(QoL, "DeckGunTurnSpeed", "88mm: Dreh-/Neige-Tempo (1 = normal) (#27)", 1, 0.5f, 10, 0.5f);
            Num(QoL, "DeckGunRPM", "88mm: Schuss pro Minute (0 = Spielwert) (#27)", 0, 0, 120, 5);
            Num(QoL, "DeckGunMagazine", "88mm: Magazin (0 = Spielwert; groß = Dauerfeuer) (#28)", 0, 0, 200, 1);
            Num(QoL, "AAGunTurnSpeed", "Flak: Dreh-/Neige-Tempo (1 = normal) (#27)", 1, 0.5f, 10, 0.5f);
            Num(QoL, "AAGunRPM", "Flak: Schuss pro Minute (0 = Spielwert) (#27)", 0, 0, 600, 10);
            Num(QoL, "AAGunMagazine", "Flak: Magazin (0 = Spielwert) (#28)", 0, 0, 500, 5);
            Num(QoL, "DeckGunShellSpeed", "88mm: Geschossgeschwindigkeit (1 = normal)", 1, 0.5f, 5, 0.25f);
            Num(QoL, "AAGunShellSpeed", "Flak: Geschossgeschwindigkeit (1 = normal)", 1, 0.5f, 5, 0.25f);
            Num(QoL, "GunnerAccuracy", "KI-Schützen: Treffsicherheit (0 = Spiel, 1 = keine Streuung)", 0, 0, 1, 0.1f);
            Bool(QoL, "AAvsShips", "Flak schießt automatisch auch auf Schiffe", false);
            Num(QoL, "GunSoundGap", "Schussgeräusch höchstens alle x Sek. (0 = aus, gegen Tonprobleme)", 0.12f, 0, 1, 0.02f);

            // ---------- Cheats (single player) ----------
            Num(Cheat, "CheatPeriscopeHeight", "Sehrohr bis zu x m ausfahren (0 = aus) (Cheat #34)", 0, 0, 100, 1);
            Bool(Cheat, "CheatUndetectable", "Boot unentdeckbar (Sicht, Sonar, Horchgerät, Radar)", false);
            Bool(Cheat, "CheatInfiniteGunAmmo", "Deckgeschütz + Flak: Munition unendlich", false);
            Bool(Cheat, "CheatInfiniteTorpedoes", "Torpedos unendlich: Rohr lädt nach dem Schuss sofort neu (#26)", false);
            Bool(Cheat, "CheatAllT5", "  Nachgeladen wird immer ein T5 Zaunkönig (zielsuchend)", false);
            Bool(Cheat, "CheatNoDuds", "Keine Torpedo-Versager", false);
            Num(Cheat, "CheatSpeed", "Boot schneller x (über und unter Wasser) (1 = normal) (#33)", 1, 1, 5, 0.25f);
            Num(Cheat, "CheatPumpPower", "Pumpe x stärker (1 = normal) (#38)", 1, 1, 50, 1);
            Bool(Cheat, "CheatPumpAnyDepth", "Pumpe arbeitet in jeder Tiefe voll", false);
            Bool(Cheat, "CheatNeverFlood", "Boot kann nie volllaufen (Wasser wird sofort abgepumpt)", false);
            Num(Cheat, "CheatCrewWalkSpeed", "Crew läuft x schneller (1 = normal)", 1, 1, 3, 0.25f);
            Bool(Cheat, "CheatImmortalCrew", "Crew unsterblich + unverletzbar (keine Wunden, fällt nicht um)", false);
            Bool(Cheat, "CheatNoSupplyUse", "Vorräte gehen nie aus (Essen, Ersatzteile, Verbandskästen ...)", false);
            Bool(Cheat, "CheatNoWeight", "Munition + Gegenstände fast ohne Gewicht (0,0000001)", false);
            Num(Cheat, "CheatStackSize", "Lager: x mehr pro Platz stapeln (1 = normal, INI ohne Obergrenze)", 1, 1, 1000, 10);
            Num(Cheat, "CheatSensorRange", "Sensor-Reichweite x (Radar, Sonar, Horchgerät, Ausguck) (1 = normal)", 1, 1, 5, 0.5f);
            Bool(Cheat, "CheatDeepDive", "Kein Wasserdruck-Schaden in der Tiefe (Rumpf hält)", false);
            Bool(Cheat, "CheatNoHullDamage", "Rumpf verliert keine Gesundheit", false);
            Bool(Cheat, "CheatFullFuel", "Diesel wird nicht verbraucht (Tank bleibt voll)", false);
            Bool(Cheat, "CheatFullBattery", "Batterie bleibt voll", false);
            Bool(Cheat, "CheatFullAir", "Luft unter Wasser unbegrenzt", false);
            Bool(Cheat, "CheatCrewNeverTired", "Crew wird nie müde", false);
            Bool(Cheat, "CheatFullDiscipline", "Disziplin/Stimmung immer voll, kein Stress", false);
            Bool(Cheat, "CheatAllAchievements", "  >> Alle Steam-Erfolge freischalten", false);
            Num(Cheat, "CheatMoney", "Geld: nie weniger als (0 = aus)", 0, 0, 1000000, 10000);
            Num(Cheat, "CheatReputation", "Ruf-Punkte: nie weniger als (0 = aus)", 0, 0, 1000, 10);
            Num(Cheat, "CheatPriceFactor", "Preise im Hafen x (1 = normal, 0,01 = fast gratis)", 1, 0.01f, 1, 0.01f);
            Num(Cheat, "CheatRepairSpeed", "Reparieren x schneller (1 = normal, 1000 = ca. 1 Sek.)", 1, 1, 1000, 10);
            Num(Cheat, "CheatResearchSpeed", "Forschung/Aufgaben im Hauptquartier x schneller", 1, 1, 1000, 10);
            Bool(Cheat, "CheatUnlockAll", "  >> Alle Upgrades + alle Boote freischalten", false);
            Num(Cheat, "CheatTorpedoSpeed", "Torpedos x schneller (Rechner zielt mit, Treffer sicherer)", 1, 1, 5, 0.25f);
            Bool(Cheat, "CheatQuickTravel", "Schnellreise ans Kursziel per Taste (Tab „Tasten“)", false);
            Bool(Cheat, "CheatQuickTravelNow", "  >> Jetzt ans Kursziel springen", false);
            Bool(Cheat, "CheatDeckCrewStays", "Deck-Crew bleibt beim Tauchen draußen, schießt weiter + ertrinkt nie", false);
            Bool(Cheat, "CheatHealNow", "  >> Alle heilen (Wunden, Blutungen, Krankheiten, wieder aufstehen)", false);
            Bool(Cheat, "CheatRadioAnyDepth", "Funk-Empfang in jeder Tiefe", false);
            Bool(Cheat, "CheatHydrophoneAlways", "Horchgerät hört voll, auch aufgetaucht und bei hoher Fahrt", false);
            Bool(Cheat, "CheatExactTargets", "Zielerfassung genau: Tempo, Kurs, Entfernung exakt statt geschätzt", false);
            Num(Cheat, "CheatHullArmour", "Panzer-Rumpf: Schaden am Boot x (1 = normal, 0 = kein Schaden)", 1, 0, 1, 0.05f);
            Bool(Cheat, "CheatCrewProtection", "Rumpf schützt die Crew: keine Verletzungen durch Treffer", false);
            Bool(Cheat, "CheatSonarPing", "Echolot pingt unter Wasser von selbst", false);
            Num(Cheat, "CheatExtraSkillPoints", "Offiziere: x Skillpunkte extra", 0, 0, 50, 1);
            Bool(Cheat, "CheatUnlimitedSkillPoints", "Offiziere: Skillpunkte unbegrenzt (alle Skills lernbar)", false);
            Bool(Cheat, "CheatSkillsAnyLevel", "Alle Skills ab Stufe 1 lernbar (keine Stufen-Sperre)", false);
            Bool(Cheat, "CheatLinguist", "Wer eine Fremdsprache spricht, wird Sprachgenie (Linguist: alle Sprachen)", false);
            Bool(Cheat, "CheatAllSkills", "Offiziere dürfen Skills aller Klassen wählen", false);
            Bool(Cheat, "CheatSinkConvoy", "  >> Sink the convoy: alle Gegner-Schiffe in Reichweite bekommen Torpedos", false);
            Num(Cheat, "SinkConvoyRange", "  Reichweite in km", 20, 1, 100, 1);
            Num(Cheat, "SinkConvoyTorpedoes", "  Torpedos pro Schiff", 3, 1, 10, 1);
            Bool(Cheat, "CheatRemoteTorpedo", "Fernschuss per Taste (auch aus der Koje): Torpedo auf nächsten Gegner", false);
            Bool(Cheat, "BackupBeforeCheats", "Backup-Spielstand, sobald ein Cheat eingeschaltet wird", true);
            Bool(Cheat, "CheatShowActive", "Aktive Cheats oben links anzeigen", true);

            // ---------- Tasten (frei belegbar) ----------
            Key("KeyMenu", "Menü öffnen/schließen", "F10");
            Key("KeyDepthDown10", "Tiefe +10 m (tiefer)", "Ctrl+PageDown");
            Key("KeyDepthUp10", "Tiefe -10 m (höher)", "Ctrl+PageUp");
            Key("KeyDepthDown100", "Tiefe +100 m (tiefer)", "Shift+PageDown");
            Key("KeyDepthUp100", "Tiefe -100 m (höher)", "Shift+PageUp");
            Key("KeyReloadAll", "Alle leeren Rohre laden", "Ctrl+Shift+R");
            Key("KeyQuickTravel", "Schnellreise ans Kursziel (Cheat muss an sein)", "Ctrl+Shift+T");
            Key("KeyBackToWater", "Zurück ins Wasser", "Ctrl+Shift+W");
            Key("KeySortStorage", "Lager aufräumen", "");
            Key("KeySwapShifts", "Wachen tauschen", "");
            Key("KeyCallMilkCow", "Milchkuh herholen", "");
            Key("KeyRemoteTorpedo", "Fernschuss: Torpedo auf nächsten Gegner (Cheat muss an sein)", "Ctrl+Shift+F");
            Key("KeySinkConvoy", "Sink the convoy", "");
            Key("KeyHealAll", "Alle heilen", "");
            Key("KeyHotspots", "Bremsen-Finder an/aus", "F11");

            // ---------- Tools ----------
            Bool(Tools, "DebugLog", "Test-Log: alles, was der Mod tut + Fehler (logs\\toolkit-Datum.log)", true);
            Bool(Tools, "GameLogFilter", "Spiel-Log klein halten: nur Warnungen + Fehler in Player.log (spart Festplatte + RAM)", true);
            Num(Tools, "GcSliceMs", "Speicher-Müllabfuhr: max. ms pro Bild (0 = Spielwert, kleiner = weniger Ruckler)", 0, 0, 10, 0.5f);
            Num(Tools, "MemoryCleanMinutes", "Speicher aufräumen alle x Minuten (gegen wachsenden RAM, 0 = aus)", 30, 0, 120, 5);
            Num(Tools, "MaxShipsPerGroup", "Höchstens x Schiffe pro Konvoi gleichzeitig in 3D (0 = Spiel: 28, weniger = schneller)", 0, 0, 60, 1);
            Num(Tools, "MaxGroupsPerBattle", "Höchstens x Gruppen gleichzeitig im Gefecht (0 = Spiel: 4, weniger = schneller)", 0, 0, 10, 1);
            Num(Tools, "FarAIInterval", "Ferne Schiffe: KI-Takt in Sekunden (30 = Spiel-Standard, 60 = weniger CPU)", 30, 5, 300, 5);
            Bool(Tools, "PerfLogger", "Performance-Logger (schreibt CSV, kostet wenig Leistung)", false);
            Bool(Tools, "PerfHotspots", "  Bremsen-Finder (Taste im Tab „Tasten“, kostet Leistung)", false);
            Bool(Tools, "PerfQueue", "  Bremsen-Finder Spiel-Warteschlange (findet die echten Bremsen, kostet Leistung)", false);
        }

        private static void Bool(string tab, string key, string label, bool def) => Add(new Opt { Tab = tab, Key = key, Label = label, IsBool = true, Default = def ? 1 : 0, Value = def ? 1 : 0, Min = 0, Max = 1, Step = 1 });
        private static void Num(string tab, string key, string label, float def, float min, float max, float step) => Add(new Opt { Tab = tab, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
        private static void Key(string key, string label, string def) => Add(new Opt { Tab = KeysTab, Key = key, Label = label, IsKey = true, Text = def });
        private static void Add(Opt o) { All.Add(o); byKey[o.Key] = o; }

        public static bool B(string key) => byKey.TryGetValue(key, out var o) && o.On;
        public static float F(string key) => byKey.TryGetValue(key, out var o) ? o.Value : 0f;
        public static Opt Get(string key) => byKey.TryGetValue(key, out var o) ? o : null;

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) { Save(); return; }
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    var o = Get(line.Substring(0, eq).Trim());
                    if (o == null) continue;
                    if (o.IsKey) { o.Text = line.Substring(eq + 1).Trim(); continue; }
                    var v = line.Substring(eq + 1).Trim().ToLowerInvariant();
                    if (o.IsBool) o.Value = (v == "1" || v == "true" || v == "on" || v == "ja") ? 1 : 0;
                    else if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) o.Value = Mathf.Max(0f, f); // no upper cap: INI may go beyond the menu range
                }
                // Write back so new settings from a mod update appear in the file.
                Save();
            }
            catch (Exception e)
            {
                Debug.Log(ToolkitLoader.Tag + " could not read INI, using defaults.");
                Debug.LogException(e);
            }
        }

        public static void Save()
        {
            DebugLog.DumpSettings("gespeichert");
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("; UBOAT Toolkit. 1 = an, 0 = aus. Zahlen mit Punkt (z.B. 2.5).");
                sb.AppendLine("; Menü im Spiel: F10. Diese Datei ist optional.");
                string tab = null;
                foreach (var o in All)
                {
                    if (o.Tab != tab) { tab = o.Tab; sb.AppendLine(); sb.AppendLine("[" + tab + "]"); }
                    sb.AppendLine("; " + o.Label.Trim());
                    sb.AppendLine(o.Key + "=" + (o.IsKey ? o.Text : o.IsBool ? (o.On ? "1" : "0") : o.Value.ToString("0.###", CultureInfo.InvariantCulture)));
                }
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}

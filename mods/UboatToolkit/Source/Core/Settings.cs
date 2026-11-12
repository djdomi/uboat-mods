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
            Bool(QoL, "AutoCrashDive", "Alarmtauchen automatisch, wenn ein feindliches Flugzeug gesichtet wird", false);
            Bool(QoL, "LeavePortButton", "Knopf \u201eHafen verlassen\u201c direkt nach dem Ablegen (nur offizieller Ausfahrt-Punkt)", false);
            Bool(QoL, "TonnageBeforePatrol", "Patrouillen-Auftrag: Tonnage z\u00e4hlt schon auf dem Weg ins Gebiet", true);
            Bool(QoL, "MapActualDepth", "Karte: genaue Wassertiefe unter der Maus anzeigen", true);
            Bool(QoL, "TorpedoMix", "H\u00e4fen haben eine gleichm\u00e4\u00dfige Mischung aller Torpedo-Typen", false);
            Bool(QoL, "SeaFloorAlways", "Meeresboden immer laden, wenn das Wasser flacher als ~300 m ist (nur Reise-System \u201eRealistisch\u201c)", true);
            Bool(QoL, "SeaLayer", "Realistische Sprungschicht (25-150 m je nach Wassertemperatur, darunter schwerer zu orten)", true);
            Bool(QoL, "SeaLayerHearing", "  Unter der Sprungschicht h\u00f6rt der eigene Horcher Schiffe oben schlechter", true);
            Bool(QoL, "SeaLayerNote", "  Kurze Meldung beim Unter-/\u00dcberqueren der Sprungschicht", true);
            Bool(QoL, "NoPauseBlur", "Pause ohne Unsch\u00e4rfe und ohne dunkle Einblendung", false);
            Bool(QoL, "NoEdgeScroll", "Kein Bild-Wegrutschen am Bildschirmrand (Schnittansicht + Deck-Sprung)", false);
            Bool(QoL, "FixCarrierConvoy", "Fix: Konvois mit Flugzeugtr\u00e4ger fahren normal (geparkte Flugzeuge bremsen nicht)", true);
            Bool(QoL, "FixScopeStall", "Fix: kein Ruckeln beim Zoomen von Sehrohr/UZO (Bilder bleiben geladen)", true);
            Bool(QoL, "BerlinTime", "Alle Uhren zeigen Berliner Zeit (keine Zeitzonen-Meldungen)", false);
            Bool(QoL, "FixSpeedRange", "Fix: kein Tempo-/Reichweiten-Verlust bei hohem Zeitraffer (lernt Werte bei normaler Zeit)", true);
            Bool(QoL, "FixSkipperDead", "Fix: Kommandant tot/verwundet \u2192 Warnung vor dem Hafen + keine Gespr\u00e4che, die h\u00e4ngen", true);
            Bool(QoL, "FixPortDialogue", "Fix: Hafen-Gespr\u00e4che h\u00e4ngen nicht mehr fest", true);
            Bool(QoL, "FixRadioRewards", "Fix: vergessene Belohnungen von Funk-Auftr\u00e4gen werden ausgezahlt", true);
            Bool(QoL, "MapReadable", "Karte: Raster-Beschriftung auf jedem Kartenstil lesbar + Stil \u201eEigene Farben\u201c (Farben im Tab Karte)", true);
            Bool(QoL, "MapIvory", "Karte: zus\u00e4tzlicher Kartenstil \u201eElfenbein\u201c", true);
            Num(QoL, "ReticleBright", "Fadenkreuz-Licht: Helligkeit (1 = Spiel, gilt wenn „eigene Farbe“ aus)", 1, 0.2f, 3, 0.1f);
            Bool(QoL, "ReticleCustom", "Fadenkreuz-Licht: eigene Farbe, Helligkeit pro Optik, Peilskala-Ausgleich", false);
            Num(QoL, "ReticleR", "  Farbe Rot (0-255)", 27, 0, 255, 1);
            Num(QoL, "ReticleG", "  Farbe Grün (0-255)", 255, 0, 255, 1);
            Num(QoL, "ReticleB", "  Farbe Blau (0-255)", 30, 0, 255, 1);
            Num(QoL, "ReticleStep", "  Helligkeit 0-100 (20 = Spiel, 60 = 4-fach, 100 = 16-fach)", 60, 0, 100, 1);
            Num(QoL, "ReticleMulAttack", "  Faktor Angriffs-Sehrohr", 2, 0, 12, 0.25f);
            Num(QoL, "ReticleMulObservation", "  Faktor Beobachtungs-Sehrohr", 2, 0, 12, 0.25f);
            Num(QoL, "ReticleMulUzo", "  Faktor UZO", 1, 0, 12, 0.25f);
            Num(QoL, "ReticleMulBino", "  Faktor Fernglas", 2, 0, 12, 0.25f);
            Num(QoL, "ReticleBearing", "  Peilskala-Helligkeit (1 = gleich hell wie das Fadenkreuz)", 1, 0.2f, 2, 0.1f);
            Bool(QoL, "ReticleLabels", "  Zahlen der Peilskala mit beleuchten", true);
            Bool(QoL, "ReticleNormalize", "  schwache Masken angleichen (alle Optiken gleich hell)", true);
            Bool(QoL, "ReticleSound", "  Klick-Ton beim Umschalten", true);
            Num(QoL, "ReticleSoundVolume", "    Lautstärke", 1, 0, 2, 0.1f);
            Bool(QoL, "DeckGunSeaState", "Deckgeschütz und Seegang (Streuung, Ladezeit, Sperre bei Sturm, feindliche Kanonen)", true);
            Num(QoL, "DgsCalm", "  ruhig bis Windstärke (keine Strafe)", 4, 0, 12, 0.5f);
            Num(QoL, "DgsRough", "  volle Strafe ab Windstärke", 8, 0, 12, 0.5f);
            Num(QoL, "DgsDisable", "  Geschütz gesperrt ab Windstärke (Leuchtgranaten gehen immer)", 7, 0, 12, 0.5f);
            Num(QoL, "DgsMaxSpread", "  Streuung bei voller Strafe (x)", 3, 1, 10, 0.25f);
            Num(QoL, "DgsMaxReload", "  Ladezeit bei voller Strafe (x)", 2, 1, 5, 0.25f);
            Bool(QoL, "DgsAllShips", "  gilt für alle Schiffe (auch Feinde)", true);
            Bool(QoL, "DgsAA", "  gilt auch für Flak", false);
            Bool(QoL, "DgsManualAim", "  Handzielen bekommt dieselbe Streuung und schießt sich ein", true);
            Num(QoL, "DgsNpcSpread", "  Feind-Kanonen: Streuung in % (100 = Spiel)", 200, 100, 500, 25);
            Num(QoL, "DgsNpcFloor", "  Feind-Kanonen: kleinster Fehlschuss (m pro km)", 4, 0, 20, 1);
            Num(QoL, "DgsNpcRanging", "  Feind-Kanonen: Einschießen in % (100 = Spiel)", 50, 10, 100, 5);
            Bool(QoL, "DgsNpcEvenOut", "  C3 und Tanker schießen sich ein wie andere Frachter", true);
            Bool(QoL, "DgsNpcWarships", "  Feind-Einstellungen auch für Kriegsschiffe", false);
            Bool(QoL, "DgsKnockout", "  eigene Granaten können Feind-Kanonen ausschalten", true);
            Num(QoL, "DgsKnockoutPct", "    Schaden an der Kanone in %", 200, 50, 500, 25);
            Num(QoL, "DgsKnockoutRadius", "    Radius in Metern", 6, 1, 20, 1);
            Bool(QoL, "ScopeRemember", "Sehrohr merkt sich Ansicht und Zoom (je Sehrohr)", false);
            Bool(QoL, "RainGearWind", "Regenkleidung für die Wache an Deck (eigene Steuerung statt Spiel)", true);
            Num(QoL, "RainGearMode", "  Modus: 0 = nur bei Regen, 1 = immer an Deck, 2 = aus", 1, 0, 2, 1);
            Num(QoL, "RainGearRain", "  Mantel ab Regen-Stärke (bei Modus 0)", 0.05f, 0, 1, 0.05f);
            Num(QoL, "RainGearCapRain", "  Südwester ab Regen-Stärke", 0.05f, 0, 1, 0.05f);
            Num(QoL, "RainGearCapWind", "  Südwester ab Windstärke", 6, 0, 12, 0.5f);
            Bool(QoL, "RainGearImpersonated", "  auch den Mann anziehen, durch den du schaust", true);
            Bool(QoL, "RainGearSkipperCap", "  Kommandant trägt auch Südwester", true);
            Bool(QoL, "RainGearSheltered", "  auch im Hafen/geschützt anziehen", false);
            Num(QoL, "RainGearInterval", "  Prüfen alle ... Sekunden", 2, 0.25f, 10, 0.25f);
            Bool(QoL, "OfficerWatches", "Neue/bef\u00f6rderte Offiziere bekommen 6-Stunden-Wachen (halb Dienst, halb Ruhe)", true);
            Num(QoL, "OfficerWatchHours", "  Wachlänge in Stunden (6 oder 3)", 6, 3, 6, 3);
            Bool(QoL, "FastHatch", "Ich-Sicht: Luken flüssig durchqueren (Lauf-Gang bleibt, schneller)", true);
            Bool(QoL, "HatchHoldState", "  Geh- oder Lauf-Durchgang bis zum Ende halten", true);
            Num(QoL, "HatchVariant", "  Durchgang: 0 = wie gedrückt, 1 = immer gehen, 2 = immer laufen", 0, 0, 2, 1);
            Bool(QoL, "HatchWalkForSprint", "  beim Rennen ruhige Geh-Animation nehmen (aber schnell)", false);
            Num(QoL, "HatchSprintSpeed", "  Tempo beim Rennen (x)", 1.6f, 1, 3, 0.1f);
            Num(QoL, "HatchWalkSpeed", "  Tempo beim Gehen (x)", 1, 1, 3, 0.1f);
            Num(QoL, "HatchMaxSpeed", "  höchstes Animations-Tempo", 3, 1, 5, 0.25f);
            Num(QoL, "HatchRunTail", "  nach der Luke weiterlaufen (Sekunden)", 0.35f, 0, 2, 0.05f);
            Bool(QoL, "MuteLightCallouts", "Ansagen zum Lichtwechsel stumm", false);
            Bool(QoL, "QuietUI", "Ruhige Oberfläche an (Einstellungen darunter, Meldungs-Stufen in toolkit-notifications.txt)", false);
            Bool(QoL, "QuietNames", "  Crew-Namen nur beim Zeigen oder Auswählen", true);
            Bool(QoL, "QuietShowHover", "    Name beim Zeigen mit der Maus", true);
            Bool(QoL, "QuietShowSelected", "    Name bei ausgewähltem Mann", true);
            Bool(QoL, "QuietDeviceNames", "  auch Geräte- und Stations-Namen verstecken", false);
            Bool(QoL, "QuietActionIcons", "  Tätigkeits-Symbol und Fortschritt verstecken (gleiche Regel)", true);
            Bool(QoL, "QuietEnergyBars", "  Ruhe-Balken verstecken (gleiche Regel)", true);
            Bool(QoL, "QuietEventIcons", "  Ereignis-Ausrufezeichen verstecken", false);
            Bool(QoL, "QuietTimers", "  Countdown (z. B. Blutung) verstecken", false);
            Bool(QoL, "QuietBanner", "  Band oben in der Mitte und Ton stumm (Symbole bleiben, wichtige Ausnahmen bleiben laut)", true);
            Bool(QoL, "QuietDirectPopups", "  auch direkte Hinweise stumm („Spiel gespeichert“, Fehler beim Speichern ...)", false);
            Bool(QoL, "QuietMutePort", "  Stimmen von Hafen-Personal und fremden Kapitänen stumm (Untertitel bleiben)", false);
            Bool(QoL, "KeepVoiceLanguage", "Fix: gew\u00e4hlte Sprache der Crew bleibt nach Updates erhalten", true);
            Num(QoL, "VoiceLang", "  Stimme festhalten auf: 0 = wie gerade eingestellt, 1 = Deutsch, 2 = Englisch", 0, 0, 2, 1);
            Bool(QoL, "VoiceRepairVersion", "  kaputte „letzte Version“ reparieren (sonst neue Spielstände nicht ladbar)", true);
            Bool(QoL, "FixAbortStall", "Fix: kein Einfrieren, wenn Crew-Aussehen sich w\u00e4hrend Textur-Arbeit \u00e4ndert", true);
            Bool(QoL, "FixTextureRace", "Fix: Texturen werden etwas sp\u00e4ter gel\u00f6scht (weniger Abst\u00fcrze)", true);
            Bool(QoL, "CamWheelZoom", "Kamera: Mausrad zoomt in Ich-Sicht und freier Kamera", true);
            Bool(QoL, "StationZoom", "Kamera: an Ger\u00e4ten (Horcher, Vorhaltrechner ...) zoomt Strg + Mausrad", true);
            Bool(QoL, "CamOrbitUnderwater", "Kamera: getaucht bleibt die Au\u00dfenkamera unter Wasser (realistisch)", false);
            Bool(QoL, "FixWolfpackRetry", "Fix: Wolfsrudel geht nicht verloren, wenn das BdU gerade funkt", true);
            Bool(QoL, "Wolfpack", "Wolfsrudel-Einstellungen an (Datum, Chance, Boote, Ziel-BRT unten)", false);
            Num(QoL, "WolfpackFrom", "  Rudel ab: 0 = Sep 1940, 1 = Jul 1940, 2 = Nov 1940, 3 = Jan 1941, 4 = 8. Mai 1941 (Spiel)", 0, 0, 4, 1);
            Num(QoL, "WolfpackChance", "  Chance, dass sich ein Rudel bildet, in %", 95, 5, 100, 5);
            Num(QoL, "WolfpackBoats", "  höchstens x Boote pro Rudel", 6, 2, 8, 1);
            Num(QoL, "WolfpackGrt", "  erstes Rudel-Ziel: x BRT", 10000, 1000, 50000, 1000);
            Bool(QoL, "FastLoad", "Spielst\u00e4nde schneller laden (Bild ruckelt beim Laden etwas mehr)", true);
            Bool(QoL, "SaveGroupCareer", "Spielstand-Liste: Spielst\u00e4nde einer Karriere stehen zusammen", true);
            Num(QoL, "LookoutRange", "Ausguck: Sichtweite f\u00fcr Schiffe (1 = Spiel, weniger Bonus bei schlechter Sicht)", 1.5f, 1, 2.5f, 0.1f);
            Bool(QoL, "LookoutSmoke", "Ausguck: Rauch fahrender Schiffe sieht man fr\u00fcher, gestoppte Schiffe bleiben sichtbar", true);
            Num(QoL, "NightBright", "Nacht drau\u00dfen heller (%, 0 = Spiel)", 0, 0, 300, 10);
            Num(QoL, "InteriorBright", "Boot innen heller (%, 0 = Spiel)", 0, 0, 300, 10);
            Bool(QoL, "NightOptics", "Optik-Belichtung pro Sehrohr/UZO/Fernglas (nimmt dem Beobachtungs-Sehrohr den Extra-Hellbonus)", false);
            Num(QoL, "NightAttackEv", "  Angriffs-Sehrohr Belichtung in EV (unter dem Spiel-Bild)", -1.3f, -4, 1, 0.1f);
            Num(QoL, "NightObservationEv", "  Beobachtungs-Sehrohr Belichtung in EV (0 = Extra-Bonus weg)", 0, -4, 1, 0.1f);
            Bool(QoL, "NightUzoBinoGrading", "  auch UZO und Fernglas eigene Belichtung", false);
            Num(QoL, "NightUzoEv", "    UZO Belichtung in EV", 0, -4, 1, 0.1f);
            Num(QoL, "NightBinoEv", "    Fernglas Belichtung in EV", 0, -4, 1, 0.1f);
            Num(QoL, "NightOpticStrength", "  Glas-Charakter Stärke in % (Dunst am Tag, Farbverlust nachts)", 100, 0, 200, 5);
            Num(QoL, "NightAttackGlass", "  Glas-Anteil Angriffs-Sehrohr in %", 100, 0, 100, 5);
            Num(QoL, "NightObservationGlass", "  Glas-Anteil Beobachtungs-Sehrohr in %", 35, 0, 100, 5);
            Num(QoL, "NightReticleComp", "  Fadenkreuz-Ausgleich in % (hält das Fadenkreuz trotz dunklerem Bild lesbar)", 100, 0, 200, 5);
            Num(QoL, "NightOpticDayLift", "  Dunst bei Tag (Schwarzwert-Anhebung, 0-100)", 30, 0, 100, 5);
            Num(QoL, "NightOpticColourLoss", "  Farbverlust bei Nacht (0-100)", 45, 0, 100, 5);
            Num(QoL, "NightGainAttack", "  Nacht-Anpassung Angriffs-Sehrohr in % (100 = Spiel)", 100, 0, 300, 10);
            Num(QoL, "NightGainObservation", "  Nacht-Anpassung Beobachtungs-Sehrohr in %", 100, 0, 300, 10);
            Num(QoL, "NightGainUzo", "  Nacht-Anpassung UZO in %", 100, 0, 300, 10);
            Num(QoL, "NightGainBino", "  Nacht-Anpassung Fernglas in %", 100, 0, 300, 10);
            Num(QoL, "NightGainOther", "  Nacht-Anpassung alle anderen Ansichten in %", 100, 0, 300, 10);
            Bool(QoL, "CrewNoEatAlarm", "Crew: bei Alarm nur essen, wenn sehr hungrig", true);
            Bool(QoL, "CrewBowRunCap", "Crew: beim Alarmtauchen laufen h\u00f6chstens 8 Mann zum Bug", true);
            Bool(QoL, "CrewNearestBed", "Crew: m\u00fcde M\u00e4nner nehmen die n\u00e4chste freie Koje", true);
            Bool(QoL, "CrewScopeNoOfficers", "Crew: Offiziere gehen nicht von selbst ans Sehrohr (Befehl geht weiter)", true);
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
            Bool(Cheat, "AutoExpandStorage", "Lager wächst mit: fast voll = neue Plätze (Haupt-, Item-Lager, Küche)", false);
            Num(Cheat, "AutoExpandStep", "  So viele Plätze pro Erweiterung", 2, 1, 10, 1);
            Num(Cheat, "AutoExpandMax", "  Höchstens so viele Plätze pro Lager", 100, 10, 300, 10);
            Num(Cheat, "StorageMinSlots", "  Lager mindestens x Plätze, sofort (0 = aus)", 0, 0, 200, 5);
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
            Bool(Cheat, "DevConsole", "Entwickler-Konsole des Spiels einschalten (mit „Erfolge immer erlaubt“ ohne Achievement-Verlust)", false);
            Bool(Cheat, "CheatInstantResearch", "Forschung + Aufgaben im Hauptquartier sofort fertig", false);
            Bool(Cheat, "CheatResearchAll", "  >> Alles erforschen", false);
            Num(Cheat, "CheatAddReputationAmount", "  Punkte pro Klick (für die zwei Knöpfe unten)", 10, 1, 100, 1);
            Bool(Cheat, "CheatAddReputation", "  >> Ruf-Punkte dazu (damit bezahlst du Forschung)", false);
            Bool(Cheat, "CheatAddUpgradePoints", "  >> Upgrade-Punkte dazu", false);
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
            Num(Cheat, "ExtraOfficers", "Mehr Offiziere an Bord erlaubt (+x, 0 = Spiel)", 0, 0, 12, 1);
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
            Key("KeyGearStop", "Maschine: Stopp", "Alt+Alpha0");
            Key("KeyGear1", "Maschine: Voraus 1", "Alt+Alpha1");
            Key("KeyGear2", "Maschine: Voraus 2", "Alt+Alpha2");
            Key("KeyGear3", "Maschine: Voraus 3", "Alt+Alpha3");
            Key("KeyGear4", "Maschine: Voraus 4", "Alt+Alpha4");
            Key("KeyGear5", "Maschine: Voraus 5 (\u00c4u\u00dferste)", "Alt+Alpha5");
            Key("KeyGearBack1", "Maschine: Zur\u00fcck 1", "Ctrl+Alt+Alpha1");
            Key("KeyGearBack2", "Maschine: Zur\u00fcck 2", "Ctrl+Alt+Alpha2");
            Key("KeyGearBack3", "Maschine: Zur\u00fcck 3", "Ctrl+Alt+Alpha3");
            Key("KeyGearBack4", "Maschine: Zur\u00fcck 4", "Ctrl+Alt+Alpha4");
            Key("KeyTurnL1", "Kurs: 1\u00b0 nach Backbord", "Ctrl+Shift+LeftArrow");
            Key("KeyTurnR1", "Kurs: 1\u00b0 nach Steuerbord", "Ctrl+Shift+RightArrow");
            Key("KeyTurnL5", "Kurs: 5\u00b0 nach Backbord", "Ctrl+LeftArrow");
            Key("KeyTurnR5", "Kurs: 5\u00b0 nach Steuerbord", "Ctrl+RightArrow");
            Key("KeyTurnL45", "Kurs: 45\u00b0 nach Backbord", "Shift+LeftArrow");
            Key("KeyTurnR45", "Kurs: 45\u00b0 nach Steuerbord", "Shift+RightArrow");
            Key("KeyTurnAbout", "Kurs: wenden (180\u00b0)", "Ctrl+DownArrow");
            Key("KeyAmidships", "Ruder mittschiffs", "Ctrl+UpArrow");
            Key("KeyDeeper5", "Tiefe: 5 m tiefer", "Ctrl+F");
            Key("KeyShallower5", "Tiefe: 5 m h\u00f6her", "Ctrl+R");
            Key("KeyDeeper20", "Tiefe: 20 m tiefer", "Ctrl+Shift+F");
            Key("KeyShallower20", "Tiefe: 20 m h\u00f6her", "Ctrl+Shift+R");
            Key("KeyPeriscopeDepth", "Tiefe: Sehrohrtiefe", "Ctrl+P");
            Key("KeyMaxDepth", "Tiefe: maximale sichere Tiefe", "Ctrl+C");
            Key("KeyHoldDepth", "Tiefe: aktuelle Tiefe halten", "Ctrl+H");
            Key("KeyReticleLight", "Fadenkreuz-Licht an/aus (im Sehrohr/Fernglas)", "F");
            Key("KeyHotspots", "Bremsen-Finder an/aus", "F11");

            // ---------- Tools ----------
            Bool(Tools, "DebugLog", "Test-Log: alles, was der Mod tut + Fehler (logs\\toolkit-Datum.log)", true);
            Bool(Tools, "GameLogFilter", "Spiel-Log klein halten: nur Warnungen + Fehler in Player.log (spart Festplatte + RAM)", true);
            Bool(Tools, "MapPixelSample", "Karte: Pixel-Messung der Raster-Beschriftung ins Log (Kontrast, Lesbarkeit, je Stil und Ebene)", false);
            Num(Tools, "GcSliceMs", "Speicher-Müllabfuhr: max. ms pro Bild (0 = Spielwert, kleiner = weniger Ruckler)", 0, 0, 10, 0.5f);
            Bool(Tools, "AutoCleanCache", "Cache aufräumen beim Spielstart: alte Daten-Cache, Absturzberichte, Mod-Logs (Spielstände nie)", true);
            Num(Tools, "AutoCleanDays", "  Dateien älter als x Tage", 14, 1, 90, 1);
            Bool(Tools, "CacheCheck", "Mod-Cache prüfen: Mods, die das Spiel still ablehnen würde, neu bauen lassen (wirkt beim nächsten Start)", true);
            Bool(Tools, "CacheCheckFirst", "  erst mit der Spiel-Prüfung testen (aus = jeden Start alle Mods neu bauen, langsam)", true);
            Num(Tools, "MemoryCleanMinutes", "Speicher aufräumen alle x Minuten (gegen wachsenden RAM, 0 = aus)", 30, 0, 120, 5);
            Num(Tools, "MaxShipsPerGroup", "Höchstens x Schiffe pro Konvoi gleichzeitig in 3D (0 = Spiel: 28, weniger = schneller)", 0, 0, 60, 1);
            Num(Tools, "MaxGroupsPerBattle", "Höchstens x Gruppen gleichzeitig im Gefecht (0 = Spiel: 4, weniger = schneller)", 0, 0, 10, 1);
            Num(Tools, "FarAIInterval", "Ferne Schiffe: KI-Takt in Sekunden (30 = Spiel-Standard, 60 = weniger CPU)", 30, 5, 300, 5);
            Bool(Tools, "OfficerWatchLog", "Log: jede Stunde Offiziere mit Energie und Dienst", false);
            Bool(Tools, "QuietTooltipLog", "Tooltip-Schlüssel mitschreiben (toolkit-tooltip-keys.txt, zum Sperren)", false);
            Bool(Tools, "QuietLog", "Log: jede leise/stumme Meldung und Stimme", false);
            Bool(Tools, "NightLog", "Log: Nacht-Belichtung und Ansicht", false);
            Bool(Tools, "DgsLog", "Log: Deckgeschütz gesperrt wegen Seegang", false);
            Bool(Tools, "RainGearLog", "Log: Regenkleidung an/aus je Mann", false);
            Bool(Tools, "HatchLog", "Log: Luken-Durchgang Bild für Bild", false);
            Bool(Tools, "PerfLogger", "Performance-Logger (schreibt CSV, kostet wenig Leistung)", false);
            Bool(Tools, "PerfHotspots", "  Bremsen-Finder (Taste im Tab „Tasten“, kostet Leistung)", false);
            Bool(Tools, "PerfQueue", "  Bremsen-Finder Spiel-Warteschlange (findet die echten Bremsen, kostet Leistung)", false);

            // ---------- Licht-Modi (Tab "Licht") ----------
            Bool("Licht", "LightModes", "Licht-Modi an (Stil pro Schalter-Stellung, oben wählen)", false);
            Bool("Licht", "LightWarm", "Warme Glühbirnen-Farbe für „warm“ (aus = Spiel-Weiß)", true);
            Num("Licht", "LightLevelNormal", "Helligkeit „warm“ auf Stellung Normal in % (100 = Spiel)", 79, 0, 100, 1);
            Num("Licht", "LightLevelNight", "Helligkeit „warm“ auf Stellung Rot/Blau (Nachtlicht) in % (0 = Licht aus)", 58, 0, 100, 1);
            Num("Licht", "LightLevelEmerg", "Helligkeit „Notlicht“ in %", 65, 0, 100, 1);
            Bool("Licht", "LightRedTower", "Nachtlicht: Turm bleibt rot (Nachtsicht der Wache)", false);
            Bool("Licht", "LightCustomEmerg", "Eigene Notlicht-Farbe statt Spiel-Rot (Farbe oben)", false);
            Num("Licht", "LightNightPerf", "Nachtlicht: Crew-Leistung minus x Punkte", 10, 0, 50, 5);
            Num("Licht", "LightNightSleep", "Nachtlicht: Schlaf erholt x % schneller", 10, 0, 50, 5);
            Num("Licht", "LightEmergPower", "Notlicht: E-Maschinen brauchen x % weniger Strom", 10, 0, 50, 5);
            Bool("Licht", "LightAutoEmerg", "Licht-Automatik des Spiels: nach schwerem Schaden Notlicht", true);
            Num("Licht", "LightDamage", "  schwerer Schaden ab x fehlender Haltbarkeit (alle Geräte zusammen)", 5, 1, 100, 1);
            Num("Licht", "LightLeaks", "  oder mehr als x Lecks (außer in geräumten Abteilungen)", 3, 1, 20, 1);
            Bool("Licht", "LightAutoSilent", "Auto-Schleichfahrt (auch am Lichtschalter umschaltbar)", false);
            Num("Licht", "LightSwapDepth", "  Schleichfahrt an unter x m, aus darüber", 9, 5, 60, 1);
            Bool("Licht", "LightSilentAlarmOnly", "  nur bei Alarm", true);
            Bool("Licht", "LightLabels", "Schalter-Namen passend umbenennen (z. B. „Nacht“, „Notlicht“)", true);
            Num("LichtStil", "LightStyleNormal", "Stil Stellung Normal (0 = Spiel, 1 = warm, 2 = Notlicht)", 1, 0, 2, 1);
            Num("LichtStil", "LightStyleRed", "Stil Stellung Rot (0 = Spiel, 1 = warm, 2 = Notlicht)", 1, 0, 2, 1);
            Num("LichtStil", "LightStyleBlue", "Stil Stellung Blau (0 = Spiel, 1 = warm, 2 = Notlicht)", 2, 0, 2, 1);
            Num("LichtStil", "LightDayR", "Farbe LightDay R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightDayG", "Farbe LightDay G (0-255)", 232, 0, 255, 1);
            Num("LichtStil", "LightDayB", "Farbe LightDay B (0-255)", 184, 0, 255, 1);
            Num("LichtStil", "LightNightR", "Farbe LightNight R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightNightG", "Farbe LightNight G (0-255)", 214, 0, 255, 1);
            Num("LichtStil", "LightNightB", "Farbe LightNight B (0-255)", 130, 0, 255, 1);
            Num("LichtStil", "LightTowerR", "Farbe LightTower R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightTowerG", "Farbe LightTower G (0-255)", 0, 0, 255, 1);
            Num("LichtStil", "LightTowerB", "Farbe LightTower B (0-255)", 0, 0, 255, 1);
            Num("LichtStil", "LightEmergR", "Farbe LightEmerg R (0-255)", 255, 0, 255, 1);
            Num("LichtStil", "LightEmergG", "Farbe LightEmerg G (0-255)", 0, 0, 255, 1);
            Num("LichtStil", "LightEmergB", "Farbe LightEmerg B (0-255)", 0, 0, 255, 1);
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
                    else if (float.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) o.Value = o.Min < 0f ? Mathf.Max(o.Min, f) : Mathf.Max(0f, f); // no upper cap: INI may go beyond the menu range
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

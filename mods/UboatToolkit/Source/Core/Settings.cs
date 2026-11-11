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
        public bool IsBool;
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
            Bool(QoL, "NoAutoCarryWounded", "Verletzte nicht automatisch wegtragen (Iron Coffins H)", false);
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
            Num(QoL, "VentilationNoise", "Lüftung: Lautstärke/Geräusch x (1 = normal) (#36)", 0.3f, 0.05f, 1, 0.05f);
            Num(QoL, "AbsorberDuration", "  Absorber (Kalipatrone) hält x länger", 2, 1, 10, 0.5f);
            Bool(QoL, "CompressorNoAir", "  E-Kompressor verbraucht keine Luft im Boot", true);
            Bool(QoL, "AutoVentilation", "  Unter Wasser: Lüftung automatisch an + Absorber einsetzen", true);
            Bool(QoL, "EmergencyOxygen", "  Notfall-Sauerstoff: etwas mehr als der Verbrauch, wenn die Luft sinkt", true);
            Bool(QoL, "FullCrewAtStart", "Volle Besatzung ab Start (vor der ersten Feindfahrt) (#37)", true);
            Num(QoL, "FullCrewSize", "  Gewünschte Besatzung (Männer, in der INI ohne Obergrenze)", 90, 10, 200, 1);
            Bool(QoL, "AutoPump", "Pumpe schaltet sich bei Wasser im Boot selbst an (#38)", true);
            Bool(QoL, "PortLoadingFix", "Hafen-Grundfix: kein Einfrieren beim Verladen, hängende Aufträge werden gelöst (#39)", true);
            Num(QoL, "PortLoadingStuckSeconds", "  Hängt das Verladen so viele Sekunden: neu anstoßen, dann abbrechen", 30, 5, 300, 5);
            Bool(QoL, "CancelPortLoading", "  >> Verladen jetzt abbrechen (Schalter springt zurück)", false);
            Bool(QoL, "FixMyDamnShipment", "  >> FIX MY DAMN SHIPMENT! Alles Bestellte sofort ins Boot zwingen", false);
            Bool(QoL, "InstantPortLoading", "Hafen: alles sofort im Boot, keine Wartezeit", true);
            Num(QoL, "RebreatherDurability", "Atemgeräte halten x-mal länger (Iron Coffins J)", 2, 1, 10, 0.5f);
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
            Bool(Cheat, "CheatImmortalCrew", "Crew unsterblich (verliert keine Gesundheit)", false);
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
            Num(Cheat, "CheatMoney", "Geld: nie weniger als (0 = aus)", 0, 0, 1000000, 10000);
            Num(Cheat, "CheatReputation", "Ruf-Punkte: nie weniger als (0 = aus)", 0, 0, 1000, 10);
            Bool(Cheat, "CheatShowActive", "Aktive Cheats oben links anzeigen", true);

            // ---------- Tools ----------
            Bool(Tools, "DebugLog", "Test-Log: alles, was der Mod tut + Fehler (logs\\toolkit-Datum.log)", true);
            Bool(Tools, "PerfLogger", "Performance-Logger (schreibt CSV, kostet wenig Leistung)", false);
            Bool(Tools, "PerfHotspots", "  Bremsen-Finder (Taste F11 an/aus, kostet Leistung)", false);
        }

        private static void Bool(string tab, string key, string label, bool def) => Add(new Opt { Tab = tab, Key = key, Label = label, IsBool = true, Default = def ? 1 : 0, Value = def ? 1 : 0, Min = 0, Max = 1, Step = 1 });
        private static void Num(string tab, string key, string label, float def, float min, float max, float step) => Add(new Opt { Tab = tab, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
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
                    sb.AppendLine(o.Key + "=" + (o.IsBool ? (o.On ? "1" : "0") : o.Value.ToString("0.###", CultureInfo.InvariantCulture)));
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

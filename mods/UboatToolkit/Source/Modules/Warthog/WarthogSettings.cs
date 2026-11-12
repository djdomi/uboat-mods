using System.Linq;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules.Warthog
{
    // All options of the "Warthog" tab (U-1337, class Type XXX).
    public static class WarthogSettings
    {
        public const string Tab = "Warthog";

        static void Bool(string key, string label, bool def) => Settings.AddOpt(new Opt { Tab = Tab, Key = key, Label = label, IsBool = true, Default = def ? 1 : 0, Value = def ? 1 : 0, Min = 0, Max = 1, Step = 1 });
        static void Num(string key, string label, float def, float min, float max, float step) => Settings.AddOpt(new Opt { Tab = Tab, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
        static void Key(string key, string label, string def) => Settings.AddOpt(new Opt { Tab = Settings.KeysTab, Key = key, Label = label, IsKey = true, Text = def });

        public static void Register()
        {
            Bool("WhOnlyWarthog", "Alle Warthog-Funktionen nur auf U-1337 (Type XXX), nicht auf anderen Booten", true);

            // --- Rumpf
            Num("WhRubberCoating", "Gummibeschichtung: Sonar-Ortbarkeit x (1 = ohne, 0 = unsichtbar)", 0.3f, 0f, 1f, 0.05f);
            Num("WhHydroNoise", "Gummibeschichtung: Horch-Ortbarkeit x (1 = normal)", 0.6f, 0f, 1f, 0.05f);
            Bool("WhSecondHatch", "Zweites Turmluk + Seitentüren Deck <-> Turm (Crew nimmt den kürzesten Weg)", true);
            Bool("WhFins", "Finnen: engere Kurven, weniger Rollen, schneller Tiefe wechseln", true);
            Num("WhFinTurn", "  Kurven: Drehkraft x", 1.6f, 1f, 4f, 0.1f);
            Num("WhFinRoll", "  Rollen: Dämpfung x", 2.5f, 1f, 6f, 0.1f);
            Num("WhFinDepth", "  Tiefenruder-Wirkung x", 1.6f, 1f, 4f, 0.1f);

            // --- Antrieb
            Bool("WhThrusters", "Strahlruder vorn + achtern", true);
            Bool("WhThrusterWithRudder", "  Strahlruder helfen automatisch beim Ruderlegen", true);
            Num("WhThrusterPower", "  Stärke (Drehmoment in MNm)", 6f, 0.5f, 30f, 0.5f);
            Num("WhThrusterEnergy", "  Strom pro Sekunde volle Leistung (Anteil Batterie x 0,0001)", 2f, 0f, 20f, 0.5f);
            Bool("WhThrusterHoldFiring", "  Kurs halten, während Geschütze feuern (gegen Rückstoß)", true);

            // --- Schnorchel
            Bool("WhSnorkelTelescope", "Teleskop-Schnorchel: tiefer als Sehrohrtiefe", true);
            Num("WhSnorkelExtra", "  zusätzliche Länge in m", 6f, 0f, 15f, 0.5f);
            Bool("WhSnorkelAuto", "  fährt bei Wellen automatisch weiter aus", true);
            Num("WhSnorkelExhaust", "  Abgas unter Wasser: Sichtbarkeit x (0 = unsichtbar)", 0f, 0f, 1f, 0.05f);

            // --- Pumpen
            Bool("WhPumps", "Eine Lenzpumpe pro Abteil, mit Schwimmer (springt selbst an)", true);
            Num("WhPumpStart", "  Schwimmer: an ab x m³ Wasser im Abteil", 0.05f, 0.01f, 2f, 0.01f);
            Num("WhPumpRate", "  Leistung je Pumpe in m³/s (offenes Turmluk getaucht ≈ 0,2)", 0.5f, 0.05f, 5f, 0.05f);
            Bool("WhPumpDepthScale", "  Leistung steigt mit der Tiefe (gegen den Wasserdruck)", true);
            Num("WhPumpEnergy", "  Strom je Pumpe bei voller Leistung (Anteil x 0,0001/s)", 1f, 0f, 10f, 0.5f);

            // --- Luft + Strom
            Bool("WhSilentVent", "Lüftung immer an und lautlos, auch bei Schleichfahrt", true);
            Bool("WhElectrolysis", "Sauerstoff per Elektrolyse: keine Kalipatronen mehr", true);
            Bool("WhAllGenerate", "Alle Verbraucher generieren Strom", false);
            WarthogPower.RegisterOptions(Num); // one indented line per device under the top switch

            // --- Sonar
            Bool("WhPassive", "Passives Sonar 360°: immer an, auch ohne Horcher", true);
            Bool("WhPassiveSame", "  aufgetaucht und bei hoher Fahrt genauso gut", true);
            Num("WhPassiveRange", "  Reichweite x", 2f, 0.5f, 5f, 0.25f);
            Bool("WhPassiveMap", "  Kontakte auf der Karte zeigen", true);
            Bool("WhPassiveAlarm", "  Alarm bei neuem Kontakt", true);
            Bool("WhSonarBow", "Aktives Sonar Bug (nach vorn + unten)", true);
            Bool("WhSonarMid", "Sonar Mitte (nur passiv)", true);
            Bool("WhSonarStern", "Aktives Sonar Heck (nach hinten + unten)", true);
            Bool("WhPingReveals", "  Ping verrät uns (Gegner hören den Ping)", true);

            // --- Torpedos
            Bool("WhAutoLoad", "Automatischer Torpedolader: Rohre laden ohne Crew, Außenbehälter füllen innen nach", true);
            Num("WhAutoLoadSeconds", "  Nachladezeit pro Rohr in Sekunden (0 = sofort, Typ XXI hydraulisch ≈ 120)", 30, 0, 120, 5);

            // --- Waffen
            Num("WhCaliber", "Deckgeschütze: Kaliber (88 oder 105 mm)", 105, 88, 105, 17);
            Bool("WhOverkill", "Overkill: Vierlingsrohre statt Zwillinge", false);
            Num("WhFlakDepression", "Flak: Absenkung bis x Grad (unter Horizont)", 45, 5, 60, 5);
            Bool("WhOwnBoatGuard", "  Feuerschutz: nie ins eigene Boot schießen", true);
            Bool("WhRemoteGuns", "Geschütze aus dem Feuerleitstand fernbedienen (aus = klassisch an Deck)", true);
            Num("WhAirDetect", "Flugzeuge orten bis x km", 100, 10, 150, 5);
            Num("WhAirEngage", "Flugzeuge beschießen ab x km", 25, 2, 40, 1);

            Key("KeyWhThrustLeft", "Warthog: Strahlruder links", "Z");
            Key("KeyWhThrustRight", "Warthog: Strahlruder rechts", "X");
        }

        // True when the Warthog features should act on this ship.
        public static bool Active(PlayerShip ship)
        {
            if (ship == null) return false;
            if (!Settings.B("WhOnlyWarthog")) return true;
            return IsWarthog(ship);
        }

        // By the game's entity type ("Type XXX (Player)"), and by the object name as a fallback.
        public static bool IsWarthog(PlayerShip ship)
        {
            if (ship == null) return false;
            try
            {
                var t = ship.SandboxEntity != null ? ship.SandboxEntity.Type : null;
                if (t != null && t.Name != null && t.Name.StartsWith("Type XXX")) return true;
            }
            catch { }
            return ship.gameObject.name.StartsWith("Type XXX");
        }
    }

    // Modules to add in ToolkitRunner.Awake():  foreach (var m in WarthogModules.All()) Modules.Add(m);
    public static class WarthogModules
    {
        public static IModule[] All() => new IModule[] { new WarthogHull(), new WarthogPumps(), new WarthogPower(), new WarthogAir(), new WarthogSonar(), new WarthogSnorkel(), new WarthogWeapons(), new WarthogTorpedoes(), new WarthogHatches() };
    }
}

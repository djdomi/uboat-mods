using System.Linq;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules.Warthog
{
    // All options of the "Warthog" tab (U-1337, class Type XXX).
    public static class WarthogSettings
    {
        public const string Tab = "Warthog";

        static void Bool(string key, string label, bool def) { def = def && key == "WhOnlyWarthog"; BoolRaw(key, label, def); }
        static void BoolRaw(string key, string label, bool def) => Settings.AddOpt(new Opt { Tab = Tab, Key = key, Label = label, IsBool = true, Default = def ? 1 : 0, Value = def ? 1 : 0, Min = 0, Max = 1, Step = 1 });
        static void Num(string key, string label, float def, float min, float max, float step) => Settings.AddOpt(new Opt { Tab = Tab, Key = key, Label = label, Default = def, Value = def, Min = min, Max = max, Step = step });
        static void Key(string key, string label, string def) => Settings.AddOpt(new Opt { Tab = Settings.KeysTab, Key = key, Label = label, IsKey = true, Text = def });

        public static void Register()
        {
            Bool("WhOnlyWarthog", "Alle Warthog-Funktionen nur auf U-1337 (Type XXX), nicht auf anderen Booten", true);

            // --- Rumpf
            Num("WhRubberCoating", "Gummibeschichtung: Sonar-Ortbarkeit x (1 = ohne, 0 = unsichtbar)", 0.3f, 0f, 1f, 0.05f);
            Num("WhHydroNoise", "Gummibeschichtung: Horch-Ortbarkeit x (1 = normal)", 0.6f, 0f, 1f, 0.05f);
            Bool("WhFins", "Finnen: engere Kurven, weniger Rollen, schneller Tiefe wechseln", true);
            Num("WhFinTurn", "  Kurven: Drehkraft x", 1.6f, 1f, 4f, 0.1f);
            Num("WhFinRoll", "  Rollen: Dämpfung x", 2.5f, 1f, 6f, 0.1f);
            Num("WhFinDepth", "  Tiefenruder-Wirkung x", 1.6f, 1f, 4f, 0.1f);

            // --- Antrieb
            Bool("WhThrusters", "Strahlruder vorn + achtern (alle Boote)", true);
            Bool("WhThrusterWithRudder", "  Strahlruder helfen automatisch beim Ruderlegen", true);
            Num("WhThrusterPower", "  Stärke (6 = ca. 0,6 Grad/s² Drehbeschleunigung)", 6f, 0.5f, 30f, 0.5f);
            Num("WhThrusterEnergy", "  Strom pro Sekunde volle Leistung (Anteil Batterie x 0,0001)", 2f, 0f, 20f, 0.5f);
            Bool("WhThrustGauge", "  Strahlruder-Anzeige (wie die Tiefen-Anzeige)", true);
            Num("WhThrustGaugeX", "    Anzeige: Position von links in %", 2, 0, 95, 1);
            Num("WhThrustGaugeY", "    Anzeige: Position von oben in %", 62, 0, 95, 1);
            Bool("WhThrusterHoldFiring", "  Kurs halten, während Geschütze feuern (gegen Rückstoß)", true);
            Num("WhSpeedFactor", "Geschwindigkeit: Faktor auf die Schraubenleistung (1,0 = wie gebaut)", 1f, 0.5f, 2f, 0.05f);
            Num("WhDiveSpeed", "Tauch-/Steiggeschwindigkeit: Faktor auf die Tiefenruder-Kraft (1,0 = wie ein IXC, auch in großer Tiefe)", 4f, 0.25f, 10f, 0.25f);
            Num("WhCrushDepth", "Zerstörungstiefe in m (befohlene Tiefe wird auf 100 m darüber begrenzt)", 3000f, 500f, 6000f, 100f);
            Bool("WhVThrusters", "Vertikal-Strahlruder vorn + achtern (alle Boote): Boot heben/senken", true);
            Bool("WhVThrustAuto", "  helfen automatisch, die befohlene Tiefe zu erreichen", true);
            Bool("WhVThrustLevel", "  Nase waagerecht halten (gegen Bug-hoch bei hoher Fahrt), an der Oberfläche", true);
            Num("WhVThrustPower", "  Stärke (% vom Bootsgewicht)", 10f, 1f, 100f, 1f);
            Bool("WhThrustShiftSide", "  Shift + Z/X: ganzes Boot seitwärts (Anlegen); Z/X ohne Shift: auf der Stelle drehen", true);
            Num("WhThrustBowManual", "  Bug-Strahlruder einzeln (-100 = Bb, +100 = Stb, 0 = aus)", 0, -100, 100, 5);
            Num("WhThrustSternManual", "  Heck-Strahlruder einzeln (-100 = Bb, +100 = Stb, 0 = aus)", 0, -100, 100, 5);

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

            // --- Luken
            Bool("WhHatchAll", "Luken: alle Luken benutzbar (2. Turmluk, Seitentüren, Torpedoluken, Kombüse; aus = Spiel mit einem Luk)", true);
            Bool("WhHatchSingle", "  Luken einzeln steuern (Liste oben im Reiter; aus = alle folgen Turmluk 1)", false);

            // --- Hafen
            Num("WhDockShift", "Liegeplatz: Boot x m nach vorn schieben (IXC = 5; 14 = Heck wie beim IXC)", 14, -10, 30, 1);
            Num("WhDockSide", "Liegeplatz: Boot x m seitlich schieben (+ = Steuerbord)", 0, -5, 5, 0.5f);

            // --- Netzsäge + Anker
            Bool("WhNetSaw", "Netzsäge: U-Boot-Netze durchschneiden (Bug + Heck)", true);
            Bool("WhAnchor", "Anker benutzen (Knopf oben im Reiter oder Taste)", true);
            Num("WhAnchorMode", "  welcher Anker: 0 = Bug, 1 = Heck, 2 = beide", 0, 0, 2, 1);
            Num("WhAnchorChain", "  Kettenlänge in m", 150, 20, 400, 10);
            Num("WhAnchorLiftSeconds", "  Anker lichten dauert x Sekunden (0 = sofort)", 30, 0, 120, 5);

            // --- Feuerleitstand
            Bool("WhFireControlCrew", "Feuerleitstand: Geschützgruppe nur bereit, wenn ihr Platz besetzt ist", false);

            // --- Torpedos
            Bool("WhAutoLoad", "Automatischer Torpedolader: Rohre laden ohne Crew, Außenbehälter füllen innen nach", true);
            Num("WhAutoLoadSeconds", "  Nachladezeit pro Rohr in Sekunden (0 = sofort, Typ XXI hydraulisch ≈ 120)", 30, 0, 120, 5);

            // --- Waffen
            // a switch, not a slider: the Toolkit slider rounds to multiples of the step (17 -> 85 / 102 mm)
            Bool("WhCaliber88", "Deckgeschütze: Kaliber 8,8 cm statt 10,5 cm", false);
            Bool("WhOverkill", "Overkill: Vierlingsrohre statt Zwillinge", false);
            Num("WhFlakDepression", "Flak: Absenkung bis x Grad (unter Horizont)", 45, 5, 60, 5);
            Bool("WhOwnBoatGuard", "  Feuerschutz: nie ins eigene Boot schießen", true);
            Bool("WhRemoteGuns", "Geschütze aus dem Feuerleitstand fernbedienen (aus = klassisch an Deck)", true);
            Num("WhAirDetect", "Flugzeuge orten bis x km", 100, 10, 150, 5);
            Num("WhAirEngage", "Flugzeuge beschießen ab x km", 25, 2, 40, 1);

            Key("KeyWhThrustLeft", "Warthog: Strahlruder links", "Z");
            Key("KeyWhThrustRight", "Warthog: Strahlruder rechts", "X");
            Key("KeyWhVThrustUp", "Vertikal-Strahlruder: Bug hoch (mit Shift: ganzes Boot hoch)", "Ctrl+Alt+Home");
            Key("KeyWhVThrustDown", "Vertikal-Strahlruder: Bug runter (mit Shift: ganzes Boot runter)", "Ctrl+Alt+End");
            Key("KeyWhAnchor", "Warthog: Anker werfen / lichten", "J");
        }

        // True when the Warthog features should act on this ship.
        public static bool Active(PlayerShip ship)
        {
            if (ship == null || Util.WarthogModPresent) return false;
            if (!Settings.B("WhOnlyWarthog")) return true;
            return IsWarthog(ship);
        }

        // Thrusters work on EVERY boat (DoMe); only skipped on the Warthog when the Warthog mod brings its own code.
        public static bool ThrustersActive(PlayerShip ship) => ship != null && !Util.WarthogModOwns(ship);

        // By the game's entity type ("Type XXX (Player)"), and by the object name as a fallback.
        private static object logged;
        public static bool IsWarthog(PlayerShip ship)
        {
            if (ship == null) return false;
            if (!ReferenceEquals(logged, ship))
            {
                logged = ship;
                string tn = "?";
                try { tn = ship.SandboxEntity?.Type?.Name ?? "null"; } catch { }
                Util.Log("[Warthog] Boot: Objekt '" + ship.gameObject.name + "', Typ '" + tn + "'");
            }
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
        public static IModule[] All() => new IModule[] { new WarthogHull(), new WarthogVThrusters(), new WarthogPumps(), new WarthogPower(), new WarthogAir(), new WarthogSonar(), new WarthogSnorkel(), new WarthogWeapons(), new WarthogTorpedoes(), new WarthogHatches(),
            new WarthogNetCutter(), new WarthogAnchor(), new WarthogFireControl() };
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using UBOAT.Game.Core.Data;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Power menu:
    //   [x] Alle Verbraucher generieren Strom
    //         Lüftung        verbraucht / kostenlos / erzeugt
    //         (every device of the boat that uses battery, one line each)
    // How: every game device that uses power keeps a private Modifier on the boat's "Energy" gain
    // (energyModifier / energyGainModifier / energyUseModifier). The game writes a negative value there
    // (= use). We remember the game's value and replace it by 0 (free) or +|value| (generate).
    public class WarthogPower : ModuleBase
    {
        // device type name -> label in the menu
        internal static readonly (string type, string label)[] Devices =
        {
            ("Ventilation", "Lüftung"),
            ("TrimPump", "Trimm-/Lenzpumpe (Zentrale)"),
            ("AirCompressor", "Kompressor"),
            ("Gyrocompass", "Kreiselkompass"),
            ("DivingPlanesStation", "Tiefenruder"),
            ("PlayerShipEngine", "E-Maschinen (Fahrstrom)"),
            ("WarthogThrusters", "Strahlruder"),
            ("WarthogPumps", "Lenzpumpen pro Abteil"),
            ("WarthogSonar", "Sonar-Anlagen"),
        };

        public static void RegisterOptions(Action<string, string, float, float, float, float> num)
        {
            foreach (var d in Devices)
                num("WhPwr_" + d.type, "      " + d.label + " (0 = verbraucht, 1 = kostenlos, 2 = erzeugt)", 0, 0, 2, 1);
        }

        // 0 consume, 1 free, 2 generate (the top switch forces 2 for everything)
        internal static int Mode(string type)
        {
            if (!WarthogSettings.Active(Util.PlayerShip)) return 0;
            if (Settings.B("WhAllGenerate")) return 2;
            return Mathf.RoundToInt(Settings.F("WhPwr_" + type));
        }

        // apply a mode to a "use" value (positive number = power used per second)
        internal static float Apply(string type, float use)
        {
            switch (Mode(type)) { case 1: return 0f; case 2: return -Mathf.Abs(use); default: return use; }
        }

        private class Slot { public Modifier M; public float Game; public float Written = float.NaN; }
        private readonly Dictionary<Modifier, Slot> slots = new Dictionary<Modifier, Slot>();
        private static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            foreach (var d in Devices)
            {
                if (d.type.StartsWith("Warthog")) continue; // own consumers apply the mode themselves
                int mode = Mode(d.type);
                foreach (var comp in DevicesOfType(d.type))
                    foreach (var f in EnergyFields(comp.GetType()))
                    {
                        var m = f.GetValue(comp) as Modifier;
                        if (m == null) continue;
                        if (!slots.TryGetValue(m, out var s)) { s = new Slot { M = m, Game = m.Value }; slots[m] = s; }
                        // the game changed the value since we wrote -> that is the new game value
                        if (float.IsNaN(s.Written) || m.Value != s.Written) s.Game = m.Value;
                        float want = mode == 1 ? 0f : mode == 2 ? Mathf.Abs(s.Game) : s.Game;
                        if (m.Value != want) m.Value = want;
                        s.Written = want;
                    }
            }
        }

        private static readonly Dictionary<string, Type> typeByName = new Dictionary<string, Type>();
        private static Component[] DevicesOfType(string name)
        {
            if (!typeByName.TryGetValue(name, out var t))
            {
                t = typeof(UBOAT.Game.Scene.Items.Ventilation).Assembly.GetType("UBOAT.Game.Scene.Items." + name);
                typeByName[name] = t;
            }
            if (t == null) return new Component[0];
            var ship = Util.PlayerShip;
            // searching the boat every frame is slow: keep the list 5 s (or until the boat changes)
            if (cacheShip != ship) { compCache.Clear(); cacheShip = ship; }
            if (compCache.TryGetValue(name, out var c) && Time.unscaledTime - c.at < 5f) return c.list;
            var list = ship.GetComponentsInChildren(t, true);
            compCache[name] = (list, Time.unscaledTime);
            return list;
        }
        private static object cacheShip;
        private static readonly Dictionary<string, (Component[] list, float at)> compCache = new Dictionary<string, (Component[] list, float at)>();

        private static FieldInfo[] EnergyFields(Type t)
        {
            if (fieldCache.TryGetValue(t, out var fs)) return fs;
            var list = new List<FieldInfo>();
            for (var x = t; x != null && x != typeof(MonoBehaviour); x = x.BaseType)
                foreach (var f in x.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (f.FieldType == typeof(Modifier) && f.Name.IndexOf("energy", StringComparison.OrdinalIgnoreCase) >= 0 && f.Name != "energyLinkModifier")
                        list.Add(f);
            return fieldCache[t] = list.ToArray();
        }
    }
}

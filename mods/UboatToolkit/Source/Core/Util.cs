using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit
{
    public static class Util
    {
        // 0.3.79: fast type lookup by name. Harmony's TypeByName walks every loaded type on each call (0.2-0.4 s when the
        // type does not exist, e.g. another mod that is not installed); measured 15.3 s of patching at game start, most of
        // it these lookups. Here all types are listed once; the list is rebuilt only when a new assembly was loaded.
        private static Dictionary<string, Type> typeByFull, typeByShort;
        private static int typeAsmCount = -1;
        public static Type FindType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            try
            {
                var direct = Type.GetType(name, false);
                if (direct != null) return direct;
                int n = 0; foreach (var a in AccessTools.AllAssemblies()) n++;
                if (typeByFull == null || n != typeAsmCount)
                {
                    var full = new Dictionary<string, Type>(); var shortN = new Dictionary<string, Type>();
                    foreach (var t in AccessTools.AllTypes())
                    {
                        if (t == null) continue;
                        if (t.FullName != null && !full.ContainsKey(t.FullName)) full[t.FullName] = t;
                        if (t.Name != null && !shortN.ContainsKey(t.Name)) shortN[t.Name] = t;
                    }
                    typeByFull = full; typeByShort = shortN; typeAsmCount = n;
                }
                if (typeByFull.TryGetValue(name, out var r)) return r;
                return typeByShort.TryGetValue(name, out r) ? r : null;
            }
            catch { return AccessTools.TypeByName(name); }
        }

        public const string ModifierName = "UboatToolkit";

        private static PlayerShip playerShip;

        // The player's boat, or null in menus / port loading.
        public static PlayerShip PlayerShip
        {
            get
            {
                // Searching the whole scene is slow: in menus (no boat) only try once per second.
                if (playerShip == null && Time.unscaledTime >= nextShipSearch)
                {
                    nextShipSearch = Time.unscaledTime + 1f;
                    playerShip = UnityEngine.Object.FindObjectOfType<PlayerShip>();
                    if (playerShip != null) shipFoundAt = Time.unscaledTime;
                }
                return playerShip;
            }
        }

        private static float shipFoundAt;

        // Number of slots of a storage, or 0 while the storage is not set up yet (would crash otherwise).
        public static int Slots(UBOAT.Game.Scene.Items.Storage s)
        {
            if (s == null) return 0;
            try { return s.SlotCount; } catch { return 0; }
        }

        // True once the boat has existed for a few seconds: right after loading many parts are
        // still being built, and touching them then gives "Object reference not set" errors.
        public static bool ShipReady => PlayerShip != null && Time.unscaledTime - shipFoundAt >= 5f;

        private static float nextShipSearch;
        private static PlayerShip cacheShip;
        private static readonly Dictionary<Type, Array> found = new Dictionary<Type, Array>();
        private static readonly Dictionary<Type, float> foundAt = new Dictionary<Type, float>();

        // All parts of type T on the player's boat. Searching the boat is slow, so the result is kept
        // for 10 seconds (or until one of the parts is gone). Saves many small stutters.
        // 0.4.7: how often and how long the boat was searched (logged once per minute with the Toolkit cost line)
        internal static int FindScans; internal static double FindSeconds;
        public static T[] Find<T>(float maxAge = 10f) where T : Component
        {
            var ship = PlayerShip;
            if (ship == null) return new T[0];
            if (cacheShip != ship) { found.Clear(); foundAt.Clear(); cacheShip = ship; }
            var t = typeof(T);
            if (found.TryGetValue(t, out var a) && Time.unscaledTime - foundAt[t] < maxAge)
            {
                var arr = (T[])a;
                bool ok = true;
                for (int i = 0; i < arr.Length; i++) if (arr[i] == null) { ok = false; break; }
                if (ok) return arr;
            }
            double f0 = Time.realtimeSinceStartupAsDouble; FindScans++;
            var res = ship.GetComponentsInChildren<T>(true);
            // Interior devices (pumps, racks, ...) live in a separate "<Name> Interior" scene: add them too.
            try
            {
                var inner = ship.Interior;
                if (inner != null && !inner.transform.IsChildOf(ship.transform))
                {
                    var more = inner.GetComponentsInChildren<T>(true);
                    if (more.Length > 0)
                    {
                        var all = new List<T>(res);
                        var seen = new HashSet<T>(res);
                        foreach (var m in more) if (seen.Add(m)) all.Add(m);
                        res = all.ToArray();
                    }
                }
            }
            catch { }
            found[t] = res; foundAt[t] = Time.unscaledTime;
            FindSeconds += Time.realtimeSinceStartupAsDouble - f0;
            return res;
        }

        // 0.4.6: same as Find<T>, for a type only known by name (boat + its separate interior scene). Not cached.
        public static Component[] FindByType(Type t)
        {
            var ship = PlayerShip;
            if (ship == null || t == null) return new Component[0];
            var res = ship.GetComponentsInChildren(t, true);
            try
            {
                var inner = ship.Interior;
                if (inner != null && !inner.transform.IsChildOf(ship.transform))
                {
                    var more = inner.GetComponentsInChildren(t, true);
                    if (more.Length > 0)
                    {
                        var all = new List<Component>(res);
                        var seen = new HashSet<Component>(res);
                        foreach (var m in more) if (seen.Add(m)) all.Add(m);
                        res = all.ToArray();
                    }
                }
            }
            catch { }
            return res;
        }

        // true while the normal game view (boat, map, first person, manual device) is shown; false in port menus,
        // ship selection, headquarters, dialogues, cutscenes and other fullscreen UIs: our gauges are not drawn there.
        private static UBOAT.Game.UI.GameUI gameUi; private static float gameUiAt = -99f;
        private static UBOAT.Game.UI.ShipSelectorUI shipSel; private static float shipSelAt = -99f;
        public static bool InGameView()
        {
            if (gameUi == null && Time.unscaledTime - gameUiAt > 5f) { gameUiAt = Time.unscaledTime; gameUi = UnityEngine.Object.FindObjectOfType<UBOAT.Game.UI.GameUI>(); }
            if (shipSel == null && Time.unscaledTime - shipSelAt > 5f) { shipSelAt = Time.unscaledTime; shipSel = UnityEngine.Object.FindObjectOfType<UBOAT.Game.UI.ShipSelectorUI>(true); }
            if (shipSel != null && shipSel.isActiveAndEnabled) return false;   // ship selection open
            if (gameUi == null) return true;
            var m = gameUi.CurrentMode;
            return m == UBOAT.Game.UI.GameUI.Mode.Normal || m == UBOAT.Game.UI.GameUI.Mode.Map || m == UBOAT.Game.UI.GameUI.Mode.FPP
                || m == UBOAT.Game.UI.GameUI.Mode.FPPEx || m == UBOAT.Game.UI.GameUI.Mode.DeviceManualMode;
        }

        // "Type XXX" = the Warthog boat type (string check only, no reference to the Warthog mod).
        public static bool IsTypeXXX(PlayerShip ship)
        {
            if (ship == null) return false;
            try { var t = ship.SandboxEntity != null ? ship.SandboxEntity.Type : null; if (t != null && t.Name != null) return t.Name.StartsWith("Type XXX"); } catch { }
            return ship.gameObject.name.StartsWith("Type XXX");
        }

        // True when the Warthog mod brings its own Warthog code (namespace "Warthog", 0.3.9+). Then the Toolkit's
        // built-in Warthog parts stay off, so nothing runs twice; with an older Warthog (or none) the Toolkit's own
        // parts do the work. Both mods run alone and in any version combination (DoMe 2026-11-13).
        private static int warthogMod = -1;
        public static bool WarthogModPresent
        {
            get
            {
                if (warthogMod < 0)
                {
                    warthogMod = 0;
                    try
                    {
                        // No System.AppDomain here (the game forbids it in mod code): Harmony lists the loaded types.
                        var own = typeof(Util).Assembly;
                        foreach (var t in HarmonyLib.AccessTools.AllTypes())
                        {
                            if (t == null || t.Assembly == own) continue;
                            var ns = t.Namespace;
                            if (ns == "Warthog" || ns == "UboatToolkit.Modules.Warthog" || (ns != null && ns.StartsWith("Warthog."))) { warthogMod = 1; break; }
                        }
                    }
                    catch { }
                    Log("Warthog-Mod mit eigenem Code: " + (warthogMod == 1 ? "ja (Toolkit-Warthog-Teile bleiben aus)" : "nein (Toolkit-Warthog-Teile aktiv)"));
                }
                return warthogMod == 1;
            }
        }
        public static bool WarthogModOwns(PlayerShip ship) => WarthogModPresent && IsTypeXXX(ship);

        // true for parts of the player's boat, also for devices in the separate interior scene
        // (the game itself finds the owner the same way: SceneExtensions.GetParentShip).
        private static readonly Dictionary<int, bool> ownerCache = new Dictionary<int, bool>();
        private static PlayerShip ownerShip;
        public static bool IsPlayers(Component c)
        {
            var ship = PlayerShip;
            if (ship == null || c == null) return false;
            if (ownerShip != ship) { ownerCache.Clear(); ownerShip = ship; }
            var tr = c.transform;
            if (tr.IsChildOf(ship.transform)) return true;
            int id = c.GetInstanceID();
            bool r;
            if (ownerCache.TryGetValue(id, out r)) return r;
            r = false;
            try
            {
                var inner = ship.Interior;
                if (inner != null && tr.IsChildOf(inner.transform)) r = true;
                else
                {
                    var pi = c.GetComponentInParent<UBOAT.Game.Scene.Spaces.PlayerShipInterior>();
                    if (pi != null && pi.Ship == ship) r = true;
                }
            }
            catch { }
            if (ownerCache.Count > 20000) ownerCache.Clear();
            ownerCache[id] = r;
            return r;
        }

        // Makes a game parameter end up at "target" (or back to the game value when target <= 0),
        // using a scale modifier that the game itself understands.
        public static void SetParameter(Parameter p, float target)
        {
            if (p == null) return;
            var m = p.GetScaleModifier(ModifierName, true);
            if (target <= 0f) { m.Value = 1f; return; }
            float baseValue = p.GetValueExcludingModifier(ModifierName);
            m.Value = Mathf.Abs(baseValue) > 0.0001f ? target / baseValue : 1f;
        }

        // PlayerCareer is a ScriptableObject, so FindObjectOfType may miss it; FindObjectsOfTypeAll always finds it.
        private static UBOAT.Game.Sandbox.PlayerCareer career;
        private static float careerRetry;
        public static UBOAT.Game.Sandbox.PlayerCareer Career
        {
            get
            {
                // FindObjectsOfTypeAll searches every loaded object (40-100 ms). While no career exists (menu, loading)
                // it ran on every call; now at most every 10 s.
                if (career == null && Time.unscaledTime >= careerRetry)
                {
                    careerRetry = Time.unscaledTime + 10f;
                    career = System.Linq.Enumerable.FirstOrDefault(Resources.FindObjectsOfTypeAll<UBOAT.Game.Sandbox.PlayerCareer>());
                }
                return career;
            }
        }

        public static void ScaleParameter(Parameter p, float factor)
        {
            if (p == null) return;
            p.GetScaleModifier(ModifierName, true).Value = factor <= 0f ? 1f : factor;
        }

        // Remembers the original value of a private field per object, so a feature can be switched off again.
        private static readonly Dictionary<string, object> originals = new Dictionary<string, object>();

        public static T Original<T>(object obj, string field)
        {
            string key = obj.GetHashCode() + "/" + field;
            if (!originals.TryGetValue(key, out var v))
            {
                v = Traverse.Create(obj).Field(field).GetValue();
                if (v is float[] arr) v = arr.Clone();
                originals[key] = v;
            }
            return (T)v;
        }

        public static void SetField(object obj, string field, object value) => Traverse.Create(obj).Field(field).SetValue(value);

        public static void Log(string text)
        {
            Debug.Log(ToolkitLoader.Tag + " " + text);
            DebugLog.Write("INFO", text);
        }

        private static readonly Dictionary<string, int> errorCounts = new Dictionary<string, int>();

        public static T SafeGet<T>(Func<T> f, T fallback) { try { return f(); } catch { return fallback; } }

        public static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e0)
            {
                // errors of calls made through reflection (the whole menu) arrive wrapped: log the real one
                var e = e0;
                while (e is System.Reflection.TargetInvocationException && e.InnerException != null) e = e.InnerException;
                // Same error every second would flood the log: write it 3 times, then every 100th time.
                string key = what + "|" + e.Message;
                errorCounts.TryGetValue(key, out int n);
                errorCounts[key] = ++n;
                if (n <= 3 || n % 100 == 0)
                {
                    Debug.Log(ToolkitLoader.Tag + " error in " + what + " (x" + n + "): " + e.Message);
                    DebugLog.Write("ERROR", what + " (x" + n + "): " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                }
            }
        }
    }
}

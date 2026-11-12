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
                }
                return playerShip;
            }
        }

        private static float nextShipSearch;
        private static PlayerShip cacheShip;
        private static readonly Dictionary<Type, Array> found = new Dictionary<Type, Array>();
        private static readonly Dictionary<Type, float> foundAt = new Dictionary<Type, float>();

        // All parts of type T on the player's boat. Searching the boat is slow, so the result is kept
        // for 10 seconds (or until one of the parts is gone). Saves many small stutters.
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
            var res = ship.GetComponentsInChildren<T>(true);
            found[t] = res; foundAt[t] = Time.unscaledTime;
            return res;
        }

        public static bool IsPlayers(Component c)
        {
            var ship = PlayerShip;
            return ship != null && c != null && c.transform.IsChildOf(ship.transform);
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
        public static UBOAT.Game.Sandbox.PlayerCareer Career
        {
            get
            {
                if (career == null) career = System.Linq.Enumerable.FirstOrDefault(Resources.FindObjectsOfTypeAll<UBOAT.Game.Sandbox.PlayerCareer>());
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

        public static void Safe(string what, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
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

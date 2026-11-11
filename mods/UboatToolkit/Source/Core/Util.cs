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
                if (playerShip == null) playerShip = UnityEngine.Object.FindObjectOfType<PlayerShip>();
                return playerShip;
            }
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

// Crew-KI (Teil des UBOAT Toolkit): CrewAILoitering. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class LoiterToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewControlRoom");

        public static readonly bool EnforceIdleTravelDistance = false;
        public const float MaxIdleTravelMeters = 12f;

        public static readonly string[] RestrictedCompartments = { "Control" };

        public static readonly bool AllowShuffleIfAlreadyInside = true;

        public static readonly bool LogRejections = true;
    }

    internal static class LoiterFilter
    {
        private static readonly Vector3 NoDestination =
            new Vector3(float.NaN, float.NaN, float.NaN);

        private static Compartment[] _compartments;
        private static float _cacheStamp;
        private const float CacheSeconds = 60f;   // unscaled

        public static bool IsNoDestination(Vector3 v) { return float.IsNaN(v.x); }
        public static Vector3 Reject() { return NoDestination; }

        private static Compartment[] Compartments()
        {
            try
            {
                float now = Time.unscaledTime;
                if (_compartments == null || now - _cacheStamp > CacheSeconds)
                {
                    _compartments = UnityEngine.Object.FindObjectsOfType<Compartment>();
                    _cacheStamp = now;
                }
            }
            catch { }
            return _compartments;
        }

        private static Compartment CompartmentAt(Vector3 point)
        {
            var all = Compartments();
            if (all == null) return null;
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (all[i] != null && all[i].Contains(point)) return all[i];
                }
                catch { }
            }
            return null;
        }

        private static bool IsRestricted(Compartment c)
        {
            if (c == null) return false;
            string n;
            try { n = c.name; } catch { return false; }
            if (string.IsNullOrEmpty(n)) return false;
            var list = LoiterToggles.RestrictedCompartments;
            for (int i = 0; i < list.Length; i++)
            {
                if (n.IndexOf(list[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static bool ShouldReject(PlayableCharacter character, Vector3 dest,
                                        bool enforceDistance, out string why)
        {
            why = null;
            if (!LoiterToggles.Enabled) return false;
            if (character == null) return false;
            if (IsNoDestination(dest)) return false;   // already "none"

            long t0 = Perf.Begin();
            try
            {
                Vector3 from = character.transform.position;

                if (enforceDistance && LoiterToggles.EnforceIdleTravelDistance)
                {
                    float d = Vector3.Distance(from, dest);
                    if (d > LoiterToggles.MaxIdleTravelMeters)
                    {
                        why = "distance " + d.ToString("F1") + "m > " +
                              LoiterToggles.MaxIdleTravelMeters + "m";
                        return true;
                    }
                }

                var destComp = CompartmentAt(dest);
                if (IsRestricted(destComp))
                {
                    Compartment cur = null;
                    try { cur = character.CurrentCompartment; } catch { }

                    bool alreadyInside = LoiterToggles.AllowShuffleIfAlreadyInside &&
                                         cur != null && destComp != null && cur == destComp;
                    if (!alreadyInside)
                    {
                        why = "restricted compartment '" +
                              (destComp != null ? destComp.name : "?") + "'";
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Loiter.ShouldReject", e);
                return false; // fail open
            }
            finally { Perf.End("Loiter.ShouldReject", t0); }
            return false;
        }

        private const float LogIntervalPerManSeconds = 60f;
        private const int MaxRejectionLines = 300;
        private static readonly Dictionary<int, float> _lastLoggedFor = new Dictionary<int, float>();
        private static readonly Dictionary<string, int> _tallies = new Dictionary<string, int>();
        private static readonly object _tallyLock = new object();
        private static int _rejectionLines;

        public static void NoteRejection(PlayableCharacter c, string source, string why)
        {
            if (!LoiterToggles.LogRejections) return;
            try
            {
                OptCounters.LoiterDestinationsRejected++;

                string where = "?";
                int head = -1;
                try
                {
                    var cur = c != null ? c.CurrentCompartment : null;
                    if (cur != null) { where = cur.name; head = cur.Characters != null ? cur.Characters.Count : -1; }
                    else where = "(outside)";
                }
                catch { }

                string key = where + " -> " + (why ?? "?");
                lock (_tallyLock)
                {
                    int n0;
                    _tallies.TryGetValue(key, out n0);
                    _tallies[key] = n0 + 1;
                }

                if (_rejectionLines >= MaxRejectionLines || c == null) return;
                int id = c.GetInstanceID();
                float now = Time.unscaledTime;
                float last;
                lock (_tallyLock)
                {
                    if (_lastLoggedFor.TryGetValue(id, out last) && now - last < LogIntervalPerManSeconds) return;
                    _lastLoggedFor[id] = now;
                }
                _rejectionLines++;
                string n = "unknown";
                try { n = c.Name; } catch { }
                DiagLog.Write("[Loiter] refused " + source + " for " + n + " standing in '" + where + "' (" +
                              head + " present) - " + why);
            }
            catch { }
        }

        public static string DrainTallies()
        {
            lock (_tallyLock)
            {
                if (_tallies.Count == 0) return null;
                var list = new List<KeyValuePair<string, int>>(_tallies);
                _tallies.Clear();
                list.Sort((a, b) => b.Value.CompareTo(a.Value));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < list.Count && i < 10; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i].Key).Append(" x").Append(list[i].Value);
                }
                return sb.ToString();
            }
        }
    }

    [HarmonyPatch(typeof(FreeSpaceBehaviour), "FindFreeIdleSlot")]
    internal static class Patch_Loiter_FindFreeIdleSlot
    {
        static readonly System.Reflection.FieldInfo CharacterField =
            AccessTools.Field(typeof(FreeSpaceBehaviour), "character");

        static void Postfix(FreeSpaceBehaviour __instance, ref Vector3 __result)
        {
            if (!LoiterToggles.Enabled) return;
            try
            {
                if (CharacterField == null) return;
                var c = CharacterField.GetValue(__instance) as PlayableCharacter;
                string why;
                if (LoiterFilter.ShouldReject(c, __result, true, out why))
                {
                    LoiterFilter.NoteRejection(c, "idle slot", why);
                    __result = LoiterFilter.Reject();
                }
            }
            catch (Exception e) { DiagLog.WriteError("Loiter.FindFreeIdleSlot", e); }
        }
    }

    [HarmonyPatch(typeof(FreeSpaceBehaviour), "FindFreeSpaceNearby")]
    internal static class Patch_Loiter_FindFreeSpaceNearby
    {
        static readonly System.Reflection.FieldInfo CharacterField =
            AccessTools.Field(typeof(FreeSpaceBehaviour), "character");

        static void Postfix(FreeSpaceBehaviour __instance, ref Vector3 __result)
        {
            if (!LoiterToggles.Enabled) return;
            try
            {
                if (CharacterField == null) return;
                var c = CharacterField.GetValue(__instance) as PlayableCharacter;
                string why;
                if (LoiterFilter.ShouldReject(c, __result, false, out why))
                {
                    LoiterFilter.NoteRejection(c, "nearby space", why);
                    __result = LoiterFilter.Reject();
                }
            }
            catch (Exception e) { DiagLog.WriteError("Loiter.FindFreeSpaceNearby", e); }
        }
    }
}

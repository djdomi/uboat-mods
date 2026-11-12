// Crew-KI (Teil des UBOAT Toolkit): CrewAIProximity. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Items;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class ProximityToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewNearbyJobs");

        public const float PerCompartmentFalloff = 0.7f;

        public const float MinScale = 0.30f;

        public const int MaxHops = 12;

        public static readonly bool LogSamples = true;
    }

    internal static class CompartmentGraph
    {
        private static readonly Dictionary<long, int> _hops = new Dictionary<long, int>();
        private static readonly Dictionary<int, Compartment> _activatorComp =
            new Dictionary<int, Compartment>();
        private static readonly object _lock = new object();

        private static long Key(int a, int b) { return ((long)a << 32) ^ (uint)b; }

        public static Compartment CompartmentOf(Activator activator)
        {
            if (activator == null) return null;
            try
            {
                int id = activator.GetInstanceID();
                Compartment cached;
                lock (_lock)
                {
                    if (_activatorComp.TryGetValue(id, out cached)) return cached;
                }

                Compartment found = null;
                var inter = activator.Interactable;
                if (inter != null)
                {
                    var slots = inter.Slots;
                    if (slots != null)
                    {
                        for (int i = 0; i < slots.Length && found == null; i++)
                        {
                            if (slots[i] != null) found = slots[i].Compartment;
                        }
                    }
                }

                lock (_lock) { _activatorComp[id] = found; }
                return found;
            }
            catch { return null; }
        }

        private static Dictionary<int, List<Compartment>> _adj;
        private static float _lastBuild;
        private static bool _rebuildRequested;

        private static Dictionary<int, List<Compartment>> Adjacency()
        {
            if (_adj != null && !_rebuildRequested) return _adj;

            long t0 = Perf.Begin();
            try
            {
                float now = Time.unscaledTime;
                if (_adj != null && now - _lastBuild < 120f) return _adj;
                _rebuildRequested = false;

                var map = new Dictionary<int, List<Compartment>>();
                Action<Compartment, Compartment> link = (a, b) =>
                {
                    if (a == null || b == null || a == b) return;
                    List<Compartment> l;
                    int ka = a.GetInstanceID();
                    if (!map.TryGetValue(ka, out l)) { l = new List<Compartment>(); map[ka] = l; }
                    if (!l.Contains(b)) l.Add(b);
                };

                var comps = UnityEngine.Object.FindObjectsOfType<Compartment>();
                if (comps != null)
                {
                    for (int i = 0; i < comps.Length; i++)
                    {
                        var c = comps[i];
                        if (c == null) continue;
                        Compartment[] nb = null;
                        try { nb = c.NeighbouringCompartments; } catch { }
                        if (nb == null) continue;
                        for (int n = 0; n < nb.Length; n++) { link(c, nb[n]); link(nb[n], c); }
                    }
                }

                var hatches = UnityEngine.Object.FindObjectsOfType<Hatch>();
                if (hatches != null)
                {
                    for (int i = 0; i < hatches.Length; i++)
                    {
                        var h = hatches[i];
                        if (h == null) continue;
                        Compartment a = null, b = null;
                        try { a = h.LeftCompartment; b = h.RightCompartment; } catch { }
                        link(a, b); link(b, a);
                    }
                }

                _adj = map; _lastBuild = now;
                lock (_lock) { _hops.Clear(); _towerOf.Clear(); }   // topology changed shape
                DiagLog.Write("[Proximity] compartment graph built: " + map.Count +
                              " compartment(s) with neighbours");
                return _adj;
            }
            catch { return _adj; }
            finally { Perf.End("Compartments.Rebuild", t0); }
        }

        private static readonly Dictionary<int, Compartment> _towerOf = new Dictionary<int, Compartment>();

        public static Compartment ConningTower(Compartment sameBoatAs)
        {
            if (sameBoatAs == null) return null;
            try
            {
                int id = sameBoatAs.GetInstanceID();
                Compartment cachedTower;
                lock (_lock) { if (_towerOf.TryGetValue(id, out cachedTower)) return cachedTower; }

                var map = Adjacency();
                if (map == null) return null;
                Compartment found = null;
                var seen = new HashSet<int> { id };
                var frontier = new List<Compartment> { sameBoatAs };
                for (int depth = 0; depth <= ProximityToggles.MaxHops && frontier.Count > 0 && found == null; depth++)
                {
                    var next = new List<Compartment>();
                    for (int i = 0; i < frontier.Count && found == null; i++)
                    {
                        var c = frontier[i];
                        string n = null;
                        try { n = c.name; } catch { }
                        if (n != null && n.IndexOf("Conning Tower", StringComparison.OrdinalIgnoreCase) >= 0)
                        { found = c; break; }
                        List<Compartment> nb;
                        if (!map.TryGetValue(c.GetInstanceID(), out nb)) continue;
                        for (int k = 0; k < nb.Count; k++)
                            if (nb[k] != null && seen.Add(nb[k].GetInstanceID())) next.Add(nb[k]);
                    }
                    frontier = next;
                }
                lock (_lock) { _towerOf[id] = found; }   // null cached too: no tower, no re-walk
                return found;
            }
            catch { return null; }
        }

        private static List<Compartment> NeighboursOf(Compartment c)
        {
            try
            {
                var map = Adjacency();
                if (map == null || c == null) return null;
                List<Compartment> l;
                if (map.TryGetValue(c.GetInstanceID(), out l)) return l;

                _rebuildRequested = true;
                return null;
            }
            catch { return null; }
        }

        public static int Hops(Compartment from, Compartment to)
        {
            if (from == null || to == null) return -1;
            if (from == to) return 0;
            try
            {
                int a = from.GetInstanceID(), b = to.GetInstanceID();
                long k = Key(a, b);
                int cached;
                lock (_lock)
                {
                    if (_hops.TryGetValue(k, out cached)) return cached;
                }

                int result = -1;
                var seen = new HashSet<int> { a };
                var frontier = new List<Compartment> { from };
                var next = new List<Compartment>();

                for (int depth = 1; depth <= ProximityToggles.MaxHops && frontier.Count > 0; depth++)
                {
                    next.Clear();
                    for (int i = 0; i < frontier.Count; i++)
                    {
                        var nb = NeighboursOf(frontier[i]);
                        if (nb == null) continue;
                        for (int n = 0; n < nb.Count; n++)
                        {
                            var c = nb[n];
                            if (c == null) continue;
                            int cid = c.GetInstanceID();
                            if (!seen.Add(cid)) continue;
                            if (cid == b) { result = depth; break; }
                            next.Add(c);
                        }
                        if (result >= 0) break;
                    }
                    if (result >= 0) break;
                    var swap = frontier; frontier = next; next = swap;
                }

                lock (_lock) { _hops[k] = result; }
                return result;
            }
            catch { return -1; }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Proximity_GetActionProfit
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            var sig = new[] { typeof(Activator), typeof(PlayableCharacter), typeof(float) };
            var found = new List<MethodBase>();
            try
            {
                foreach (var t in typeof(CharacterAction).Assembly.GetTypes())
                {
                    if (t == null || !typeof(CharacterAction).IsAssignableFrom(t)) continue;
                    MethodInfo m = null;
                    try
                    {
                        m = t.GetMethod("GetActionProfit",
                            BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.DeclaredOnly,
                            null, sig, null);
                    }
                    catch { }
                    if (m != null && !m.IsAbstract) found.Add(m);
                }
            }
            catch (Exception e) { DiagLog.WriteError("Proximity.TargetMethods", e); }
            return found;
        }

        private static readonly HashSet<string> _sampled = new HashSet<string>();
        private static readonly object _lock = new object();

        static void Postfix(CharacterAction __instance, Activator activator,
                            PlayableCharacter character, ref float __result)
        {
            if (!ProximityToggles.Enabled) return;
            if (__result <= 0f) return;
            long t0 = Perf.Begin();
            try
            {
                if (character == null || activator == null) return;

                if (__instance != null)
                {
                    bool exempt = false;
                    try { exempt = __instance.IsDirectPlayerOrder; }
                    catch { }
                    if (exempt)
                    {
                        OptCounters.ProximityPlayerOrderExempt++;
                        return;
                    }
                }

                if (__instance is SleepAction)
                {
                    OptCounters.ProximitySleepExempt++;
                    return;
                }

                var to = CompartmentGraph.CompartmentOf(activator);
                if (to == null) return;

                Compartment from = null;
                try { from = character.CurrentCompartment; } catch { }
                if (from == null)
                {
                    from = CompartmentGraph.ConningTower(to);
                    if (from == null) return;
                    OptCounters.ProximityDeckAsTower++;
                }

                int hops = CompartmentGraph.Hops(from, to);
                if (hops <= 0) return;      // same compartment, or unknown

                float scale = 1f;
                for (int i = 0; i < hops; i++) scale *= ProximityToggles.PerCompartmentFalloff;
                if (scale < ProximityToggles.MinScale) scale = ProximityToggles.MinScale;

                __result *= scale;
                OptCounters.ProximityScaled++;

                if (ProximityToggles.LogSamples)
                {
                    string act = __instance != null ? __instance.GetType().Name : "?";
                    string key = act + "|" + hops;
                    bool first;
                    lock (_lock) { first = _sampled.Add(key); }
                    if (first)
                    {
                        DiagLog.Write("[Proximity] " + act + " scaled x" + scale.ToString("F2") +
                                      " for " + hops + " compartment(s) - " +
                                      SafeName(from) + " -> " + SafeName(to));
                    }
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Proximity.GetActionProfit", e);
            }
            finally { Perf.End("Proximity.GetProfit", t0); }
        }

        private static string SafeName(Compartment c)
        {
            try { return c != null ? c.name : "null"; } catch { return "?"; }
        }
    }
}

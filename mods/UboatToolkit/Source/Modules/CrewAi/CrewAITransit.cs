// Crew-KI (Teil des UBOAT Toolkit): CrewAITransit. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class TransitToggles
    {
        public static readonly bool Enabled = true;

        public const int MaxTransitLines = 400;

        public const float SummaryInterval = 300f;

        public const string ControlRoomName = "Control";
    }

    internal static class TransitTracker
    {
        private static readonly Dictionary<int, Compartment> _lastCompartment =
            new Dictionary<int, Compartment>();
        private static readonly Dictionary<string, int> _perCharacter = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _perBehaviour = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _viaControl = new Dictionary<string, int>();
        private static readonly object _lock = new object();

        private static int _lines;
        private static float _lastSummary;
        private static Compartment _controlRoom;
        private static bool _controlSearched;

        private static Compartment ControlRoom()
        {
            if (_controlSearched) return _controlRoom;
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<Compartment>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        string n = null;
                        try { n = all[i].name; } catch { }
                        if (!string.IsNullOrEmpty(n) &&
                            n.IndexOf(TransitToggles.ControlRoomName,
                                      StringComparison.OrdinalIgnoreCase) >= 0)
                        { _controlRoom = all[i]; break; }
                    }
                }
                _controlSearched = true;
            }
            catch { _controlSearched = true; }
            return _controlRoom;
        }

        private static readonly Dictionary<int, Compartment> _enteredControlFrom =
            new Dictionary<int, Compartment>();

        private static int ScoreControlRoom(PlayableCharacter c, Compartment from, Compartment to)
        {
            try
            {
                var cr = ControlRoom();
                if (cr == null || c == null) return 0;
                int id = c.GetInstanceID();

                if (to == cr)
                {
                    lock (_lock) { _enteredControlFrom[id] = from; }
                    return 0;                       // scored on the way out
                }
                if (from == cr)
                {
                    Compartment origin = null;
                    lock (_lock)
                    {
                        _enteredControlFrom.TryGetValue(id, out origin);
                        _enteredControlFrom.Remove(id);
                    }
                    if (origin == null) return 0;   // we never saw him arrive
                    return to != origin ? 2 : 1;
                }
                return 0;
            }
            catch { return 0; }
        }

        public static void Note(PlayableCharacter c, Compartment to)
        {
            if (!TransitToggles.Enabled || c == null || to == null) return;
            try
            {
                int id = c.GetInstanceID();
                Compartment from;
                lock (_lock)
                {
                    _lastCompartment.TryGetValue(id, out from);
                    _lastCompartment[id] = to;
                }
                if (from == null || from == to) return;   // first sighting, or no move

                string who = "unknown";
                try { who = c.Name ?? "unknown"; } catch { }
                string behaviour = BehaviourCooldown.LastBehaviourOf(who) ?? "?";
                int crScore = ScoreControlRoom(c, from, to);
                bool passThrough = crScore == 2;
                bool inAndBack = crScore == 1;
                int hops = CompartmentGraph.Hops(from, to);

                OptCounters.CompartmentTransits++;
                if (passThrough) OptCounters.ControlRoomPassThroughs++;
                if (inAndBack) OptCounters.ControlRoomInAndBack++;

                lock (_lock)
                {
                    Bump(_perCharacter, who);
                    Bump(_perBehaviour, behaviour);
                    if (passThrough) Bump(_viaControl, behaviour);
                }

                if (_lines < TransitToggles.MaxTransitLines)
                {
                    _lines++;
                    DiagLog.Write("[Transit] " + who + " | " + SafeName(from) + " -> " + SafeName(to) +
                                  " (" + hops + " hop" + (hops == 1 ? "" : "s") + ")" +
                                  (passThrough ? " CONTROL-PASSTHROUGH" : inAndBack ? " CONTROL-RETURN" : "") +
                                  " | behaviour=" + behaviour +
                                  " | " + Needs(c));
                }

                MaybeSummary();
            }
            catch (Exception e) { DiagLog.WriteError("Transit.Note", e); }
        }

        private static readonly FieldInfo NeedsField =
            AccessTools.Field(typeof(PlayableCharacter), "needs");

        private static string Needs(PlayableCharacter c)
        {
            try
            {
                if (NeedsField == null) return "needs=?";
                var arr = NeedsField.GetValue(c) as Array;
                if (arr == null || arr.Length == 0) return "needs=none";
                var sb = new System.Text.StringBuilder("needs=[");
                for (int i = 0; i < arr.Length; i++)
                {
                    var n = arr.GetValue(i);
                    if (n == null) continue;
                    if (i > 0) sb.Append(' ');
                    float stress = 0f;
                    try
                    {
                        var p = AccessTools.Property(n.GetType(), "BiasedStress");
                        if (p != null) stress = (float)p.GetValue(n, null);
                    }
                    catch { }
                    sb.Append(i).Append(':').Append(stress.ToString("F2"));
                }
                sb.Append(']');
                return sb.ToString();
            }
            catch { return "needs=?"; }
        }

        private static void Bump(Dictionary<string, int> d, string k)
        {
            if (string.IsNullOrEmpty(k)) return;
            int n; d.TryGetValue(k, out n); d[k] = n + 1;
        }

        private static void MaybeSummary()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastSummary < TransitToggles.SummaryInterval) return;
                _lastSummary = now;
                lock (_lock)
                {
                    DiagLog.Write("[Transit:Summary] total=" + OptCounters.CompartmentTransits +
                                  " controlPassThroughs=" + OptCounters.ControlRoomPassThroughs +
                                  " controlInAndBack=" + OptCounters.ControlRoomInAndBack +
                                  " | by behaviour: " + Top(_perBehaviour, 8));
                    DiagLog.Write("[Transit:Summary] control PASS-THROUGHS by behaviour: " + Top(_viaControl, 8));
                    DiagLog.Write("[Transit:Summary] busiest movers: " + Top(_perCharacter, 8));
                }
            }
            catch { }
        }

        private static string Top(Dictionary<string, int> d, int n)
        {
            try
            {
                var list = new List<KeyValuePair<string, int>>(d);
                list.Sort((x, y) => y.Value.CompareTo(x.Value));
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < n && i < list.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(list[i].Key).Append('=').Append(list[i].Value);
                }
                if (list.Count == 0) sb.Append("(none)");
                return sb.ToString();
            }
            catch { return "(error)"; }
        }

        private static string SafeName(Compartment c)
        {
            try { return c != null ? c.name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(Compartment), "AddCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_Transit_AddCharacter
    {
        static void Postfix(Compartment __instance, PlayableCharacter character)
        {
            if (!TransitToggles.Enabled) return;
            try { TransitTracker.Note(character, __instance); }
            catch (Exception e) { DiagLog.WriteError("Transit.AddCharacter", e); }
        }
    }
}

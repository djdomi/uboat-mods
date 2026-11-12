// Crew-KI (Teil des UBOAT Toolkit): CrewAIDiscovery. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class Discovery
    {
        private static readonly HashSet<string> _seenBehaviours = new HashSet<string>();
        private static readonly HashSet<string> _seenActions = new HashSet<string>();

        private static readonly Dictionary<string, int> _behaviourCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _actionCounts = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _charEvents = new Dictionary<string, int>();

        private static long _crewBehaviourEvents, _worldBehaviourEvents;
        private static readonly HashSet<string> _crewNames = new HashSet<string>();
        private static readonly HashSet<string> _worldNames = new HashSet<string>();

        private static readonly object _lock = new object();

        private static float _lastSummary;
        private const float SummaryInterval = 180f;

        private static float _lastCharSnapshot;
        private static int _snapshotCursor;
        private const int SnapshotBatch = 4; // characters per pass - keeps it O(1), not O(N)

        public static void NoteBehaviour(AIBehaviour b, CharacterAI ai)
        {
            if (b == null) return;
            try
            {
                string type = b.GetType().Name;
                string owner = ai != null ? ai.name : null;
                bool isWorld = DiagLog.IsWorldSimName(owner);
                bool isNew;
                lock (_lock)
                {
                    isNew = _seenBehaviours.Add(type);
                    if (isWorld)
                    {
                        _worldBehaviourEvents++;
                        if (owner != null) _worldNames.Add(owner);
                    }
                    else
                    {
                        _crewBehaviourEvents++;
                        if (owner != null) _crewNames.Add(owner);
                        int n; _behaviourCounts.TryGetValue(type, out n); _behaviourCounts[type] = n + 1;
                    }
                }

                if (isNew && OptToggles.DiscoverBehaviourTypes)
                {
                    string flags = "?";
                    string ns = "?";
                    try { flags = b.Flags.ToString(); } catch { }
                    try { ns = b.GetType().Namespace ?? "?"; } catch { }
                    DiagLog.Write($"[Discover:Behaviour] first sighting '{type}' ns={ns} flags={flags}");
                }

                if (OptToggles.DiscoverBehaviourProfit && ai != null)
                {
                    if (UnityEngine.Random.value < 0.02f)
                    {
                        string profit = "?", count = "?", attention = "?";
                        try { profit = ai.LowestActiveProfit.ToString("F3"); } catch { }
                        try { count = ai.BehavioursCount.ToString(); } catch { }
                        try { attention = ai.Attention.ToString("F2"); } catch { }
                        DiagLog.Write($"[Discover:Profit] {SafeName(ai)} started '{type}' | " +
                                      $"behavioursCount={count} lowestActiveProfit={profit} attention={attention}");
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Discover.NoteBehaviour", e); }
        }

        public static void NoteAction(CharacterAction a, PlayableCharacter c)
        {
            if (a == null) return;
            try
            {
                string type = a.GetType().Name;
                bool isNew;
                lock (_lock)
                {
                    isNew = _seenActions.Add(type);
                    int n; _actionCounts.TryGetValue(type, out n); _actionCounts[type] = n + 1;
                    if (c != null)
                    {
                        string who = SafeCharName(c);
                        int m; _charEvents.TryGetValue(who, out m); _charEvents[who] = m + 1;
                    }
                }

                if (isNew && OptToggles.DiscoverActionTypes)
                {
                    DiagLog.Write($"[Discover:Action] first sighting '{type}' | {DescribeAction(a)}");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Discover.NoteAction", e); }
        }

        private static string DescribeAction(CharacterAction a)
        {
            var sb = new StringBuilder();
            try { sb.Append("hardReq=[").Append(Join(a.HardRequirements)).Append("] "); } catch { sb.Append("hardReq=? "); }
            try { sb.Append("softReq=[").Append(Join(a.SoftRequirements)).Append("] "); } catch { sb.Append("softReq=? "); }
            try { sb.Append("queuePriority=").Append(a.QueuePriority.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("interruptable=").Append(a.IsInterruptable).Append(' '); } catch { }
            try { sb.Append("lowPriority=").Append(a.IsLowPriorityAction).Append(' '); } catch { }
            try { sb.Append("playerOrder=").Append(a.IsDirectPlayerOrder).Append(' '); } catch { }
            try { sb.Append("forced=").Append(a.IsForcedAction).Append(' '); } catch { }
            try { sb.Append("canBeStopped=").Append(a.CanBeStopped).Append(' '); } catch { }
            try { sb.Append("charLimit=").Append(a.CharactersLimit).Append(' '); } catch { }
            try { sb.Append("silent=").Append(a.Silent).Append(' '); } catch { }
            try { sb.Append("delegable=").Append(a.AllowDelegationToOtherCharacters).Append(' '); } catch { }
            try { var r = a.Role; sb.Append("role=").Append(r != null ? r.ToString() : "null"); } catch { }
            return sb.ToString().TrimEnd();
        }

        private static string Join(string[] arr)
        {
            if (arr == null || arr.Length == 0) return "";
            return string.Join(", ", arr);
        }

        public static void MaybeSnapshotCharacters()
        {
            if (!OptToggles.DiscoverCharacterState) return;
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastCharSnapshot < OptToggles.CharacterStateInterval) return;
                _lastCharSnapshot = now;

                PlayableCharacter[] all = UnityEngine.Object.FindObjectsOfType<PlayableCharacter>();
                if (all == null || all.Length == 0) return;

                DiagLog.Write($"[Discover:Character] roster size={all.Length} (sampling {SnapshotBatch})");
                for (int i = 0; i < SnapshotBatch && i < all.Length; i++)
                {
                    var c = all[(_snapshotCursor + i) % all.Length];
                    DiagLog.Write("[Discover:Character] " + DescribeCharacter(c));
                }
                _snapshotCursor = (_snapshotCursor + SnapshotBatch) % all.Length;
            }
            catch (Exception e) { DiagLog.WriteError("Discover.Snapshot", e); }
        }

        private static string DescribeCharacter(PlayableCharacter c)
        {
            if (c == null) return "null";
            var sb = new StringBuilder();
            try { sb.Append(SafeCharName(c)).Append(" | "); } catch { }
            try { var r = c.Role; sb.Append("role=").Append(r != null ? r.ToString() : "null").Append(' '); } catch { }
            try { sb.Append("officer=").Append(c.IsOfficer).Append(' '); } catch { }
            try { sb.Append("skipper=").Append(c.IsSkipper).Append(' '); } catch { }
            try { sb.Append("onWatch=").Append(c.IsOnWatch).Append(' '); } catch { }
            try { sb.Append("energy=").Append(c.Energy.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("needs=").Append(c.NeedsLevel.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("tired=").Append(c.IsTired).Append(' '); } catch { }
            try { sb.Append("health=").Append(c.Health.ToString("F2")).Append(' '); } catch { }
            try { sb.Append("trauma=").Append(c.Trauma.ToString("F2")).Append(' '); } catch { }
            try { var cm = c.CurrentCompartment; sb.Append("compartment=").Append(cm != null ? cm.name : "null").Append(' '); } catch { }
            try { var a = c.Action; sb.Append("action=").Append(a != null ? a.GetType().Name : "none").Append(' '); } catch { }
            try { sb.Append("floor=").Append(FloorTypeOf(c)); } catch { }
            return sb.ToString().TrimEnd();
        }

        private static readonly System.Reflection.PropertyInfo FloorTypeProp =
            HarmonyLib.AccessTools.Property(typeof(PlayableCharacter), "FloorType");

        private static string FloorTypeOf(PlayableCharacter c)
        {
            try
            {
                if (FloorTypeProp == null) return "?";
                object v = FloorTypeProp.GetValue(c, null);
                return v != null ? v.ToString() : "?";
            }
            catch { return "?"; }
        }

        public static void MaybeSummary()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now - _lastSummary < SummaryInterval) return;
                _lastSummary = now;

                lock (_lock)
                {
                    DiagLog.Write("[Discover:Summary] crew=" + _crewNames.Count + " entities (" +
                                  _crewBehaviourEvents + " behaviour events) | worldSim=" +
                                  _worldNames.Count + " entities (" + _worldBehaviourEvents + " events)");
                    DiagLog.Write("[Discover:Summary] crew behaviours: " + TopOf(_behaviourCounts, 10));
                    DiagLog.Write("[Discover:Summary] actions:    " + TopOf(_actionCounts, 10));
                    DiagLog.Write("[Discover:Summary] busiest characters: " + TopOf(_charEvents, 6));
                }
            }
            catch (Exception e) { DiagLog.WriteError("Discover.Summary", e); }
        }

        private static string TopOf(Dictionary<string, int> d, int n)
        {
            try
            {
                var list = new List<KeyValuePair<string, int>>(d);
                list.Sort((x, y) => y.Value.CompareTo(x.Value));
                var sb = new StringBuilder();
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

        public static string SafeName(UnityEngine.Object o)
        {
            try { return o != null ? o.name : "null"; } catch { return "?"; }
        }

        public static string SafeCharName(PlayableCharacter c)
        {
            try { return c != null ? c.Name : "null"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_Discover_StartBehaviour
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // v1.0.0: discovery is a dev aid
        static void Postfix(CharacterAI __instance, AIBehaviour behaviour)
        {
            try
            {
                Discovery.NoteBehaviour(behaviour, __instance);
                Discovery.MaybeSnapshotCharacters();
                Discovery.MaybeSummary();
            }
            catch (Exception e) { DiagLog.WriteError("Discover.StartBehaviourHook", e); }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StartAction",
        new Type[] { typeof(UBOAT.Game.Scene.Characters.Activators.Activator), typeof(CharacterAction) })]
    internal static class Patch_Discover_StartAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // v1.0.0: discovery is a dev aid
        static void Postfix(PlayableCharacter __instance, CharacterAction action)
        {
            try { Discovery.NoteAction(action, __instance); }
            catch (Exception e) { DiagLog.WriteError("Discover.StartActionHook", e); }
        }
    }
}

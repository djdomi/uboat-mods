// Crew-KI (Teil des UBOAT Toolkit): CrewAIOrderTracking. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class OrderToggles
    {
        public static readonly bool Enabled = true;

        public const float StallSeconds = 90f;

        public const int MaxQueuedLines = 60;
        public const int MaxDroppedLines = 60;
        public const int MaxStartedLines = 20;
        public const int MaxStallLines = 30;

        public static readonly bool TrackAiActionsToo = false;
    }

    internal static class OrderTracker
    {
        private sealed class Tracked
        {
            public string What;         // action type
            public string Where;        // activator
            public string Owner;
            public float QueuedAt;      // unscaled
            public bool Started;
            public bool Stalled;
            public bool Player;
        }

        private static readonly Dictionary<CharacterAction, Tracked> _tracked =
            new Dictionary<CharacterAction, Tracked>();
        private static readonly object _lock = new object();

        private static int _queuedLines, _droppedLines, _startedLines, _stallLines;
        private static float _lastSweep;

        private static readonly PropertyInfo StageProp =
            AccessTools.Property(typeof(CharacterAction), "Stage");

        private static int Stage(CharacterAction a)
        {
            try
            {
                if (StageProp == null || a == null) return -1;
                object v = StageProp.GetValue(a, null);
                return v == null ? -1 : Convert.ToInt32(v);
            }
            catch { return -1; }
        }

        private static string StageName(int s)
        {
            switch (s)
            {
                case 0: return "Uninitialized";
                case 1: return "Initialized";
                case 2: return "Running";
                case 3: return "Completed";
                default: return "?";
            }
        }

        private static bool IsPlayerOrder(CharacterAction a)
        {
            try { return a != null && (a.IsDirectPlayerOrder || a.IsForcedAction); }
            catch { return false; }
        }

        private static string Describe(CharacterAction a)
        {
            try { return a != null ? a.GetType().Name : "?"; } catch { return "?"; }
        }

        private static string ActivatorOf(CharacterAction a)
        {
            try { return a != null && a.Activator != null ? a.Activator.name : "none"; }
            catch { return "?"; }
        }

        public static void NoteQueued(ActionQueue queue, CharacterAction action)
        {
            if (!OrderToggles.Enabled || action == null) return;
            try
            {
                bool player = IsPlayerOrder(action);

                float now = Time.unscaledTime;
                string owner = "?";
                int depth = -1;
                try { owner = queue != null && queue.Owner != null ? queue.Owner.Name : "(global)"; } catch { }
                try { var acts = queue != null ? queue.Actions : null; depth = acts != null ? acts.Count : -1; } catch { }

                var t = new Tracked();
                t.What = Describe(action);
                t.Where = ActivatorOf(action);
                t.Owner = owner;
                t.QueuedAt = now;
                t.Player = player;

                bool isNew;
                lock (_lock)
                {
                    isNew = !_tracked.ContainsKey(action);
                    _tracked[action] = t;
                }
                if (!isNew) return;

                if (player) OptCounters.PlayerOrdersSeen++;
                if (player && _queuedLines < OrderToggles.MaxQueuedLines)
                {
                    _queuedLines++;
                    DiagLog.Write("[Order] queued '" + t.What + "' on '" + t.Where +
                                  "' | owner=" + owner + " queueDepth=" + depth +
                                  " stage=" + StageName(Stage(action)) +
                                  " sourceJob=" + SafeJob(action));
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.NoteQueued", e); }
        }

        private static string SafeJob(CharacterAction a)
        {
            try
            {
                var j = a.SourceJob;
                return j == null ? "null(pinned 100+)" : j.GetType().Name;
            }
            catch { return "?"; }
        }

        public static void NoteRemoved(ActionQueue queue, CharacterAction action, string via)
        {
            if (!OrderToggles.Enabled || action == null) return;
            try
            {
                Tracked t;
                lock (_lock)
                {
                    if (!_tracked.TryGetValue(action, out t)) return;
                    _tracked.Remove(action);
                }

                int stage = Stage(action);
                float waited = Time.unscaledTime - t.QueuedAt;

                if (stage >= 2) return;

                bool player = IsPlayerOrder(action);
                if (player) OptCounters.PlayerOrdersDropped++;
                else OptCounters.AiActionsDropped++;

                if ((player || _droppedLines < 12) && _droppedLines < OrderToggles.MaxDroppedLines)
                {
                    _droppedLines++;
                    DiagLog.Write("[Order] DROPPED '" + t.What + "' on '" + t.Where +
                                  "' | owner=" + t.Owner +
                                  " playerOrder=" + player +
                                  " reachedStage=" + StageName(stage) +
                                  " waited=" + waited.ToString("F1") + "s real" +
                                  " via=" + via +
                                  " - removed from the queue without ever running");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.NoteRemoved", e); }
        }

        public static void Sweep()
        {
            if (!OrderToggles.Enabled) return;
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastSweep < 20f) return;
                _lastSweep = now;

                List<KeyValuePair<CharacterAction, Tracked>> snapshot;
                lock (_lock)
                {
                    snapshot = new List<KeyValuePair<CharacterAction, Tracked>>(_tracked);
                }

                List<CharacterAction> expired = null;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    var t = snapshot[i].Value;

                    if (now - t.QueuedAt > 900f)
                    {
                        if (expired == null) expired = new List<CharacterAction>();
                        expired.Add(snapshot[i].Key);
                        continue;
                    }

                    if (!t.Started && now - t.QueuedAt > OrderToggles.StallSeconds && !t.Stalled)
                    {
                        t.Stalled = true;
                        if (!t.Player) continue;
                        OptCounters.PlayerOrdersStalled++;
                        if (_stallLines < OrderToggles.MaxStallLines)
                        {
                            _stallLines++;
                            DiagLog.Write("[Order:Stalled] '" + t.What + "' on '" + t.Where +
                                          "' | owner=" + t.Owner +
                                          " still below Running after " +
                                          (now - t.QueuedAt).ToString("F0") + "s real");
                        }
                    }
                }

                if (expired != null)
                {
                    lock (_lock)
                    {
                        for (int i = 0; i < expired.Count; i++) _tracked.Remove(expired[i]);
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.Sweep", e); }
        }

        public static void NoteStageChanged(CharacterAction action)
        {
            if (!OrderToggles.Enabled || action == null) return;
            try
            {
                int stage = Stage(action);
                if (stage < 2) return;

                Tracked t;
                lock (_lock)
                {
                    if (!_tracked.TryGetValue(action, out t)) return;
                }
                if (t.Started) return;
                t.Started = true;

                if (_startedLines < OrderToggles.MaxStartedLines)
                {
                    _startedLines++;
                    DiagLog.Write("[Order] started '" + t.What + "' on '" + t.Where +
                                  "' | owner=" + t.Owner +
                                  " after " + (Time.unscaledTime - t.QueuedAt).ToString("F1") + "s real");
                }
            }
            catch (Exception e) { DiagLog.WriteError("Order.NoteStageChanged", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Add", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_Add
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.Add", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "ResetAndAdd", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_ResetAndAdd
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.ResetAndAdd", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Remove",
        new Type[] { typeof(CharacterAction), typeof(bool) })]
    internal static class Patch_Order_Remove
    {
        static void Prefix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteRemoved(__instance, action, "Remove"); }
            catch (Exception e) { DiagLog.WriteError("Order.Remove", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "RemoveAt", new Type[] { typeof(int) })]
    internal static class Patch_Order_RemoveAt
    {
        static void Prefix(ActionQueue __instance, int index)
        {
            try
            {
                var acts = __instance != null ? __instance.Actions : null;
                if (acts == null || index < 0 || index >= acts.Count) return;
                OrderTracker.NoteRemoved(__instance, acts[index], "RemoveAt");
            }
            catch (Exception e) { DiagLog.WriteError("Order.RemoveAt", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "AddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_AddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.AddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "ResetAndAddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_ResetAndAddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { OrderTracker.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("Order.ResetAndAddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Action_StageChanged",
        new Type[] { typeof(CharacterAction) })]
    internal static class Patch_Order_StageChanged
    {
        static void Postfix(CharacterAction action)
        {
            try { OrderTracker.NoteStageChanged(action); }
            catch (Exception e) { DiagLog.WriteError("Order.StageChanged", e); }
        }
    }
}

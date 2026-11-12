// Crew-KI (Teil des UBOAT Toolkit): CrewAIWatchFlip. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class WatchFlipToggles
    {
        public static readonly bool Enabled = true;
        public const float FlipSeconds = 10f;     // real
        public const int MaxLogLines = 80;
    }

    internal static class WatchFlips
    {
        private sealed class Holding
        {
            public string Who;
            public float Since;
        }

        private static readonly Dictionary<CharacterAction, Holding> _holder = new Dictionary<CharacterAction, Holding>();
        private static readonly Dictionary<CharacterAction, KeyValuePair<string, float>> _lastLoser =
            new Dictionary<CharacterAction, KeyValuePair<string, float>>();
        private static readonly object _lock = new object();
        private static int _lines;

        private static string Spot(CharacterAction a)
        {
            try { return a.Activator != null ? a.Activator.name : "?"; } catch { return "?"; }
        }

        private static string Name(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "?"; } catch { return "?"; }
        }

        public static void Added(CharacterAction a, PlayableCharacter c)
        {
            if (!WatchFlipToggles.Enabled || !(a is ObserveAction) || c == null) return;
            try
            {
                float now = Time.unscaledTime;
                string who = Name(c);
                string line = null;
                lock (_lock)
                {
                    var h = new Holding(); h.Who = who; h.Since = now;
                    _holder[a] = h;
                    KeyValuePair<string, float> lost;
                    if (_lastLoser.TryGetValue(a, out lost) && now - lost.Value < 2f && lost.Key != who)
                        line = "[WatchFlip] ObserveAction at '" + Spot(a) + "' handed to " + who +
                               " (" + lost.Key + " lost it " + (now - lost.Value).ToString("F1") + "s earlier)";
                    _lastLoser.Remove(a);
                }
                if (line != null && _lines < WatchFlipToggles.MaxLogLines) { _lines++; DiagLog.Write(line); }
            }
            catch { }
        }

        public static void Removed(CharacterAction a, PlayableCharacter c)
        {
            if (!WatchFlipToggles.Enabled || !(a is ObserveAction) || c == null) return;
            try
            {
                float now = Time.unscaledTime;
                string who = Name(c);
                float held = -1f;
                lock (_lock)
                {
                    Holding h;
                    if (_holder.TryGetValue(a, out h) && h.Who == who) { held = now - h.Since; _holder.Remove(a); }
                    _lastLoser[a] = new KeyValuePair<string, float>(who, now);
                    if (_lastLoser.Count > 64) _lastLoser.Clear();     // bounded
                }
                if (held < 0f || held >= WatchFlipToggles.FlipSeconds) return;

                OptCounters.WatchFlips++;
                if (_lines >= WatchFlipToggles.MaxLogLines) return;
                _lines++;
                string prio = "?", stage = "?", queue = "?";
                try { prio = a.QueuePriority.ToString("F2"); } catch { }
                try { stage = a.Stage.ToString(); } catch { }
                try { var q = a.ActionQueue; queue = q == null ? "none" : (q.Owner != null ? "personal" : "group/global"); } catch { }
                string sched = "?", job = "?";
                try { sched = c.Schedule != null ? c.Schedule.CurrentBehaviour.ToString() : "none"; } catch { }
                try { job = a.SourceJob != null ? a.SourceJob.GetType().Name : "none"; } catch { }
                DiagLog.Write("[WatchFlip] " + who + " lost ObserveAction at '" + Spot(a) + "' after " +
                              held.ToString("F1") + "s (queuePriority " + prio + ", stage " + stage + ", queue " + queue +
                              ", job " + job + ", his schedule " + sched + ")");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CharacterAction), "AddCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_WatchFlip_Add
    {
        static void Postfix(CharacterAction __instance, PlayableCharacter character)
        {
            try { WatchFlips.Added(__instance, character); } catch { }
        }
    }

    [HarmonyPatch(typeof(CharacterAction), "RemoveCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_WatchFlip_Remove
    {
        static void Prefix(CharacterAction __instance, PlayableCharacter character)
        {
            try { WatchFlips.Removed(__instance, character); } catch { }
        }
    }
}

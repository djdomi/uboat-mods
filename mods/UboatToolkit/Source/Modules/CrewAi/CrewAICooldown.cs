// Crew-KI (Teil des UBOAT Toolkit): CrewAICooldown. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Core.AI;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class CooldownToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewStuckCooldown");

        public const int DistinctTargetsBeforeCooldown = 3;

        public const float FailureWindowSeconds = 20f;

        public const float CooldownSeconds = 60f;

        public static readonly string[] ExemptBehaviours =
        {
            "Escape", "Danger", "Damage", "Repair", "Leak", "Fire", "Alarm",
            "Combat", "Fight", "Battle", "Evacuat", "Wounded", "Medic",
            "Treatment", "Bleeding", "Order", "Command",
            "Idle", "Compound", "Blank"
        };

        public static readonly bool LogCooldowns = true;
    }

    internal static class BehaviourCooldown
    {
        private class Record
        {
            public readonly HashSet<string> Targets = new HashSet<string>();
            public float LastFailure;       // unscaled
            public float CooldownUntil;     // unscaled; 0 = not on cooldown
        }

        private static readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();

        private static readonly Dictionary<string, string> _lastBehaviour = new Dictionary<string, string>();

        private static readonly object _lock = new object();
        private static float _lastPrune;

        private static string Key(string who, string behaviour) { return who + "|" + behaviour; }

        public static bool IsExempt(string behaviour)
        {
            if (string.IsNullOrEmpty(behaviour)) return true;
            var ex = CooldownToggles.ExemptBehaviours;
            for (int i = 0; i < ex.Length; i++)
            {
                if (behaviour.IndexOf(ex[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        public static void NoteBehaviourStarted(string who, string behaviour)
        {
            if (!CooldownToggles.Enabled) return;
            if (string.IsNullOrEmpty(who) || string.IsNullOrEmpty(behaviour)) return;
            try { lock (_lock) { _lastBehaviour[who] = behaviour; } }
            catch { }
        }

        public static string LastBehaviourOf(string who)
        {
            if (string.IsNullOrEmpty(who)) return null;
            try
            {
                string b;
                lock (_lock) { return _lastBehaviour.TryGetValue(who, out b) ? b : null; }
            }
            catch { return null; }
        }

        public static void NoteUnreachable(string who, string targetDesc)
        {
            if (!CooldownToggles.Enabled) return;
            if (string.IsNullOrEmpty(who)) return;
            try
            {
                string behaviour;
                lock (_lock)
                {
                    if (!_lastBehaviour.TryGetValue(who, out behaviour)) return;
                }
                if (IsExempt(behaviour)) return;

                float now = Time.unscaledTime;
                bool tripped = false;
                int distinct = 0;

                lock (_lock)
                {
                    Record r;
                    string k = Key(who, behaviour);
                    if (!_records.TryGetValue(k, out r))
                    {
                        r = new Record();
                        _records[k] = r;
                    }

                    if (now - r.LastFailure > CooldownToggles.FailureWindowSeconds)
                        r.Targets.Clear();

                    r.LastFailure = now;
                    r.Targets.Add(targetDesc ?? "?");
                    distinct = r.Targets.Count;

                    if (r.CooldownUntil <= now && distinct >= CooldownToggles.DistinctTargetsBeforeCooldown)
                    {
                        r.CooldownUntil = now + CooldownToggles.CooldownSeconds;
                        r.Targets.Clear();
                        tripped = true;
                    }
                }

                OptCounters.CooldownFailuresSeen++;
                if (tripped)
                {
                    OptCounters.CooldownsStarted++;
                    if (CooldownToggles.LogCooldowns)
                    {
                        DiagLog.Write("[Cooldown] " + who + " cannot reach " + distinct +
                                      " distinct targets for '" + behaviour + "' - suppressing it for " +
                                      CooldownToggles.CooldownSeconds + "s (latest target=" + targetDesc + ")");
                    }
                }
            }
            catch (Exception e) { DiagLog.WriteError("Cooldown.NoteUnreachable", e); }
        }

        public static bool IsOnCooldown(string who, string behaviour)
        {
            if (!CooldownToggles.Enabled) return false;
            if (string.IsNullOrEmpty(who) || IsExempt(behaviour)) return false;
            try
            {
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    Record r;
                    if (!_records.TryGetValue(Key(who, behaviour), out r)) return false;
                    if (r.CooldownUntil <= now) return false;
                    return true;
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("Cooldown.IsOnCooldown", e);
                return false; // fail open - never suppress because of our own error
            }
        }

        public static void MaybePrune()
        {
            try
            {
                float now = Time.unscaledTime;
                if (now - _lastPrune < 60f) return;
                _lastPrune = now;
                lock (_lock)
                {
                    var dead = new List<string>();
                    foreach (var kv in _records)
                    {
                        var r = kv.Value;
                        if (r.CooldownUntil <= now &&
                            now - r.LastFailure > CooldownToggles.FailureWindowSeconds * 2f)
                            dead.Add(kv.Key);
                    }
                    for (int i = 0; i < dead.Count; i++) _records.Remove(dead[i]);
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_Cooldown_Enforce
    {
        static void Postfix(CharacterAI __instance, AIBehaviour behaviour)
        {
            if (!CooldownToggles.Enabled) return;
            long t0 = Perf.Begin();
            try
            {
                if (behaviour == null) return;
                string type = behaviour.GetType().Name;
                if (BehaviourCooldown.IsExempt(type)) return;

                string who = null;
                try { who = __instance != null ? __instance.name : null; } catch { }
                if (who == null) return;

                if (BehaviourCooldown.IsOnCooldown(who, type))
                {
                    behaviour.State = AIBehaviourState.Cancelled;
                    OptCounters.CooldownBehavioursCancelled++;
                }
                BehaviourCooldown.MaybePrune();
            }
            catch (Exception e) { DiagLog.WriteError("Cooldown.Enforce", e); }
            finally { Perf.End("Cooldown.Enforce", t0); }
        }
    }
}

// Crew-KI (Teil des UBOAT Toolkit): CrewAIWatchClaims. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class WatchClaimToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewWatchClaims");

        public const float MaxClaimSeconds = 120f;

        public const int MaxLogLines = 40;
    }

    internal static class WatchClaims
    {
        private sealed class Claim
        {
            public PlayableCharacter Who;
            public CharacterAction Action;
            public float Since;      // unscaled
        }

        private static readonly Dictionary<int, Claim> _byActivator = new Dictionary<int, Claim>();
        private static readonly object _lock = new object();
        private static int _logLines;

        public static void NoteQueued(ActionQueue queue, CharacterAction action)
        {
            if (!WatchClaimToggles.Enabled || queue == null || action == null) return;
            try
            {
                if (!(action is ObserveAction)) return;
                PlayableCharacter who = null;
                try { who = queue.Owner; } catch { }
                if (who == null) return;                 // global queue: no single holder
                Activator act = null;
                try { act = action.Activator; } catch { }
                if (act == null) return;

                if (AttackPeriscopeGate.IsAttackPeriscope(act) && AttackPeriscopeGate.Denied(action, who)) return;

                var c = new Claim();
                c.Who = who; c.Action = action; c.Since = Time.unscaledTime;
                lock (_lock) { _byActivator[act.GetInstanceID()] = c; }
                OptCounters.WatchClaimsTaken++;
            }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.NoteQueued", e); }
        }

        public static void NoteRemoved(CharacterAction action)
        {
            if (!WatchClaimToggles.Enabled || action == null) return;
            try
            {
                if (!(action is ObserveAction)) return;
                Activator act = null;
                try { act = action.Activator; } catch { }
                if (act == null) return;
                int id = act.GetInstanceID();
                lock (_lock)
                {
                    Claim c;
                    if (_byActivator.TryGetValue(id, out c) && c.Action == action) _byActivator.Remove(id);
                }
            }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.NoteRemoved", e); }
        }

        private static bool Live(Claim c, float now)
        {
            try
            {
                if (c == null || c.Who == null || c.Action == null) return false;   // Unity null covers destroyed men
                if (c.Who.Action == c.Action) return true;                          // running it now
                if (now - c.Since > WatchClaimToggles.MaxClaimSeconds) return false;
                var q = c.Who.ActionQueue;
                var acts = q != null ? q.Actions : null;
                return acts != null && acts.Contains(c.Action);                     // still queued for him
            }
            catch { return false; }
        }

        public static bool HeldByOther(Activator activator, PlayableCharacter character, out PlayableCharacter holder)
        {
            holder = null;
            if (!WatchClaimToggles.Enabled || activator == null || character == null) return false;
            try
            {
                int id = activator.GetInstanceID();
                float now = Time.unscaledTime;
                Claim c;
                lock (_lock)
                {
                    if (!_byActivator.TryGetValue(id, out c)) return false;
                    if (!Live(c, now)) { _byActivator.Remove(id); return false; }
                }
                if (c.Who == character) return false;
                holder = c.Who;
                return true;
            }
            catch { return false; }
        }

        public static void NoteRefused(Activator activator, PlayableCharacter character, PlayableCharacter holder)
        {
            OptCounters.WatchRefusedClaimed++;
            if (_logLines >= WatchClaimToggles.MaxLogLines) return;
            _logLines++;
            try
            {
                DiagLog.Write("[Watch] '" + (activator != null ? activator.name : "?") + "' is held by " +
                              (holder != null ? holder.Name : "?") + " - not offering it to " +
                              (character != null ? character.Name : "?") +
                              " (vanilla would let him walk up and evict the holder)");
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter), typeof(float) })]
    internal static class Patch_WatchClaims_Profit
    {
        static void Postfix(ObserveAction __instance, Activator activator,
                            PlayableCharacter character, ref float __result)
        {
            if (!WatchClaimToggles.Enabled || __result <= 0f) return;
            try
            {
                if (__instance != null && __instance.IsDirectPlayerOrder) return;
                PlayableCharacter holder;
                if (WatchClaims.HeldByOther(activator, character, out holder))
                {
                    __result = -1f;
                    WatchClaims.NoteRefused(activator, character, holder);
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("WatchClaims.Profit", e);
            }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "AddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_WatchClaims_AddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { WatchClaims.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.AddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "ResetAndAddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_WatchClaims_ResetAndAddPreStarted
    {
        static void Postfix(ActionQueue __instance, CharacterAction action)
        {
            try { WatchClaims.NoteQueued(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.ResetAndAddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "Remove", new Type[] { typeof(CharacterAction), typeof(bool) })]
    internal static class Patch_WatchClaims_Remove
    {
        static void Prefix(CharacterAction action)
        {
            try { WatchClaims.NoteRemoved(action); }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.Remove", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "RemoveAt", new Type[] { typeof(int) })]
    internal static class Patch_WatchClaims_RemoveAt
    {
        static void Prefix(ActionQueue __instance, int index)
        {
            try
            {
                var acts = __instance != null ? __instance.Actions : null;
                if (acts == null || index < 0 || index >= acts.Count) return;
                WatchClaims.NoteRemoved(acts[index]);
            }
            catch (Exception e) { DiagLog.WriteError("WatchClaims.RemoveAt", e); }
        }
    }
}

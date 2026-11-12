// Crew-KI (Teil des UBOAT Toolkit): CrewAIAttackPeriscopeStop. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class AttackPeriscopeStop
    {
        private static readonly HashSet<ObserveAction> _pending = new HashSet<ObserveAction>();
        private static readonly object _lock = new object();

        public static void MarkRefused(ObserveAction a)
        {
            if (a == null) return;
            lock (_lock)
            {
                if (_pending.Count > 64) _pending.Clear();   // bounded, belt and braces
                _pending.Add(a);
            }
        }

        public static bool TakeRefused(ObserveAction a)
        {
            if (a == null) return false;
            lock (_lock) { return _pending.Remove(a); }
        }

        public static void Forget(ObserveAction a)
        {
            if (a == null) return;
            lock (_lock) { _pending.Remove(a); }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "UpdateAction")]
    internal static class Patch_AttackPeriscopeStop_UpdateAction
    {
        static bool Prefix(ObserveAction __instance)
        {
            try
            {
                if (!AttackPeriscopeStop.TakeRefused(__instance)) return true;
                PlayableCharacter c = null;
                try { c = __instance.Character; } catch { }

                bool removed = false;
                try
                {
                    var q = c != null ? c.ActionQueue : null;
                    var acts = q != null ? q.Actions : null;
                    if (acts != null && acts.Contains(__instance)) { q.Remove(__instance, true); removed = true; }
                }
                catch (Exception e) { DiagLog.WriteError("AttackPeriscopeStop.Remove", e); }
                if (!removed) { try { __instance.CompleteAction(); } catch { } }
                if (c != null && c.Action == __instance) c.StopCurrentAction(true);
                OptCounters.AttackPeriscopeRemoved++;
                return false;                                  // skip this one frame's update
            }
            catch (Exception e)
            {
                DiagLog.WriteError("AttackPeriscopeStop.UpdateAction", e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "StopMainCharacter", new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_AttackPeriscopeStop_StopMainCharacter
    {
        static void Postfix(ObserveAction __instance)
        {
            try { AttackPeriscopeStop.Forget(__instance); } catch { }
        }
    }
}

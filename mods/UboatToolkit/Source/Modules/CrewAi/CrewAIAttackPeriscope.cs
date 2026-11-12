// Crew-KI (Teil des UBOAT Toolkit): CrewAIAttackPeriscope. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Items;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class AttackPeriscopeGate
    {
        private static readonly HashSet<string> _seen = new HashSet<string>();
        private static readonly object _lock = new object();

        public static bool IsPeriscope(Activator activator)
        {
            if (activator == null) return false;
            try { return activator.HasTag(PeriscopeToggles.PeriscopeActivatorTag); }
            catch { return false; }
        }

        public static bool IsAttackPeriscope(Activator activator)
        {
            if (activator == null) return false;
            try
            {
                var p = activator.GetComponent<Periscope>();
                return p != null && p.name == PeriscopeToggles.AttackPeriscopeObjectName;
            }
            catch { return false; }
        }

        private static readonly HashSet<string> _spared = new HashSet<string>();
        public static void NoteSpared(Activator activator)
        {
            try
            {
                if (activator == null || !IsPeriscope(activator) || IsAttackPeriscope(activator)) return;
                string n = activator.name ?? "?";
                bool first;
                lock (_lock) { first = _spared.Add(n); }
                if (first)
                    DiagLog.Write("[AttackPeriscope] '" + n + "' carries the Periscope tag but is not the " +
                                  "attack periscope - v0.19 zeroed AI profit here, v0.20 does not");
            }
            catch { }
        }

        public static void NoteOnce(CharacterAction action, Activator activator,
                                    PlayableCharacter c, string what)
        {
            if (c == null) return;
            try
            {
                string name = c.Name ?? "unknown";
                string act = activator != null ? activator.name : "null";
                lock (_lock) { if (!_seen.Add(what + "|" + name + "|" + act)) return; }

                string tags = "?";
                try
                {
                    if (activator != null && activator.Tags != null)
                        tags = string.Join(", ", new List<string>(activator.Tags).ToArray());
                }
                catch { }

                string hard = "?";
                try
                {
                    if (action != null && action.HardRequirements != null)
                        hard = string.Join(", ", action.HardRequirements);
                }
                catch { }

                DiagLog.Write("[AttackPeriscope] " + what + " | character=" + name +
                              " skipper=" + SafeSkipper(c) + " officer=" + SafeOfficer(c) +
                              " | activator=" + act + " tags=[" + tags + "]" +
                              " | actionHardReq=[" + hard + "]");
            }
            catch (Exception e) { DiagLog.WriteError("AttackPeriscope.NoteOnce", e); }
        }

        private static string SafeSkipper(PlayableCharacter c)
        { try { return c.IsSkipper.ToString(); } catch { return "?"; } }

        private static string SafeOfficer(PlayableCharacter c)
        { try { return c.IsOfficer.ToString(); } catch { return "?"; } }

        private static readonly Dictionary<string, int> _blockCounts =
            new Dictionary<string, int>();

        public static void NoteBlockedOnce(CharacterAction action, PlayableCharacter c,
                                           bool viaAttackFlag, string optic)
        {
            try
            {
                string name = "unknown";
                try { name = c != null ? (c.Name ?? "unknown") : "unknown"; } catch { }

                int n;
                bool first;
                lock (_lock)
                {
                    _blockCounts.TryGetValue(name, out n);
                    n++;
                    _blockCounts[name] = n;
                    first = (n == 1);
                }

                if (!first && (n & (n - 1)) != 0) return;

                string hard = "?", soft = "?", role = "?", job = "?";
                try
                {
                    if (action != null && action.HardRequirements != null)
                        hard = string.Join(", ", action.HardRequirements);
                }
                catch { }
                try
                {
                    if (action != null && action.SoftRequirements != null)
                        soft = string.Join(", ", action.SoftRequirements);
                }
                catch { }
                try
                {
                    if (action != null)
                        role = action.Role != null ? action.Role.ToString() : "null";
                }
                catch { }
                try
                {
                    if (action != null)
                        job = action.SourceJob != null ? action.SourceJob.ToString() : "null";
                }
                catch { }

                string order = "?";
                try { if (action != null) order = action.IsDirectPlayerOrder.ToString(); }
                catch { }

                DiagLog.Write("[AttackPeriscope] BLOCKED start #" + n + " | character=" + name +
                              " | optic='" + optic + "'" +
                              " | identifiedBy=" + (viaAttackFlag ? "isAttackPeriscope(name match)" : "unknown") +
                              " | actionHardReq=[" + hard + "] softReq=[" + soft + "]" +
                              " | role=" + role + " sourceJob=" + job +
                              " directPlayerOrder=" + order);
            }
            catch (Exception e) { DiagLog.WriteError("AttackPeriscope.NoteBlockedOnce", e); }
        }

        public static bool Denied(CharacterAction action, PlayableCharacter c)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return false;
            if (c == null) return false;
            try
            {
                if (c.IsSkipper) return false;
                if (PeriscopeToggles.AllowDirectPlayerOrder && action != null &&
                    action.IsDirectPlayerOrder) return false;
                return true;
            }
            catch { return false; } // fail open
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter) })]
    internal static class Patch_ObserveAction_GetActionProfit
    {
        static void Postfix(ObserveAction __instance, Activator activator,
                            PlayableCharacter character, ref ActionProfit __result)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return;
            try
            {
                AttackPeriscopeGate.NoteSpared(activator);
                if (character == null || !AttackPeriscopeGate.IsAttackPeriscope(activator)) return;
                AttackPeriscopeGate.NoteOnce(__instance, activator, character, "scored");

                if (AttackPeriscopeGate.Denied(__instance, character))
                {
                    __result = new ActionProfit(__result.ProfitForPlayer, 0f);
                    OptCounters.AttackPeriscopeBlocked++;
                }
            }
            catch (Exception e)
            {
                DiagLog.WriteError("AttackPeriscope.GetActionProfit", e);
            }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "GetActionProfit",
        new Type[] { typeof(Activator), typeof(PlayableCharacter), typeof(float) })]
    internal static class Patch_ObserveAction_GetActionProfitFloat
    {
        static void Postfix(ObserveAction __instance, Activator activator,
                            PlayableCharacter character, ref float __result)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return;
            try
            {
                AttackPeriscopeGate.NoteSpared(activator);
                if (character == null || !AttackPeriscopeGate.IsAttackPeriscope(activator)) return;
                if (AttackPeriscopeGate.Denied(__instance, character) && __result > 0f)
                {
                    __result = 0f;
                    OptCounters.AttackPeriscopeBlocked++;
                }
            }
            catch (Exception e) { DiagLog.WriteError("AttackPeriscope.GetActionProfitFloat", e); }
        }
    }

    [HarmonyPatch(typeof(ObserveAction), "StartMainCharacter",
        new Type[] { typeof(PlayableCharacter) })]
    internal static class Patch_ObserveAction_StartMainCharacter
    {
        static bool Prefix(ObserveAction __instance, PlayableCharacter character)
        {
            if (!PeriscopeToggles.AttackPeriscopeSkipperOnly) return true;
            try
            {
                if (__instance == null || character == null) return true;
                if (!AttackPeriscopeGate.Denied(__instance, character)) return true;

                bool isPeriscope = false;
                bool isAttackFlag = false;
                string optic = "none";
                try
                {
                    var act = __instance.Activator;
                    var p = act != null ? act.GetComponent<Periscope>() : null;
                    if (p != null)
                    {
                        optic = p.name;
                        isAttackFlag = (p.name == PeriscopeToggles.AttackPeriscopeObjectName);
                        isPeriscope = isAttackFlag;
                    }
                }
                catch { }

                if (!isPeriscope)
                {
                    AttackPeriscopeGate.NoteOnce(__instance, null, character,
                                                 "allowed-start (optic='" + optic + "')");
                    return true;
                }

                AttackPeriscopeGate.NoteBlockedOnce(__instance, character, isAttackFlag, optic);
                OptCounters.AttackPeriscopeBlocked++;
                AttackPeriscopeStop.MarkRefused(__instance);
                return true;
            }
            catch (Exception e)
            {
                DiagLog.WriteError("AttackPeriscope.StartMainCharacter", e);
            }
            return true;
        }
    }
}

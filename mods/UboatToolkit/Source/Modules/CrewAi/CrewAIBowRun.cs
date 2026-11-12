// Crew-KI (Teil des UBOAT Toolkit): CrewAIBowRun. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Tasks;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class BowRunToggles
    {
        public static bool Enabled => UboatToolkit.Settings.B("CrewBowRunCap");

        public static int MaxRunners => Mathf.Clamp(Mathf.RoundToInt(UboatToolkit.Settings.F("CrewBowRunMen")), 0, 30);

        public const float ReleaseMin = 0.4f;
        public const float ReleaseMax = 1.2f;
    }

    internal static class BowRun
    {
        public static bool IsRunning(PlayableCharacter c)
        {
            try { return c != null && c.Action is RunAtBowAction; } catch { return false; }
        }

        internal static string SafeName(PlayableCharacter c)
        {
            try { return c != null ? (c.Name ?? "?") : "?"; } catch { return "?"; }
        }
    }

    [HarmonyPatch(typeof(IdleCrewRunsAtBow), "PlayerShipOnSubmergeStateChanged")]
    internal static class Patch_BowRun_Trigger
    {
        static void Prefix(RunAtBowAction ___action, out int __state)
        {
            __state = 0;
            try { if (___action != null && ___action.Characters != null) __state = ___action.Characters.Count; }
            catch { }
        }

        static void Postfix(RunAtBowAction ___action, int __state)
        {
            if (!BowRunToggles.Enabled) return;
            try
            {
                var list = ___action != null ? ___action.Characters : null;
                if (list == null) return;
                int before = __state, sent = list.Count;
                if (sent <= before) return;   // surfaced (RemoveAllCharacters) or nobody new

                OptCounters.BowRunDives++;
                OptCounters.BowRunSent += sent - before;

                int trimmed = 0;
                int cap = Math.Max(0, BowRunToggles.MaxRunners);
                if (sent > cap)
                {
                    Vector3 bow = Vector3.zero;
                    bool haveBow = false;
                    try
                    {
                        var ship = RestController.Ship;
                        var comp = ship != null ? ship.GetEquipment<Compartment>("Bow Torpedo Room") : null;
                        if (comp != null) { bow = comp.transform.position; haveBow = true; }
                    }
                    catch { }

                    var order = new List<KeyValuePair<PlayableCharacter, float>>(sent);
                    for (int i = 0; i < list.Count; i++)
                    {
                        var c = list[i];
                        float d = 0f;
                        try { if (haveBow && c != null) d = (c.transform.position - bow).sqrMagnitude; } catch { }
                        order.Add(new KeyValuePair<PlayableCharacter, float>(c, d));
                    }
                    if (haveBow) order.Sort((x, y) => y.Value.CompareTo(x.Value));
                    else order.Reverse();   // vanilla adds from the end of the crew list

                    int excess = sent - cap;
                    for (int i = 0; i < order.Count && trimmed < excess; i++)
                    {
                        try { ___action.RemoveCharacter(order[i].Key); trimmed++; }
                        catch (Exception e) { DiagLog.WriteError("BowRun.Trim", e); }
                    }
                    OptCounters.BowRunTrimmed += trimmed;
                }

                DiagLog.Write("[BowRun] dive drill: vanilla sent " + (sent - before) + " men to the bow" +
                              (before > 0 ? " (" + before + " already there)" : "") +
                              ", kept " + (sent - trimmed) + ", sent " + trimmed + " back to what they were doing");
            }
            catch (Exception e) { DiagLog.WriteError("BowRun.Trigger", e); }
        }
    }

    [HarmonyPatch(typeof(IdleCrewRunsAtBow), "RemoveOneCharacter")]
    internal static class Patch_BowRun_Release
    {
        static void Prefix(RunAtBowAction ___action, out int __state)
        {
            __state = 0;
            try { if (___action != null && ___action.Characters != null) __state = ___action.Characters.Count; }
            catch { }
        }

        static void Postfix(ref float __result, int __state)
        {
            if (!BowRunToggles.Enabled) return;
            try
            {
                if (__state <= 0) return;
                OptCounters.BowRunReleased++;
                if (__state == 1) DiagLog.Write("[BowRun] last man released from the bow");
                if (!float.IsNaN(__result))
                    __result = UnityEngine.Random.Range(BowRunToggles.ReleaseMin, BowRunToggles.ReleaseMax);
            }
            catch (Exception e) { DiagLog.WriteError("BowRun.Release", e); }
        }
    }
}

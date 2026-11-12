// Crew-KI (Teil des UBOAT Toolkit): CrewAIRoleDropLog. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class RoleDropToggles
    {
        public static readonly bool Enabled = true;

        public static bool KeepRunningJobs => UboatToolkit.Settings.B("CrewKeepJobs");

        public const float TieTolerance = 0.05f;

        public const int MaxLogLines = 60;
        public const int SampleEvery = 50;
        public const int MaxCandidatesShown = 6;
    }

    [HarmonyPatch(typeof(CharacterRole), "RemoveOutdatedActions",
        new Type[] { typeof(ActionQueue), typeof(ActionProfitabilityData) })]
    internal static class Patch_RoleDrop_RemoveOutdatedActions
    {
        static readonly FieldInfo ActionsField = AccessTools.Field(typeof(CharacterRole), "actions");
        static readonly FieldInfo ActionsCountField = AccessTools.Field(typeof(CharacterRole), "actionsCount");
        static readonly PropertyInfo StageProp = AccessTools.Property(typeof(CharacterAction), "Stage");

        static int _dropLines, _keepLines;

        static bool ShouldLog(ref int lines, long counter)
        {
            if (lines < RoleDropToggles.MaxLogLines) { lines++; return true; }
            return counter % RoleDropToggles.SampleEvery == 0;
        }

        static string Act(CharacterAction a, UBOAT.Game.Scene.Characters.Activators.Activator act)
        {
            string t = "?", s = "?";
            try { t = a != null ? a.GetType().Name : "none"; } catch { }
            try { s = act != null ? act.name : "none"; } catch { }
            return t + " at '" + s + "'";
        }

        static void Prefix(CharacterRole __instance, ActionQueue actionQueue, ActionProfitabilityData bestAction,
                           out int __state)
        {
            __state = -1;
            if (!RoleDropToggles.Enabled || __instance == null || actionQueue == null) return;
            try
            {
                var acts = actionQueue.Actions;
                if (acts == null || acts.Count == 0) return;
                if (ActionsField == null || ActionsCountField == null) return;

                var list = ActionsField.GetValue(__instance) as ActionProfitabilityData[];
                int count = (int)ActionsCountField.GetValue(__instance);
                if (list == null) { list = new ActionProfitabilityData[0]; count = 0; }
                if (count > list.Length) count = list.Length;
                int originalCount = count;

                for (int i = acts.Count - 1; i >= 0; i--)
                {
                    var a = acts[i];
                    if (a == null) continue;
                    object role = null;
                    try { role = a.Role; } catch { }
                    if (!ReferenceEquals(role, __instance)) continue;          // only this role's actions are touched

                    bool kept = false;
                    try
                    {
                        if (bestAction.Action != null && a.IsInterchangeable(bestAction.Action, bestAction.Activator)) kept = true;
                        for (int j = 0; j < count && !kept; j++)
                            if (list[j].Action != null && a.IsInterchangeable(list[j].Action, list[j].Activator)) kept = true;
                    }
                    catch { kept = true; }
                    if (kept) continue;

                    int stage = -1;
                    try { if (StageProp != null) stage = Convert.ToInt32(StageProp.GetValue(a, null)); } catch { }
                    if (stage != 2) continue;                                   // only RUNNING jobs disturb a man

                    PlayableCharacter who = null;
                    try { var cs = a.Characters; if (cs != null && cs.Count > 0) who = cs[0]; } catch { }
                    if (who == null) { try { who = actionQueue.Owner; } catch { } }

                    float score = float.NaN;
                    try
                    {
                        float basePrio = a.SourceJob != null ? a.SourceJob.BasePriority : 1f;
                        if (who != null) score = a.GetActionProfit(a.Activator, who, basePrio);
                    }
                    catch { }

                    bool sleepJob = a is SleepAction;
                    bool sleepShift = false;
                    try { sleepShift = who != null && who.Schedule != null && who.Schedule.CurrentBehaviour == Schedule.Behaviour.Sleep; } catch { }

                    bool keep = RoleDropToggles.KeepRunningJobs && who != null && !sleepJob && !sleepShift &&
                                !float.IsNaN(score) && score > 0.001f &&
                                (bestAction.Action == null || score >= bestAction.Profit - RoleDropToggles.TieTolerance);

                    if (keep)
                    {
                        if (count >= list.Length)
                        {
                            var grown = new ActionProfitabilityData[Math.Max(4, list.Length * 2)];
                            Array.Copy(list, grown, list.Length);
                            list = grown;
                            ActionsField.SetValue(__instance, list);
                        }
                        list[count] = new ActionProfitabilityData(a, a.Activator, score, a.SourceJob);
                        count++;
                        ActionsCountField.SetValue(__instance, count);
                        __state = originalCount;                              // postfix restores

                        OptCounters.RoleKeeps++;
                        if (ShouldLog(ref _keepLines, OptCounters.RoleKeeps))
                        {
                            DiagLog.Write("[RoleKeep] " + SafeRole(__instance) + ": kept " + (who.Name ?? "?") +
                                          " on running " + Act(a, a.Activator) + " (scores " + score.ToString("F2") +
                                          ") instead of " + Act(bestAction.Action, bestAction.Activator) + " " +
                                          bestAction.Profit.ToString("F2"));
                        }
                        continue;
                    }

                    OptCounters.RoleDropsRunning++;
                    if (!ShouldLog(ref _dropLines, OptCounters.RoleDropsRunning)) continue;

                    string why = who == null ? "no holder" : sleepJob ? "sleep job" : sleepShift ? "his schedule is Sleep" :
                                 float.IsNaN(score) ? "unscorable" : score <= 0.001f ? "no longer valid for him" :
                                 "outscored";
                    var sb = new StringBuilder("[RoleDrop] ");
                    sb.Append(SafeRole(__instance)).Append(": ").Append(who != null ? (who.Name ?? "?") : "(group)");
                    sb.Append(" loses running ").Append(Act(a, a.Activator));
                    sb.Append(" (scores ").Append(float.IsNaN(score) ? "?" : score.ToString("F2")).Append(" for him now)");
                    sb.Append(" - best is ").Append(Act(bestAction.Action, bestAction.Activator));
                    sb.Append(" ").Append(bestAction.Profit.ToString("F2"));
                    sb.Append(" [").Append(why).Append("] | candidates: ");
                    if (originalCount == 0) sb.Append("none");
                    for (int j = 0; j < originalCount && j < RoleDropToggles.MaxCandidatesShown; j++)
                    {
                        if (j > 0) sb.Append(", ");
                        sb.Append(Act(list[j].Action, list[j].Activator)).Append(' ').Append(list[j].Profit.ToString("F2"));
                    }
                    DiagLog.Write(sb.ToString());
                }
            }
            catch (Exception e) { DiagLog.WriteError("RoleDrop.Prefix", e); }
        }

        static void Postfix(CharacterRole __instance, int __state)
        {
            if (__state < 0 || ActionsCountField == null) return;
            try { ActionsCountField.SetValue(__instance, __state); }
            catch (Exception e) { DiagLog.WriteError("RoleDrop.Postfix", e); }
        }

        static string SafeRole(CharacterRole r)
        {
            try { return r.Name ?? "?"; } catch { return "?"; }
        }
    }
}

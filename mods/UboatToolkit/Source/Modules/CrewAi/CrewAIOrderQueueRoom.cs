// Crew-KI (Teil des UBOAT Toolkit): CrewAIOrderQueueRoom. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class QueueRoomToggles
    {
        public static readonly bool ReportRefusals = true;

        public static bool MakeRoomForPlayerOrders => UboatToolkit.Settings.B("CrewOrderRoom");

        public const int PerCharacterLimit = 6;
        public const int GlobalLimit = 9;

        public const int MaxRefusalLines = 60;
        public const int MaxEvictionLines = 40;
    }

    internal static class QueueRoom
    {
        private static int _refusalLines, _evictionLines;

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

        private static string OwnerOf(ActionQueue q)
        {
            try { return q != null && q.Owner != null ? q.Owner.Name : "(global)"; }
            catch { return "?"; }
        }

        private static int LimitFor(ActionQueue q)
        {
            try
            {
                return (q != null && q.Owner != null)
                    ? QueueRoomToggles.PerCharacterLimit
                    : QueueRoomToggles.GlobalLimit;
            }
            catch { return QueueRoomToggles.GlobalLimit; }
        }

        public static void BeforeAddPreStarted(ActionQueue queue, CharacterAction action)
        {
            if (action == null || queue == null) return;
            try
            {
                List<CharacterAction> actions = null;
                try { actions = queue.Actions; } catch { }
                if (actions == null) return;

                int limit = LimitFor(queue);
                if (actions.Count <= limit) return;      // vanilla will accept it

                bool player = IsPlayerOrder(action);

                if (QueueRoomToggles.ReportRefusals && _refusalLines < QueueRoomToggles.MaxRefusalLines)
                {
                    _refusalLines++;
                    DiagLog.Write("[Order] REFUSED '" + Describe(action) + "' on '" +
                                  ActivatorOf(action) + "' | queue=" + OwnerOf(queue) +
                                  " depth=" + actions.Count + " cap=" + limit +
                                  " playerOrder=" + player +
                                  " - vanilla ActionQueue.AddPreStarted discards this SILENTLY");
                }
                OptCounters.OrdersRefusedQueueFull++;

                bool direct = false;
                try { direct = action.IsDirectPlayerOrder; } catch { }
                if (direct) OptCounters.PlayerOrdersRefusedQueueFull++;

                if (!QueueRoomToggles.MakeRoomForPlayerOrders || !direct) return;

                CharacterAction victim = null;
                float worst = float.MaxValue;
                for (int i = 0; i < actions.Count; i++)
                {
                    var a = actions[i];
                    if (a == null) continue;
                    if (IsPlayerOrder(a)) continue;          // never sacrifice intent
                    if (Stage(a) >= 2) continue;             // never abort running work
                    bool stoppable;
                    try { stoppable = a.CanBeStopped; } catch { continue; }
                    if (!stoppable) continue;                // vanilla's own Clear() test

                    float p;
                    try { p = a.QueuePriority; } catch { continue; }
                    if (p < worst) { worst = p; victim = a; }
                }

                if (victim == null) return;                  // nothing safe to drop

                queue.Remove(victim, true);
                OptCounters.OrdersMadeRoom++;

                if (_evictionLines < QueueRoomToggles.MaxEvictionLines)
                {
                    _evictionLines++;
                    DiagLog.Write("[Order] made room for player order '" + Describe(action) +
                                  "' on '" + ActivatorOf(action) + "' by evicting '" +
                                  Describe(victim) + "' on '" + ActivatorOf(victim) +
                                  "' (priority " + worst.ToString("F2") +
                                  ", stage " + Stage(victim) + ") from " + OwnerOf(queue));
                }
            }
            catch (Exception e) { DiagLog.WriteError("QueueRoom.BeforeAddPreStarted", e); }
        }
    }

    [HarmonyPatch(typeof(ActionQueue), "AddPreStarted", new Type[] { typeof(CharacterAction) })]
    internal static class Patch_QueueRoom_AddPreStarted
    {
        static void Prefix(ActionQueue __instance, CharacterAction action)
        {
            try { QueueRoom.BeforeAddPreStarted(__instance, action); }
            catch (Exception e) { DiagLog.WriteError("QueueRoom.Prefix", e); }
        }
    }
}

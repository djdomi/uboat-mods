using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.UI;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Game error guards: places where the game itself throws an error every frame (stutter, slow menus,
    // crashes). Each guard only acts after the error really happened, or on the exact broken state.
    internal static class Guards
    {
        internal static bool On(string key) => Settings.B(key);
        private static readonly HashSet<string> logged = new HashSet<string>();
        internal static void Once(string key, string text) { if (logged.Add(key)) Util.Log("Fehlerschutz: " + text); }
        internal static bool IsNull(Exception e) => e is NullReferenceException;
    }

    // 1) Character without an action queue: the role code reads it every frame and throws.
    [HarmonyPatch(typeof(CharacterRole), "HasPlayerOrders", new Type[] { typeof(ActionQueue) })]
    internal static class Guard_NoQueueOrders
    {
        private static bool Prefix(ActionQueue actionQueue, ref bool __result)
        {
            if (actionQueue != null || !Guards.On("GuardNoQueue")) return true;
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(CharacterRole), "AssignJob", new Type[] { typeof(PlayableCharacter) })]
    internal static class Guard_NoQueueJob
    {
        private static bool Prefix(PlayableCharacter character)
        {
            if (!Guards.On("GuardNoQueue")) return true;
            if (character == null) return false;
            try { return character.ActionQueue != null; } catch { return false; }
        }
    }

    // 2) Card game that keeps running after its table is gone: finish it instead of erroring every frame.
    [HarmonyPatch(typeof(PlayCardsAction), "UpdateAction")]
    internal static class Guard_CardsWithoutTable
    {
        private static readonly FieldInfo table = AccessTools.Field(typeof(PlayCardsAction), "table");
        private static bool Prefix(PlayCardsAction __instance)
        {
            if (!Guards.On("GuardCards")) return true;
            if (__instance == null) return false;
            try
            {
                if (table == null || table.GetValue(__instance) != null) return true;
                __instance.CompleteAction();
                Guards.Once("cards", "Kartenspiel ohne Tisch beendet");
                return false;
            }
            catch { return true; }
        }
    }

    // 3) Destroyed enemy torpedo launcher group that errors every frame: switch off only that broken one.
    [HarmonyPatch]
    internal static class Guard_EnemyLauncher
    {
        private static MethodBase target;
        private static readonly HashSet<int> broken = new HashSet<int>();
        private static bool Prepare()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Items.NPCTorpedoLauncherGroup");
            target = t != null ? AccessTools.Method(t, "DoUpdate") : null;
            return target != null;
        }
        private static MethodBase TargetMethod() => target;
        private static bool Prefix(object __instance)
        {
            var o = __instance as UnityEngine.Object;
            return o == null || !broken.Contains(o.GetInstanceID());
        }
        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardEnemyLauncher")) return __exception;
            try
            {
                if (__instance is UnityEngine.Object o) broken.Add(o.GetInstanceID());
                if (__instance is Behaviour b) b.enabled = false;
                Guards.Once("launcher", "kaputte Torpedowerfer-Gruppe eines Gegners abgeschaltet");
            }
            catch { }
            return null;
        }
    }

    // 4) Ceiling fade area with a missing neighbour (after interior changes): switch off only that one.
    [HarmonyPatch]
    internal static class Guard_CeilingFade
    {
        private static MethodBase target;
        private static bool Prepare()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Effects.FadingCeilingArea");
            target = t != null ? AccessTools.Method(t, "DoUpdate") : null;
            return target != null;
        }
        private static MethodBase TargetMethod() => target;
        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardCeiling")) return __exception;
            try { if (__instance is Behaviour b) b.enabled = false; Guards.Once("ceiling", "kaputte Decken-Blende abgeschaltet"); } catch { }
            return null;
        }
    }

    // 5) Portrait bar keeps destroyed portraits after crew changes: clean the list.
    internal static class PortraitRepair
    {
        private static readonly FieldInfo portraits = AccessTools.Field(typeof(CharacterPortraitsUI), "portraits");
        private static readonly FieldInfo start = AccessTools.Field(typeof(CharacterPortraitsUI), "displayStartIndex");
        private static readonly FieldInfo end = AccessTools.Field(typeof(CharacterPortraitsUI), "displayEndIndex");
        private static readonly FieldInfo count = AccessTools.Field(typeof(CharacterPortraitsUI), "displayablePortraitsCount");
        private static readonly HashSet<int> dirty = new HashSet<int>();
        internal static void Mark(CharacterPortraitsUI ui) { if (ui != null) dirty.Add(ui.GetInstanceID()); }
        internal static void RepairIfDirty(CharacterPortraitsUI ui) { if (ui != null && dirty.Remove(ui.GetInstanceID())) Repair(ui); }
        internal static void Repair(CharacterPortraitsUI ui)
        {
            if (ui == null || portraits == null) return;
            try
            {
                var src = portraits.GetValue(ui) as CharacterPortraitUI[];
                if (src == null || src.Length == 0) return;
                var clean = new List<CharacterPortraitUI>();
                foreach (var p in src) if (p != null && p) clean.Add(p);
                if (clean.Count == src.Length) return;
                portraits.SetValue(ui, clean.ToArray());
                int live = clean.Count, s = 0;
                try { if (start != null) s = Mathf.Clamp((int)start.GetValue(ui), 0, live); } catch { }
                try { start?.SetValue(ui, s); } catch { }
                try { end?.SetValue(ui, Mathf.Clamp(live, s, live)); } catch { }
                try { count?.SetValue(ui, live); } catch { }
                Guards.Once("portraits", "zerstörte Porträts aus der Leiste entfernt");
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class Guard_PortraitsMark
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "Character_Added", "Character_Removed", "OnEnable", "OnDisable" })
            { var m = AccessTools.Method(typeof(CharacterPortraitsUI), n); if (m != null) yield return m; }
        }
        private static void Postfix(CharacterPortraitsUI __instance) { if (Guards.On("GuardPortraits")) PortraitRepair.Mark(__instance); }
    }

    [HarmonyPatch]
    internal static class Guard_PortraitsRun
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "RenderPortraits", "DoUpdate", "DisplayRange" })
            { var m = AccessTools.Method(typeof(CharacterPortraitsUI), n); if (m != null) yield return m; }
        }
        private static void Prefix(CharacterPortraitsUI __instance) { if (Guards.On("GuardPortraits")) PortraitRepair.RepairIfDirty(__instance); }
        private static Exception Finalizer(CharacterPortraitsUI __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardPortraits")) return __exception;
            PortraitRepair.Repair(__instance);
            return null;
        }
    }

    // 6) Duty schedule / squad screen keeps destroyed buttons when closing: clean lists, catch that error.
    [HarmonyPatch]
    internal static class Guard_ScheduleGroup
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "CreateCharacterButtons", "Start", "OnAddAnySailor", "OnDisbandAnySailor", "OnCharacterToggle" })
            { var m = AccessTools.Method(typeof(AssignmentManagementGroupUI), n); if (m != null) yield return m; }
        }
        private static void Prefix(AssignmentManagementGroupUI __instance) { if (Guards.On("GuardSchedule")) Compact(__instance); }
        private static Exception Finalizer(AssignmentManagementGroupUI __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardSchedule")) return __exception;
            Compact(__instance);
            Guards.Once("schedule", "Dienstplan-Fehler abgefangen");
            return null;
        }
        private static void Compact(object o)
        {
            if (o == null) return;
            try
            {
                foreach (var f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!(f.GetValue(o) is IList list) || list.IsReadOnly || list.IsFixedSize) continue;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var v = list[i];
                        if (v == null || (v is UnityEngine.Object u && !u)) list.RemoveAt(i);
                    }
                }
            }
            catch { }
        }
    }

    // 7) The squad +/- buttons sometimes change the type (sailor/officer) of other men: put it back.
    [HarmonyPatch]
    internal static class Guard_SquadTypeFence
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "OnAddAnySailor", "OnDisbandAnySailor" })
            { var m = AccessTools.Method(typeof(AssignmentManagementGroupUI), n); if (m != null) yield return m; }
        }
        private static void Prefix(out Dictionary<PlayableCharacterData, PlayableCharacterData.Type> __state)
        {
            __state = null;
            if (!Guards.On("GuardSquadType")) return;
            var crew = Util.Find<PlayerCrew>(30f);
            __state = new Dictionary<PlayableCharacterData, PlayableCharacterData.Type>();
            foreach (var pc in crew)
                if (pc != null && pc.Characters != null)
                    foreach (var c in pc.Characters) if (c != null && c.Data != null) __state[c.Data] = c.Data.CharacterType;
        }
        private static Exception Finalizer(Dictionary<PlayableCharacterData, PlayableCharacterData.Type> __state, Exception __exception)
        {
            if (__state == null) return __exception;
            foreach (var kv in __state)
                try
                {
                    if (kv.Key.CharacterType != kv.Value)
                    {
                        kv.Key.CharacterType = kv.Value;
                        Guards.Once("squadtype", "Trupp-Knopf hat einen Mann-Typ verändert - zurückgesetzt");
                    }
                }
                catch { }
            return __exception;
        }
    }

    // 8) Schedule controller keeps a destroyed part after changing game settings.
    [HarmonyPatch]
    internal static class Guard_ScheduleSettings
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Tasks.ExpandedSchedulesController");
            var m = t != null ? AccessTools.Method(t, "UserSettings_Changed") : null;
            if (m != null) yield return m;
        }
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardSchedule")) return __exception;
            Guards.Once("schedset", "Dienstplan-Fehler nach Einstellungen abgefangen");
            return null;
        }
    }

    // 9) Follower buttons on a portrait: hover check must not break the crew screen.
    [HarmonyPatch(typeof(CharacterPortraitUI), "ValidateFollowerButtons")]
    internal static class Guard_FollowerButtons
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardPortraits")) return __exception;
            Guards.Once("followers", "Gefolgs-Knopf-Fehler abgefangen");
            return null;
        }
    }

    // 10) Boarding checkboxes in port: the game removes/adds the man and destroys/spawns his body inside the click
    // (stutter with many clicks). Here the click only marks the box; the real changes run one by one afterwards
    // through the game's own handler, so all its rules (limits, healing, spawn point) stay the same.
    public class BoardingQueue : ModuleBase
    {
        private static readonly FieldInfo toggleAction = AccessTools.Field(typeof(CrewListUI), "toggleAction");
        private static readonly FieldInfo items = AccessTools.Field(typeof(CrewListUI), "items");
        private static readonly FieldInfo stateFunc = AccessTools.Field(typeof(CrewListUI), "toggleStateFunc");
        private static readonly List<PlayableCharacterData> order = new List<PlayableCharacterData>();
        private static readonly Dictionary<PlayableCharacterData, bool> want = new Dictionary<PlayableCharacterData, bool>();
        private static CrewListUI ui;
        private static float nextAt;
        internal static bool Busy;

        internal static bool IsDefault(CrewListUI u)
        {
            try { var a = toggleAction?.GetValue(u) as Action<PlayableCharacterData>; return a != null && a.Method.Name == "DefaultToggleHandler"; }
            catch { return false; }
        }

        internal static bool Board(PlayableCharacterData d)
        {
            var crew = Util.Find<PlayerCrew>(30f);
            foreach (var c in crew) if (c != null) return c.GetCharacter(d) != null;
            return false;
        }

        // Too many selected for the game's limits? Counts the ticked boxes (incl. waiting changes).
        internal static bool Full(CrewListUI u, PlayableCharacterData cand)
        {
            var career = Util.Career;
            if (career == null) return false;
            int sailors = 0, officers = 0; bool counted = false;
            foreach (var row in u.GetComponentsInChildren<CrewListItemUI>(true))
            {
                if (row == null || row.CharacterData == null || !row.Toggled) continue;
                if (ReferenceEquals(row.CharacterData, cand)) counted = true;
                if (row.CharacterData.CharacterType == PlayableCharacterData.Type.Sailor) sailors++; else officers++;
            }
            if (!counted) { if (cand.CharacterType == PlayableCharacterData.Type.Sailor) sailors++; else officers++; }
            return cand.CharacterType == PlayableCharacterData.Type.Sailor ? sailors > career.SailorsOnBoardLimit.Value : officers > career.OfficersOnBoardLimit.Value;
        }

        internal static void Queue(CrewListUI u, PlayableCharacterData d, bool board)
        {
            ui = u;
            if (Board(d) == board) { want.Remove(d); order.Remove(d); }
            else { if (!want.ContainsKey(d)) order.Add(d); want[d] = board; }
            nextAt = Time.unscaledTime + 0.35f;
        }

        public override void Frame()
        {
            if (order.Count == 0 || Time.unscaledTime < nextAt) return;
            var d = order[0]; order.RemoveAt(0);
            bool w = want.TryGetValue(d, out bool v) && v; want.Remove(d);
            Busy = true;
            try { if (d != null && Board(d) != w) CrewListUI.DefaultToggleHandler(d); }
            catch (Exception e) { Util.Log("Einschiffen: " + e.Message); }
            finally { Busy = false; }
            nextAt = Time.unscaledTime + 0.18f;
            RefreshRows();
        }

        // Same row refresh the game does after a click.
        private static void RefreshRows()
        {
            try
            {
                if (ui == null || !ui || !ui.gameObject.activeInHierarchy) return;
                var list = items?.GetValue(ui) as IList;
                var f = stateFunc?.GetValue(ui) as Func<PlayableCharacterData, CrewListUI.CharacterStateInfo>;
                if (list != null && f != null)
                    for (int i = list.Count - 1; i >= 0; i--)
                        if (list[i] is CrewListItemUI it && it)
                        {
                            var s = f(it.CharacterData);
                            it.State = s.State; it.Toggleable = s.Toggleable;
                        }
                ui.ValidateCountLabels();
            }
            catch { }
        }

        internal static void FlushNow() { nextAt = 0f; }
    }

    [HarmonyPatch(typeof(CrewListUI), "OnToggle")]
    internal static class Boarding_TogglePatch
    {
        private static bool Prefix(CrewListUI __instance, CrewListItemUI item, bool value)
        {
            if (!Settings.B("BoardingQueue") || BoardingQueue.Busy || !BoardingQueue.IsDefault(__instance)) return true;
            if (item == null || item.CharacterData == null) return true;
            if (value && !BoardingQueue.Board(item.CharacterData) && BoardingQueue.Full(__instance, item.CharacterData))
            {
                try { item.Toggled = false; } catch { }
                return false;
            }
            BoardingQueue.Queue(__instance, item.CharacterData, value);
            try { __instance.ValidateCountLabels(); } catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(CrewListUI), "OnDisable")]
    internal static class Boarding_ClosePatch
    {
        private static void Postfix() => BoardingQueue.FlushNow();
    }
}

using System.Collections.Generic;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Characters.Skills;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #34/#35: keep the depth steers (Tiefenruder) and the navigation table manned.
    // If a station stays empty for a few seconds, the nearest free sailor is sent there
    // with the game's own work action (sailors handle both very well).
    public class Stations : ModuleBase
    {
        private PlayerCrew crew;
        private readonly Dictionary<Activator, float> emptySince = new Dictionary<Activator, float>();
        private readonly List<CharacterActionInfo> infos = new List<CharacterActionInfo>();

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null) return;
            var ship = Util.PlayerShip;
            if (Settings.B("KeepDepthStation"))
                foreach (var st in Util.Find<DivingPlanesStation>())
                    Keep(st.GetComponent<Activator>(), "Tiefenruder");
            if (Settings.B("KeepNavTable"))
                foreach (var st in Util.Find<NavigationTable>())
                    Keep(st.GetComponent<Activator>(), "Kartentisch");

            bool guns = Settings.B("AutoManGuns") && ship.DeckDepth < 1f && ship.Alarmed;
            bool engines = Settings.B("AutoManEngines") && !ship.Docked;
            if (guns || engines)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null) continue;
                    var eq = act.AssociatedEquipment;
                    if (guns && eq is Gun) Keep(act, "Geschütz");
                    else if (engines && eq is PlayerShipEngine eng && eng.GearIndex != 0) Keep(act, "Maschinenraum");
                }

            // More sailors per officer.
            float max = Settings.F("MaxFollowers");
            if (max > 0f || hadMax)
            {
                foreach (var c in crew.Characters)
                    if (c != null && c.IsOfficer) Util.SetParameter(c.Data.GetStatus<Authority>()?.FollowersLimit, max);
                hadMax = max > 0f;
            }
        }
        private bool hadMax;

        private void Keep(Activator act, string label)
        {
            if (act == null || !act.isActiveAndEnabled) return;
            if (act.Users.Count > 0) { emptySince.Remove(act); return; }
            if (!emptySince.TryGetValue(act, out float since)) { emptySince[act] = Time.time; return; }
            if (Time.time - since < Settings.F("StationDelay")) return;

            PlayableCharacter best = null; CharacterAction tmpl = null; float bestD = float.MaxValue;
            foreach (var h in crew.Characters)
            {
                if (h == null || h.IsOfficer || !IsFree(h)) continue;
                var t = FindWork(act, h);
                if (t == null) continue;
                float d = (h.transform.position - act.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; tmpl = t; }
            }
            if (best == null) return;
            var a = tmpl.Duplicate(act);
            a.QueuePriority = 90f;
            best.StopCurrentAction(true);
            best.StartAction(act, a);
            emptySince.Remove(act);
            Util.Log(label + ": " + best.Name + " übernimmt");
        }

        // The game's sailor work action for this station (BasicWorkAction / UseNavigationTableAction), if allowed now.
        private CharacterAction FindWork(Activator act, PlayableCharacter h)
        {
            act.GetActionInfos(infos, h, ActionContext.Normal, true);
            foreach (var i in infos)
                if (i.ActionTemplate != null && i.State == ActionState.Enabled &&
                    (i.ActionTemplate is BasicWorkAction || i.ActionTemplate is UseNavigationTableAction ||
                     i.ActionTemplate is UseGun || i.ActionTemplate is ManageEnginesAction))
                    return i.ActionTemplate;
            return null;
        }

        private static bool IsFree(PlayableCharacter h)
        {
            if (h.Injury || h.Ai == null || !h.Ai.isActiveAndEnabled || h.IsImpersonated || h.IsSelected) return false;
            var a = h.Action;
            return a == null || (a.CanBeStopped && a.Role != null);
        }
    }
}

namespace UboatToolkit.Modules
{
    // Officers may take every available sailor (the game only allows 60 % without alarm).
    [HarmonyLib.HarmonyPatch(typeof(PlayerCrew), "ValidateWorkingCharactersCount")]
    internal static class Stations_FollowersPatch
    {
        private static void Postfix(PlayerCrew __instance)
        {
            if (!Settings.B("FollowersNoLimit") || __instance.Characters == null) return;
            int n = 0;
            foreach (var c in __instance.Characters)
                if (c != null && c.Data != null && c.Data.CharacterType == PlayableCharacterData.Type.Sailor && !c.Injury && c.Health > 0f) n++;
            var t = HarmonyLib.Traverse.Create(__instance);
            t.Field("maxOfficerFollowersCount").SetValue(n);
            t.Field("maxOfficerFollowersCountWithoutAlarm").SetValue(n);
        }
    }
}

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
        // Against "X takes over" every few seconds: if the last man left again quickly, wait longer
        // before sending the next one (30 s, 60 s, ... up to 10 min). Resets once a man stays 2 minutes.
        private readonly Dictionary<Activator, float> assignedAt = new Dictionary<Activator, float>();
        private readonly Dictionary<Activator, float> backoff = new Dictionary<Activator, float>();
        private readonly HashSet<Activator> warned = new HashSet<Activator>();
        private readonly Dictionary<Activator, float> waitUntil = new Dictionary<Activator, float>();
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

            // Hydrophone: under water or alarm. Radio room: whenever the radio can receive.
            bool hydro = Settings.B("AutoManHydrophone") && (ship.SubmergedOrGoingToSubmerge || ship.Alarmed);
            bool radio = Settings.B("AutoManRadio") && !ship.Docked;   // in port nobody needs to listen
            if (hydro || radio)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null) continue;
                    var eq = act.AssociatedEquipment;
                    if (hydro && eq is ListeningRoom) Keep(act, "Horcher");
                    else if (radio && eq is PlayerShipRadio) Keep(act, "Funkraum");
                }

            bool guns = ship.DeckDepth < 1f && ((Settings.B("AutoManGuns") && ship.Alarmed) || Time.unscaledTime < UnderFire.ManGunsUntil);
            bool engines = Settings.B("AutoManEngines") && !ship.Docked;
            if (guns || engines)
                foreach (var act in Util.Find<Activator>())
                {
                    if (act == null) continue;
                    var eq = act.AssociatedEquipment;
                    if (guns && eq is Gun) Keep(act, "Geschütz");
                    else if (engines && eq is PlayerShipEngine eng && eng.GearIndex != 0) Keep(act, "Maschinenraum");
                }

            // Echo sounder as a sailor job: under water a free sailor goes there every x seconds and pings (game's own PingDeep action).
            float every = Settings.F("EchoSailorSeconds");
            // The interval is game time; under time compression that meant a new sailor pulled from his work every few
            // real seconds (log: "Echolot: X übernimmt" every 6 s, rotating names). Now also at least 60 real seconds apart.
            if (Settings.B("EchoSailor") && every > 0f && ship.SubmergedOrGoingToSubmerge && Time.time - lastPing > every
                && Time.unscaledTime - lastPingReal > 60f
                && !(Settings.B("EchoSailorQuiet") && ship.Alarmed))
                foreach (var act in Util.Find<Activator>())
                    if (act != null && act.AssociatedEquipment is SoundRangingDevice && act.Users.Count == 0)
                    {
                        if (Send(act, "Echolot", t => t is PingDeep)) { lastPing = Time.time; lastPingReal = Time.unscaledTime; }
                        break;
                    }

            // More sailors per officer.
            // Auto: every sailor on board fits into an officer's group (sailors / officers, rounded up),
            // and an officer never has more followers than he may lead (the "10/6" display).
            float max = Settings.F("MaxFollowers");
            bool auto = Settings.B("FollowersAuto");
            if (max > 0f || auto || hadMax)
            {
                int officers = 0, sailors = 0;
                foreach (var c in crew.Characters) { if (c == null) continue; if (c.IsOfficer) officers++; else sailors++; }
                float share = officers > 0 ? Mathf.Ceil(sailors / (float)officers) : 0f;
                foreach (var c in crew.Characters)
                {
                    if (c == null || !c.IsOfficer) continue;
                    var a = c.Data.GetStatus<Authority>();
                    if (a == null) continue;
                    float want = max;
                    if (auto) want = Mathf.Max(want, Mathf.Max(share, a.Followers.Count));
                    if (auto && want > 0f && want <= a.FollowersLimit.GetValueExcludingModifier(Util.ModifierName) && max <= 0f) want = 0f;   // game value already enough
                    Util.SetParameter(a.FollowersLimit, want);
                }
                hadMax = max > 0f || auto;
            }
        }
        private bool hadMax;
        private float lastPing = -9999f, lastPingReal = -9999f;

        // Sends the nearest free sailor to do one action at this activator (for one-off jobs).
        private bool Send(Activator act, string label, System.Func<CharacterAction, bool> accept)
        {
            PlayableCharacter best = null; CharacterAction tmpl = null; float bestD = float.MaxValue;
            foreach (var h in crew.Characters)
            {
                if (h == null || h.IsOfficer || !IsFree(h)) continue;
                act.GetActionInfos(infos, h, ActionContext.Normal, true);
                CharacterAction t = null;
                foreach (var i in infos)
                    if (i.ActionTemplate != null && i.State == ActionState.Enabled && accept(i.ActionTemplate)) { t = i.ActionTemplate; break; }
                if (t == null) continue;
                float d = (h.transform.position - act.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = h; tmpl = t; }
            }
            if (best == null) return false;
            var a = tmpl.Duplicate(act);
            a.QueuePriority = 90f;
            best.StopCurrentAction(true);
            best.StartAction(act, a);
            Util.Log(label + ": " + best.Name + " übernimmt");
            return true;
        }

        private void Keep(Activator act, string label)
        {
            if (act == null || !act.isActiveAndEnabled) return;
            if (act.Users.Count > 0 || OnTheWay(act))
            {
                emptySince.Remove(act);
                if (assignedAt.TryGetValue(act, out float at) && Time.time - at > 120f) { backoff.Remove(act); assignedAt.Remove(act); }
                return;
            }
            if (!emptySince.TryGetValue(act, out float since))
            {
                emptySince[act] = Time.time;
                if (assignedAt.TryGetValue(act, out float at) && Time.time - at < 120f)
                {
                    float b = backoff.TryGetValue(act, out float old) ? Mathf.Min(600f, old * 2f) : 30f;
                    backoff[act] = b;
                    waitUntil[act] = Time.time + b;
                    if (b >= 600f && !warned.Contains(act)) { warned.Add(act); Util.Log(label + ": Leute verlassen den Platz immer wieder, warte jetzt 10 min"); }
                }
                assignedAt.Remove(act);
                return;
            }
            if (waitUntil.TryGetValue(act, out float until) && Time.time < until) return;
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
            assignedAt[act] = Time.time;
            Util.Log(label + ": " + best.Name + " übernimmt");
        }

        // The game's sailor work action for this station (BasicWorkAction / UseNavigationTableAction), if allowed now.
        private CharacterAction FindWork(Activator act, PlayableCharacter h)
        {
            act.GetActionInfos(infos, h, ActionContext.Normal, true);
            foreach (var i in infos)
                if (i.ActionTemplate != null && i.State == ActionState.Enabled &&
                    (i.ActionTemplate is BasicWorkAction || i.ActionTemplate is UseNavigationTableAction ||
                     i.ActionTemplate is UseGun || i.ActionTemplate is ManageEnginesAction ||
                     i.ActionTemplate is HydrophoneUseAction || i.ActionTemplate is SailorRadioUsageAction))
                    return i.ActionTemplate;
            return null;
        }

        // Someone is already walking there for this station. Without this check a second man was sent
        // while the first was still on his way; with two men at the radio the game ends the job for both
        // (SailorRadioUsageAction: more than one user -> complete) and both walked off again.
        private bool OnTheWay(Activator act)
        {
            foreach (var h in crew.Characters)
            {
                if (h == null) continue;
                var a = h.Action;
                if (a != null && a.Activator == act) return true;
                var q = h.ActionQueue;
                if (q != null && q.Actions != null)
                    foreach (var qa in q.Actions) if (qa != null && qa.Activator == act) return true;
            }
            return false;
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

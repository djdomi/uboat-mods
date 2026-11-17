using System.Linq;
using HarmonyLib;
using UnityEngine;
using DWS.Common.Resources;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // #26 / cheats: unlimited torpedoes (the tube is reloaded right after the shot, the same way the game
    // itself loads tubes at campaign start), optional T5 "Zaunkönig" homing torpedoes, and no duds.
    public class Torpedoes : ModuleBase
    {
        public const string T5 = "G7es Zaunkönig Torpedo T5 - Pi4";
        private static ResourceManager resources;

        public static ResourceManager Resources
        {
            get
            {
                if (resources == null) resources = UnityEngine.Resources.FindObjectsOfTypeAll<ResourceManager>().FirstOrDefault();
                return resources;
            }
        }

        private static UserSettings userSettings;

        // 0.3.43 (DoMe: "es werden keine Torpedos nachgeladen"): the reload right after the shot (OnLaunchingComplete)
        // did not fire in every case. Backup: an own tube that stays empty for 3 s is filled again here.
        private static readonly System.Collections.Generic.Dictionary<TorpedoLauncher, string> lastName = new System.Collections.Generic.Dictionary<TorpedoLauncher, string>();
        private static readonly System.Collections.Generic.Dictionary<TorpedoLauncher, float> emptySince = new System.Collections.Generic.Dictionary<TorpedoLauncher, float>();
        // 0.3.52 (DoMe: "zusätzliche option T1, t2, t,3, 4, t5 oder mine"): ReloadType 0 = like last, 1-5 = T1-T5, 6 = mine.
        // Picks the item with the newest pistol from the equipment database.
        private static readonly System.Collections.Generic.Dictionary<int, string> pickCache = new System.Collections.Generic.Dictionary<int, string>();
        internal static string Pick(string last)
        {
            int t = Mathf.RoundToInt(Settings.F("ReloadType"));
            if (t <= 0 && Settings.B("CheatAllT5")) t = 5;
            if (t <= 0 || t > 6) return string.IsNullOrEmpty(last) ? T5 : last;
            if (pickCache.TryGetValue(t, out var c)) return c;
            string best = null; float bp = -1f;
            var db = UnityEngine.Resources.FindObjectsOfTypeAll<UBOAT.Game.Sandbox.Devices.EquipmentDatabase>().FirstOrDefault();
            if (db != null)
                foreach (var e in db.GetEquipmentDirect())
                {
                    string n = e != null ? e.Name : null;
                    if (string.IsNullOrEmpty(n)) continue;
                    bool mine = Controls_TorpedoMixPatch.IsMine(n);
                    bool ok = t == 6 ? mine : !mine && n.IndexOf("Torpedo T" + t, System.StringComparison.Ordinal) >= 0 && n.IndexOf("LUT", System.StringComparison.Ordinal) < 0;
                    if (!ok) continue;
                    float p = Controls_TorpedoMixPatch.Pistol(n);
                    if (p > bp) { bp = p; best = n; }
                }
            if (best == null) { Util.Log("Nachladen: kein Typ T" + t + " gefunden, nehme " + (string.IsNullOrEmpty(last) ? T5 : last)); return string.IsNullOrEmpty(last) ? T5 : last; }
            Util.Log("Nachladen: Typ " + t + " = " + best);
            pickCache[t] = best; return best;
        }

        private static void Refill()
        {
            if (!Settings.B("CheatInfiniteTorpedoes")) return;
            foreach (var l in Util.Find<TorpedoLauncher>(2f))
            {
                if (l == null || !(Util.IsPlayers(l) || (l.Group != null && l.Group.ParentEntity is PlayerShip))) continue;
                if (l.Torpedo != null)
                {
                    emptySince.Remove(l);
                    lastName[l] = l.Torpedo.name.Replace("(Clone)", "").Trim();
                    continue;
                }
                if (!emptySince.TryGetValue(l, out var t0)) { emptySince[l] = Time.time; continue; }
                if (Time.time - t0 < 3f) continue;
                emptySince.Remove(l);
                lastName.TryGetValue(l, out var last);
                Util.Safe("torpedo refill", () =>
                {
                    var rm = Resources; if (rm == null) return;
                    string name = Pick(last);
                    var go = rm.InstantiatePrefab("Equipment/" + name);
                    if (go == null && !string.IsNullOrEmpty(last) && name != last) go = rm.InstantiatePrefab("Equipment/" + last);
                    var stored = go != null ? go.GetComponent<StoredTorpedo>() : null;
                    if (stored == null) { if (go != null) Object.Destroy(go); Util.Log("Torpedo nachladen: kein Torpedo-Typ gefunden (" + name + ")"); return; }
                    l.SetTorpedo(stored);
                    l.CompleteLoading(quiet: true);
                    Util.Log("Torpedo nachgeladen (Cheat): " + l.name + " <- " + name);
                });
            }
        }

        // 0.3.86 (DoMe 2026-11-16 BUG: "Torpedos an Bord, aber es wird nichts mehr nachgeladen"): an own tube that stays
        // empty for AutoReloadSeconds gets the nearest torpedo from the boat's own storage (nothing is created).
        // Once a minute the tube states and the stock are logged while a tube is empty.
        private static readonly System.Collections.Generic.Dictionary<TorpedoLauncher, float> autoEmpty = new System.Collections.Generic.Dictionary<TorpedoLauncher, float>();
        private static float nextTubeLog;
        private static void AutoReload()
        {
            if (!Settings.B("AutoReloadTubes") || Settings.B("CheatInfiniteTorpedoes")) { autoEmpty.Clear(); return; }
            float wait = Settings.F("AutoReloadSeconds");
            var ship = Util.PlayerShip;
            if (ship == null || ship.Docked) { autoEmpty.Clear(); return; }   // in port the port's own stock is not touched
            Storage[] storages = null;
            int empty = 0, loaded = 0;
            string states = "";
            foreach (var l in Util.Find<TorpedoLauncher>(2f))
            {
                if (l == null || !(Util.IsPlayers(l) || (l.Group != null && l.Group.ParentEntity is PlayerShip))) continue;
                states += (states.Length > 0 ? ", " : "") + l.CurrentState;
                if (l.Torpedo != null) { autoEmpty.Remove(l); continue; }
                empty++;
                if (!autoEmpty.TryGetValue(l, out var t0)) { autoEmpty[l] = Time.time; continue; }
                if (Time.time - t0 < wait) continue;
                if (storages == null) storages = Util.Find<Storage>().Where(x => x != null && !(x is TorpedoLauncherGroup) && x.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() == null).ToArray();
                Storage best = null; int bestSlot = -1; float bestDist = float.MaxValue;
                foreach (var st in storages)
                    for (int i = 0; i < Util.Slots(st); i++)
                    {
                        if (!(st.GetItemAt(i) is StoredTorpedo t) || t is StoredMinePacket || st.HasTransportOrders(i)) continue;
                        float dist = (st.transform.position - l.transform.position).sqrMagnitude;
                        if (dist < bestDist) { best = st; bestSlot = i; bestDist = dist; }
                    }
                if (best == null) continue;
                var tube = l; var from = best; int slot = bestSlot;
                Util.Safe("auto reload", () =>
                {
                    var torp = (StoredTorpedo)from.GetItemAt(slot);
                    from.SetItemAt(slot, null);
                    tube.SetTorpedo(torp);
                    tube.CompleteLoading(quiet: true);
                    Util.Log("Rohr automatisch aus dem Lager geladen: " + tube.name + " <- " + torp.name);
                });
                autoEmpty.Remove(l); loaded++;
            }
            if (empty > loaded && Time.unscaledTime > nextTubeLog)
            {
                nextTubeLog = Time.unscaledTime + 60f;
                int stock = 0;
                if (storages == null) storages = Util.Find<Storage>().Where(x => x != null && !(x is TorpedoLauncherGroup) && x.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() == null).ToArray();
                foreach (var st in storages)
                    for (int i = 0; i < Util.Slots(st); i++) if (st.GetItemAt(i) is StoredTorpedo t && !(t is StoredMinePacket)) stock++;
                Util.Log("Rohre: " + empty + " leer, Zustände [" + states + "], Torpedos im Lager: " + stock + " (Lager gefunden: " + storages.Length + ")");
                Util.Safe("tube diag", CrewLoadDiag);
            }
        }

        // Why does the crew not load by itself (DoMe 2026-11-16)? The game's own loading needs: a free tube that is
        // accessible and open for the crew, a torpedo in the tube room's own rack, and an officer whose role has the
        // loading job. Each of these is written to the log.
        private static void CrewLoadDiag()
        {
            var sb = new System.Text.StringBuilder("Lade-Diagnose: ");
            foreach (var l in Util.Find<TorpedoLauncher>(2f))
            {
                var x = l as ExtendedTorpedoLauncher;
                if (x == null || x.Torpedo != null) continue;
                sb.Append("[").Append(x.name).Append(" Zugang=").Append(x.GetAccessibilityStatus())
                  .Append(" Aufträge=").Append(x.Slot != null ? x.Slot.TransportOrders.Count : -1)
                  .Append(" KI-offen=").Append(x.LoadingInteractable ? x.LoadingInteractable.IsOpenForAI.ToString() : "kein Ladeplatz");
                var rack = x.Group != null ? x.Group.TorpedoStorage : null;
                int n = 0;
                if (rack != null) for (int i = 0; i < Util.Slots(rack); i++) if (rack.GetItemAt(i) is StoredTorpedo t && !(t is StoredMinePacket)) n++;
                sb.Append(" Gestell=").Append(rack != null ? n.ToString() : "keins").Append("] ");
            }
            var crew = Object.FindObjectOfType<UBOAT.Game.Scene.Characters.PlayerCrew>();
            if (crew != null && crew.Officers != null)
                foreach (var o in crew.Officers)
                {
                    if (o == null) continue;
                    bool job = o.Role != null && o.Role.HasAction<UBOAT.Game.Scene.Characters.Actions.LoadTorpedoesAction>();
                    sb.Append("{").Append(o.name).Append(job ? " LADE-ROLLE" : "").Append(" tut=").Append(o.Action != null ? o.Action.GetType().Name : "nichts").Append("} ");
                }
            Util.Log(sb.ToString());
            // where do the torpedoes on board lie? (the tube's selection window only shows the tube room's rack)
            var where = new System.Text.StringBuilder("Torpedo-Lagerorte: ");
            foreach (var st in Util.Find<Storage>())
            {
                if (st == null || st.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() != null) continue;
                int n = 0, slots = Util.Slots(st);
                for (int i = 0; i < slots; i++) if (st.GetItemAt(i) is StoredTorpedo) n++;
                if (n > 0 || st is TorpedoStorage) where.Append(st.name).Append(" (").Append(st.GetType().Name).Append(", Typ ").Append(st.StorageType).Append("): ").Append(n).Append(" von ").Append(slots).Append(" Plätzen; ");
            }
            Util.Log(where.ToString());
        }

        public override void Tick()
        {
            Util.Safe("AutoReload", AutoReload);
            // #17: use the game's own automatic torpedo solution (click target -> values are filled in).
            if (Settings.B("EasyTDC"))
            {
                if (userSettings == null) userSettings = UnityEngine.Resources.FindObjectsOfTypeAll<UserSettings>().FirstOrDefault();
                var gs = userSettings?.GameplaySettings;
                if (gs != null)
                {
                    gs.disableAutomaticTorpedoSolutions = false;
                    gs.disableTorpedoSolutionPreviews = false;
                }
            }

            Refill();
            if (!Settings.B("CheatNoDuds")) return;
            foreach (var t in Util.Find<Torpedo>(1f))
                if (t != null && t.Owner == Util.PlayerShip) t.DudsDisabled = true;   // 0.3.38: own torpedoes only
        }
    }

    [HarmonyPatch(typeof(TorpedoLauncher), nameof(TorpedoLauncher.OnLaunchingComplete))]
    internal static class Torpedoes_ReloadPatch
    {
        private static void Prefix(TorpedoLauncher __instance, out string __state)
        {
            __state = null;
            if (!Settings.B("CheatInfiniteTorpedoes") || !Util.IsPlayers(__instance)) return;
            var fired = __instance.Torpedo;
            if (fired != null) __state = fired.name.Replace("(Clone)", "").Trim();
        }

        private static void Postfix(TorpedoLauncher __instance, string __state)
        {
            if (string.IsNullOrEmpty(__state) || __instance.Torpedo != null) return;
            Util.Safe("torpedo reload", () =>
            {
                var rm = Torpedoes.Resources;
                if (rm == null) return;
                string name = Torpedoes.Pick(__state);
                var go = rm.InstantiatePrefab("Equipment/" + name);
                if (go == null && name != __state) go = rm.InstantiatePrefab("Equipment/" + __state);
                var stored = go != null ? go.GetComponent<StoredTorpedo>() : null;
                if (stored == null) { if (go != null) Object.Destroy(go); return; }
                __instance.SetTorpedo(stored);
                __instance.CompleteLoading(quiet: true);
            });
        }
    }
    // QoL: a torpedo the crew has maintained stays maintained (player's boat only). The game normally drops the
    // "WarmedUp" dud bonus when the maintenance cooldown runs out and the crew then maintains the tube again.
    internal static class TorpedoKeep
    {
        private static readonly System.Reflection.FieldInfo GameTimeF = AccessTools.Field(typeof(StoredTorpedo), "gameTime");
        internal static readonly AccessTools.FieldRef<StoredTorpedo, long> WarmUpTicks = AccessTools.FieldRefAccess<StoredTorpedo, long>("warmUpTicks");
        private static bool logged;

        internal static bool Wanted(StoredTorpedo t)
        {
            if (t == null || !Settings.B("TorpedoStayMaintained") || t.IsLaunched || !t.IsMaintained) return false;
            var ship = Util.PlayerShip;
            if (ship == null) return false;
            return (object)t.ParentEntity == ship || Util.IsPlayers(t);
        }

        internal static bool Refresh(StoredTorpedo t)
        {
            var gt = GameTimeF != null ? GameTimeF.GetValue(null) as GameTime : null;
            if (gt == null) return false;
            WarmUpTicks(t) = gt.ContinuousTicks;
            if (!logged) { logged = true; Util.Log("Torpedos bleiben gewartet: " + t.name + " bleibt gewartet"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "RemoveWarmUpModifier")]
    internal static class Torpedoes_KeepMaintainedPatch
    {
        private static bool Prefix(StoredTorpedo __instance, ref float __result)
        {
            try
            {
                if (!TorpedoKeep.Wanted(__instance) || !TorpedoKeep.Refresh(__instance)) return true;
                __result = Mathf.Max(60f, (float)__instance.MaintenanceCooldown);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), nameof(StoredTorpedo.MaintenanceCooldownProgress), MethodType.Getter)]
    internal static class Torpedoes_KeepProgressPatch
    {
        private static void Postfix(StoredTorpedo __instance, ref float __result)
        {
            try
            {
                if (__result > 0.05f && __result < 1f && TorpedoKeep.Wanted(__instance) && TorpedoKeep.Refresh(__instance)) __result = 0f;
            }
            catch { }
        }
    }
}

using System.Collections.Generic;
using UBOAT.Game.Sandbox.Missions;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #30: automatic damage control. During an alarm, after the boat took damage,
    // engineer officers switch to damage control (the same toggle as on the portrait).
    // When the damage is checked or the alarm ends, they go back to their normal job.
    // QoL #31: automatic production. Scrap from all storages is moved into the workshop,
    // so the engineers keep making replacement parts (game's own salvage job).
    public class Repairs : ModuleBase
    {
        private PlayerCrew crew;
        private readonly HashSet<PlayableCharacter> switched = new HashSet<PlayableCharacter>();
        private float nextDc, nextWs;

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null) return;
            if (Time.time >= nextDc) { nextDc = Time.time + 2f; Util.Safe("DamageControl", DamageControl); }
            if (Time.time >= nextWs) { nextWs = Time.time + 10f; Util.Safe("Workshop", FeedWorkshop); }
        }

        private void DamageControl()
        {
            var ship = Util.PlayerShip;
            var cs = ship.CommandSpot != null ? ship.CommandSpot.GetComponent<CommandStation>() : null;
            bool need = Settings.B("AutoDamageControl") && ship.Alarmed && cs != null && cs.IsInspectionRequired;

            if (!need)
            {
                foreach (var o in switched)
                    if (o != null && o.RoleOverride != null && o.RoleOverride.Name == "ForcedRest")
                        MissionUtility.SetForcedRest(o, false);
                switched.Clear();
                return;
            }

            int wanted = Mathf.RoundToInt(Settings.F("AutoDamageControlCount"));
            switched.RemoveWhere(o => o == null || o.RoleOverride == null);
            var officers = new List<PlayableCharacter>(crew.Officers);
            officers.Sort((a, b) => DcScore(b).CompareTo(DcScore(a)));
            foreach (var o in officers)
            {
                if (switched.Count >= wanted) break;
                if (o == null || switched.Contains(o) || o.RoleOverride != null) continue;
                string cls = o.Data.characterClass;
                bool ok = cls == "Engineer" || (cls == "Radioman" && Settings.B("AutoDamageControlRadioman"));
                if (!ok || o.Injury || o.IsSelected || o.IsImpersonated) continue;
                MissionUtility.SetForcedRest(o, true);
                switched.Add(o);
                Util.Log("Schadenskontrolle: " + o.Name);
            }
        }

        // Higher = sent first. Handyman repairs 25% faster, Thrifty gets scrap back from
        // used replacement parts. Engine Specialist stays at the engines if possible.
        private static int DcScore(PlayableCharacter o)
        {
            if (o == null || !Settings.B("AutoDamageControlSkills")) return 0;
            int s = 0;
            if (o.Data.HasStatus("Handyman")) s += 4;
            if (o.Data.HasStatus("Thrifty")) s += 2;
            if (o.Data.HasStatus("Engine Specialist")) s -= 3;
            return s;
        }

        private static void FeedWorkshop()
        {
            if (!Settings.B("AutoWorkshop")) return;
            var ship = Util.PlayerShip;
            foreach (var ws in Util.Find<Workshop>())
            {
                var target = ws.Storage;
                if (target == null || target.GetTotalCount("Scrap") >= 2f) continue;
                foreach (var s in Util.Find<Storage>())
                {
                    if (s == target || s.IsShop) continue;
                    for (int i = s.SlotCount - 1; i >= 0; i--)
                    {
                        var cargo = s.GetItemAt(i) as Cargo;
                        if (cargo == null || cargo.TypeName != "Scrap") continue;
                        if (!StorageUtility.CanStoreItem(target, cargo)) return;
                        int free = target.GetFirstFreeSlotIndex();
                        if (free < 0) return;
                        StorageUtility.TransferInstantly(s, i, target, free, cargo.Count);
                        Util.Log("Werkstatt: Schrott eingeräumt");
                        if (target.GetTotalCount("Scrap") >= 2f) return;
                    }
                }
            }
        }
    }
}

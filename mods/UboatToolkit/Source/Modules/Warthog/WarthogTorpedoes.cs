using System.Collections.Generic;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Automatic torpedo loader (U-1337):
    //  - an empty tube reloads itself from the nearest inside rack after "WhAutoLoadSeconds", no crew needed
    //  - the external deck containers refill the inside racks by themselves (also submerged)
    // Overlap with Toolkit "Torpedoes"/"Loading": those speed up the crew's own loading; this one needs no crew.
    public class WarthogTorpedoes : ModuleBase
    {
        private readonly Dictionary<int, float> emptySince = new Dictionary<int, float>();
        private float nextRefill;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (!WarthogSettings.Active(ship) || !Settings.B("WhAutoLoad")) { emptySince.Clear(); return; }
            var storages = Util.Find<TorpedoStorage>();
            var inside = new List<TorpedoStorage>(); var outside = new List<TorpedoStorage>();
            foreach (var s in storages) (s.transform.IsChildOf(ship.transform) ? outside : inside).Add(s);

            // 1) tubes
            float wait = Settings.F("WhAutoLoadSeconds");
            foreach (var l in Util.Find<TorpedoLauncher>(2f))
            {
                int id = l.GetInstanceID();
                if (l.Torpedo != null || l.IsFlooded || l.FloodingProgress > 0f) { emptySince.Remove(id); continue; }
                if (!emptySince.TryGetValue(id, out var t0)) { emptySince[id] = Time.time; continue; }
                if (Time.time - t0 < wait) continue;
                var src = Nearest(inside, l.transform.position, out int idx);
                if (src == null) continue;
                var slot = src[idx];
                var torpedo = slot.Item != null ? slot.Item.GetComponent<StoredTorpedo>() : null;
                if (torpedo == null) continue;
                slot.Item = null;
                l.SetTorpedo(torpedo);
                l.CompleteLoading(true);
                emptySince.Remove(id);
                Util.Log("Warthog: Rohr automatisch geladen");
            }

            // 2) racks inside <- containers outside (every few seconds, one torpedo per rack)
            if (Time.time < nextRefill || outside.Count == 0) return;
            nextRefill = Time.time + Mathf.Max(2f, wait * 0.5f);
            foreach (var dst in inside)
            {
                int free = FirstFree(dst);
                if (free < 0) continue;
                var src = Nearest(outside, dst.transform.position, out int idx);
                if (src == null) return;
                StorageUtility.TransferInstantly(src, idx, dst, free, 1f);
            }
        }

        private static TorpedoStorage Nearest(List<TorpedoStorage> list, Vector3 pos, out int index)
        {
            TorpedoStorage best = null; index = -1; float bd = float.MaxValue;
            foreach (var s in list)
                for (int i = 0; i < Util.Slots(s); i++)
                {
                    var it = s[i] != null ? s[i].Item : null;
                    if (it == null || it.GetComponent<StoredTorpedo>() == null) continue;
                    if (s.IsSlotBlockedDuringLoading(i)) continue;
                    float d = (s[i].transform.position - pos).sqrMagnitude;
                    if (d < bd) { bd = d; best = s; index = i; }
                }
            return best;
        }

        private static int FirstFree(TorpedoStorage s)
        {
            for (int i = 0; i < Util.Slots(s); i++) if (s[i] != null && s[i].Item == null && !s.IsSlotBlockedDuringLoading(i)) return i;
            return -1;
        }
    }
}

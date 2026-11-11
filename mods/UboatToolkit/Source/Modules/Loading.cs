using System.Collections.Generic;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #39: loading in port. With many items at once the port workers can get stuck and
    // nothing reaches the boat any more. While docked, transport orders between port and boat
    // that nobody works on (or that did not move for a while) are finished right away,
    // with the game's own "resolve instantly" function.
    public class Loading : ModuleBase
    {
        private readonly List<TransportOrder> buffer = new List<TransportOrder>();
        private readonly HashSet<TransportOrder> seen = new HashSet<TransportOrder>();
        private readonly Dictionary<TransportOrder, KeyValuePair<float, float>> progress = new Dictionary<TransportOrder, KeyValuePair<float, float>>();
        private float next;

        public override void Tick()
        {
            if (!Settings.B("InstantPortLoading") || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            var ship = Util.PlayerShip;
            if (!ship.Docked) { progress.Clear(); return; }

            seen.Clear();
            foreach (var s in ship.GetComponentsInChildren<Storage>(true))
                for (int i = 0; i < s.SlotCount; i++)
                {
                    buffer.Clear();
                    if (!s.GetTransportOrders(i, buffer)) continue;
                    foreach (var o in buffer) if (o != null) seen.Add(o);
                }

            int done = 0;
            float stuckAfter = Settings.F("PortLoadingStuckSeconds");
            foreach (var o in seen)
            {
                if (o.IsCompleted || o.CountLeft <= 0f || o.CountInTransit > 0f) continue;
                bool portToBoat = !IsShip(o.SourceStorage, ship) || !IsShip(o.TargetStorage, ship);
                if (!portToBoat) continue;

                bool idle = o.CharacterAction == null;
                bool stuck = false;
                if (!idle)
                {
                    if (progress.TryGetValue(o, out var p) && p.Key == o.CountLeft) stuck = Time.unscaledTime - p.Value > stuckAfter;
                    else progress[o] = new KeyValuePair<float, float>(o.CountLeft, Time.unscaledTime);
                }
                if (!idle && !stuck) continue;

                Util.Safe("Loading", () => StorageUtility.ResolveTransportOrderInstantly(o));
                progress.Remove(o);
                done++;
            }
            if (done > 0) Util.Log("Verladen: " + done + " Aufträge sofort erledigt");
        }

        private static bool IsShip(IItemStorage s, UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var c = s as Component;
            return c != null && c.transform.IsChildOf(ship.transform);
        }
    }
}

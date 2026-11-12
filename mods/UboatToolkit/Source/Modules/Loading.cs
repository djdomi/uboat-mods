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

        private UBOAT.Game.Scene.Tasks.PortServicesProvider provider;
        private double lastLeft = -1;
        private float lastChange;
        private int kicks;

        // Watchdog for the port resupply queue: if "loading" shows but nothing moves,
        // push it once more; if it is still stuck, cancel it so you can order again.
        // The menu switch "CancelPortLoading" cancels it by hand.
        private void Watchdog()
        {
            if (provider == null)
                provider = HarmonyLib.Traverse.Create(typeof(UBOAT.Game.Sandbox.Missions.TutorialUtility)).Field("portServicesProvider").GetValue<UBOAT.Game.Scene.Tasks.PortServicesProvider>();
            if (provider == null) return;
            var t = HarmonyLib.Traverse.Create(provider);
            var orders = t.Field("transportOrders").GetValue<List<TransportOrder>>();

            var cancel = Settings.Get("CancelPortLoading");
            if (cancel != null && cancel.On)
            {
                cancel.Value = 0f;
                Settings.Save();
                provider.CancelResupply();
                ToolkitRunner.ShowNotice("Verladen abgebrochen. Du kannst neu bestellen.");
                lastLeft = -1; kicks = 0;
                return;
            }
            var force = Settings.Get("FixMyDamnShipment");
            if (force != null && force.On)
            {
                force.Value = 0f;
                Settings.Save();
                ForceDeliver(provider, orders);
                lastLeft = -1; kicks = 0;
                return;
            }
            if (orders == null || orders.Count == 0) { lastLeft = -1; kicks = 0; return; }

            double left = 0;
            foreach (var o in orders) if (o != null) left += o.CountLeft;
            if (left != lastLeft) { lastLeft = left; lastChange = Time.unscaledTime; return; }
            if (Time.unscaledTime - lastChange < Settings.F("PortLoadingStuckSeconds")) return;

            lastChange = Time.unscaledTime;
            if (kicks == 0)
            {
                kicks++;
                Util.Log("Verladen hängt, neuer Anstoß");
                t.Method("AddDateListener").GetValue();
            }
            else
            {
                kicks = 0;
                provider.CancelResupply();
                ToolkitRunner.ShowNotice("Verladen hing fest und wurde abgebrochen. Bitte neu bestellen.");
            }
        }

        // "Fix my damn shipment": deliver EVERYTHING that is ordered into the boat right now,
        // including orders a worker is carrying, then clear whatever is left in the queue.
        private void ForceDeliver(UBOAT.Game.Scene.Tasks.PortServicesProvider p, List<TransportOrder> orders)
        {
            var all = new HashSet<TransportOrder>();
            if (orders != null) foreach (var o in orders) if (o != null) all.Add(o);
            var ship = Util.PlayerShip;
            if (ship != null)
                foreach (var s in Util.Find<Storage>())
                    for (int i = 0; i < Util.Slots(s); i++)
                    {
                        buffer.Clear();
                        if (s.GetTransportOrders(i, buffer)) foreach (var o in buffer) if (o != null) all.Add(o);
                    }
            int n = 0;
            foreach (var o in all)
            {
                if (o.IsCompleted) continue;
                try
                {
                    if (o.CountInTransit > 0f) { o.CountLeft += o.CountInTransit; o.CountInTransit = 0f; }
                    StorageUtility.ResolveTransportOrderInstantly(o);
                }
                catch (System.Exception ex) { Util.Log("Force: " + ex.Message); }
                if (!o.IsCompleted) o.Complete();
                n++;
            }
            p.CancelResupply();   // clears any leftovers and ends the "loading" state
            progress.Clear();
            ToolkitRunner.ShowNotice("FIX MY DAMN SHIPMENT: " + n + " Lieferungen erzwungen. Ruhe jetzt.");
        }

        public override void Tick()
        {
            if (Settings.B("PortLoadingFix") || Settings.B("FixMyDamnShipment") || Settings.B("CancelPortLoading")) Util.Safe("LoadingWatchdog", Watchdog);
            if (!Settings.B("InstantPortLoading") || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            var ship = Util.PlayerShip;
            if (!ship.Docked) { progress.Clear(); return; }

            seen.Clear();
            foreach (var s in Util.Find<Storage>())
                for (int i = 0; i < Util.Slots(s); i++)
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

namespace UboatToolkit.Modules
{
    // The real cause of the port hang: PortServicesProvider.AddDateListener moves ONE unit per loop
    // step on the main thread. With tiny item weight the wait per unit becomes 0 ticks, so a stack of
    // millions of units is moved in one endless loop -> the game freezes and the queue stays blocked
    // (no more ammo, items or fuel). Fix: move the whole order in one step.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.PortServicesProvider), "AddDateListener")]
    internal static class Loading_ResupplyPatch
    {
        private static readonly UBOAT.Game.Core.Data.GameDataReference durationFactor =
            new UBOAT.Game.Core.Data.GameDataReference("General/Settings/Trade/Transfer Duration Factor (s/kg)");

        private static bool Prefix(UBOAT.Game.Scene.Tasks.PortServicesProvider __instance)
        {
            bool instant = Settings.B("InstantPortLoading");
            bool fix = Settings.B("PortLoadingFix");
            if (!instant && !fix) return true;
            var t = HarmonyLib.Traverse.Create(__instance);
            var orders = t.Field("transportOrders").GetValue<System.Collections.Generic.List<UBOAT.Game.Scene.Items.TransportOrder>>();
            if (orders == null) return true;
            orders.RemoveAll(o => o == null || o.IsCompleted);     // base fix: drop dead orders that block the queue
            if (orders.Count == 0) return true;

            var order = orders[0];
            if (!instant)
            {
                // Base fix only: keep the game's normal waiting, but never let it run into the
                // endless loop (0 ticks per unit because of tiny weight, or giant stacks).
                long perUnit = order.ItemData.HasTag("Torpedo") ? 1 : (long)((double)order.ItemData.Mass * (double)(float)durationFactor * 10000000.0);
                if (perUnit >= 1 && order.CountLeft <= 10000f) return true;
            }
            t.Field("currentTransportOrder").SetValue(order);
            try
            {
                // Complete() fires the game's own handler, which starts the next order (back into this patch).
                UBOAT.Game.Scene.Utilities.StorageUtility.ResolveTransportOrderInstantly(order);
            }
            catch (System.Exception ex)
            {
                Util.Log("Verladen Fehler: " + ex.Message);
                if (!order.IsCompleted) order.Complete();
            }
            return false;
        }
    }
}

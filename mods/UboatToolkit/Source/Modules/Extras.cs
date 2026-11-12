using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Devices;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;

namespace UboatToolkit.Modules
{
    // Own ideas + remaining cheats:
    // research speed, sonar (echo sounder) auto-ping, all upgrades/boats unlocked, faster torpedoes,
    // quick travel, depth keys, reload all tubes, swap watch shifts, tidy storage, route ETA + range.
    public class Extras : ModuleBase
    {
        public override void Frame()
        {
            if (Util.PlayerShip == null) return;
            // depth keys: see DepthGauge (key alone / Shift x10 / Alt x100)
            if (Keys.Down("KeyReloadAll")) Util.Safe("ReloadAll", ReloadAll);
            if (Keys.Down("KeyQuickTravel") && Settings.B("CheatQuickTravel")) Util.Safe("QuickTravel", QuickTravel);
            if (Keys.Down("KeySortStorage")) Util.Safe("SortStorage", SortStorage);
            if (Keys.Down("KeySwapShifts")) Util.Safe("SwapShifts", SwapShifts);
            if (Keys.Down("KeyCallMilkCow")) Util.Safe("CallMilkCow", CallMilkCow);
            if (Keys.Down("KeyBackToWater")) { var o = Settings.Get("BackToWaterNow"); if (o != null) o.Value = 1; }
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            OneShot("ReloadAllNow", ReloadAll);
            OneShot("SwapShiftsNow", SwapShifts);
            OneShot("SortStorageNow", SortStorage);
            OneShot("CheatUnlockAll", UnlockAll);
            OneShot("CheatQuickTravelNow", QuickTravel);
            OneShot("CallMilkCow", CallMilkCow);

            // Research at the headquarters x faster (instant = finishes within a second).
            Util.Safe("research", () => Util.ScaleParameter(ship.SandboxEntity?.Country?.Headquarters?.TaskExecutionPerformance,
                Settings.B("CheatInstantResearch") ? 1000000f : Settings.F("CheatResearchSpeed")));
            OneShot("CheatResearchAll", () => ResearchAll(ship));
            OneShot("CheatAddReputation", () =>
            {
                var c = Util.Career; if (c == null) return;
                int add = Mathf.Max(1, (int)Settings.F("CheatAddReputationAmount"));
                c.ReputationPoints += add;
                ToolkitRunner.ShowNotice("+" + add + " Ansehen (jetzt " + c.ReputationPoints + ")", 4f);
            });
            OneShot("CheatAddUpgradePoints", () =>
            {
                var c = Util.Career; if (c == null) return;
                int add = Mathf.Max(1, (int)Settings.F("CheatAddReputationAmount"));
                c.ShipUpgradePoints += add;
                ToolkitRunner.ShowNotice("+" + add + " Upgrade-Punkte (jetzt " + c.ShipUpgradePoints + ")", 4f);
            });

            // Echo sounder pings by itself while submerged.
            if (Settings.B("CheatSonarPing"))
                foreach (var d in Util.Find<SoundRangingDevice>())
                    d.IsSendButtonPressed = ship.SubmergedOrGoingToSubmerge;

            // Faster torpedoes: the speed values of all own torpedoes are raised, so the
            // torpedo computer also aims with the new speed.
            float tf = Settings.F("CheatTorpedoSpeed");
            foreach (var t in Util.Find<StoredTorpedo>(1f))
            {
                Util.ScaleParameter(t.Speed1, tf);
                Util.ScaleParameter(t.Speed2, tf);
                Util.ScaleParameter(t.Speed3, tf);
            }

            NavInfo(ship);
        }

        private static readonly Dictionary<string, int> oneShotTries = new Dictionary<string, int>();

        // A switch that does something once and turns itself off. Waits until the boat is fully
        // loaded; if it still fails, it tries again next second (up to 10 times) instead of giving up.
        private static void OneShot(string key, System.Action a)
        {
            var o = Settings.Get(key);
            if (o == null || !o.On || !Util.ShipReady) return;
            try { a(); o.Value = 0; oneShotTries.Remove(key); }
            catch (System.Exception e)
            {
                oneShotTries.TryGetValue(key, out int n); oneShotTries[key] = ++n;
                if (n >= 10) { o.Value = 0; oneShotTries.Remove(key); Util.Log("Aktion " + key + " fehlgeschlagen: " + e); }
            }
        }

        private static void ChangeDepth(float delta)
        {
            var ship = Util.PlayerShip;
            float d = Mathf.Max(0f, ship.TargetDepth + delta);
            ship.SetTargetDepth(d);
            ToolkitRunner.ShowNotice("Zieltiefe " + d.ToString("0") + " m", 2f);
        }

        // Every empty tube gets the nearest torpedo from the boat's own storage (nothing is created).
        private static void ReloadAll()
        {
            var ship = Util.PlayerShip;
            var storages = Util.Find<Storage>().Where(s => s.GetComponentInParent<PlayableCharacter>() == null).ToArray();
            int n = 0;
            foreach (var l in Util.Find<TorpedoLauncher>())
            {
                if (l.Torpedo != null) continue;
                Storage best = null; int bestSlot = -1; float bestDist = float.MaxValue;
                foreach (var s in storages)
                    for (int i = 0; i < Util.Slots(s); i++)
                    {
                        if (!(s.GetItemAt(i) is StoredTorpedo t) || t is StoredMinePacket || s.HasTransportOrders(i)) continue;
                        float dist = (s.transform.position - l.transform.position).sqrMagnitude;
                        if (dist < bestDist) { best = s; bestSlot = i; bestDist = dist; }
                    }
                if (best == null) break;
                var torp = (StoredTorpedo)best.GetItemAt(bestSlot);
                best.SetItemAt(bestSlot, null);
                l.SetTorpedo(torp);
                l.CompleteLoading(quiet: true);
                n++;
            }
            ToolkitRunner.ShowNotice(n > 0 ? n + " Rohr(e) nachgeladen" : "Kein leeres Rohr oder kein Torpedo im Lager", 4f);
        }

        // Groups with the same job swap their watch hours (e.g. tired watch goes to bed).
        private static void SwapShifts()
        {
            var crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew?.SailorGroups == null) return;
            int swapped = 0;
            foreach (var role in crew.SailorGroups.Where(g => g != null && g.Enabled && !g.Hidden).GroupBy(g => g.Role))
            {
                var list = role.ToList();
                if (list.Count < 2) continue;
                var first = Copy(list[0].Schedule);
                for (int i = 0; i < list.Count; i++)
                {
                    var src = i + 1 < list.Count ? Copy(list[i + 1].Schedule) : first;
                    for (int h = 0; h < 24; h++) list[i].Schedule[h] = src[h];
                }
                swapped++;
            }
            ToolkitRunner.ShowNotice(swapped > 0 ? "Wachen getauscht" : "Keine Gruppen zum Tauschen", 4f);
        }

        private static Schedule.Behaviour[] Copy(Schedule s)
        {
            var a = new Schedule.Behaviour[24];
            for (int h = 0; h < 24; h++) a[h] = s[h];
            return a;
        }

        // Tidy each storage: fill up same items into one stack, then move items to the front.
        private static void SortStorage()
        {
            int moves = 0;
            foreach (var s in Util.Find<Storage>())
            {
                if (s.GetComponentInParent<PlayableCharacter>() != null) continue;
                for (int a = 0; a < Util.Slots(s); a++)
                {
                    if (!(s.GetItemAt(a) is Cargo ca) || s.HasTransportOrders(a)) continue;
                    for (int b = a + 1; b < Util.Slots(s); b++)
                    {
                        if (!(s.GetItemAt(b) is Cargo cb) || cb.Data.Name != ca.Data.Name || s.HasTransportOrders(b)) continue;
                        float room = (float)ca.StackLimit - ca.Count;
                        if (room <= 0f) break;
                        float move = Mathf.Min(room, cb.Count);
                        if (move <= 0f) continue;
                        StorageUtility.TransferInstantly(s, b, s, a, move);
                        moves++;
                    }
                }
                for (int a = 0; a < Util.Slots(s); a++)
                {
                    if (s.GetItemAt(a) != null || s.HasTransportOrders(a)) continue;
                    for (int b = a + 1; b < Util.Slots(s); b++)
                    {
                        var item = s.GetItemAt(b);
                        if (item == null || s.HasTransportOrders(b) || !s.IsItemAllowed(a, item)) continue;
                        float count = item is Cargo c ? c.Count : 1f;
                        StorageUtility.TransferInstantly(s, b, s, a, count);
                        moves++;
                        break;
                    }
                }
            }
            ToolkitRunner.ShowNotice("Lager aufgeräumt (" + moves + " Schritte)", 4f);
        }

        // Every research task of the headquarters that is not running is finished at once (the game's own Finish = item researched).
        private static void ResearchAll(PlayerShip ship)
        {
            var hq = ship.SandboxEntity?.Country?.Headquarters;
            if (hq == null) return;
            int n = 0;
            foreach (var t in hq.Tasks.ToArray())
                if (t is UBOAT.Game.Sandbox.HQ.Research r && !r.IsCompleted && (r.AssignedAgents == null || r.AssignedAgents.Length == 0))
                { r.Finish(); n++; }
            var db = Resources.FindObjectsOfTypeAll<EquipmentDatabase>().FirstOrDefault();
            if (db != null) foreach (var e in db.GetEquipmentDirect()) if (e != null) e.Researched = true;
            ToolkitRunner.ShowNotice("Erforscht: " + n + " Forschungen (laufende bleiben, gehen mit „sofort“ gleich fertig)", 6f);
        }

        private static void UnlockAll()
        {
            int items = 0;
            var db = Resources.FindObjectsOfTypeAll<EquipmentDatabase>().FirstOrDefault();
            if (db != null)
                foreach (var e in db.GetEquipmentDirect())
                    if (e != null && !e.Researched) { e.Researched = true; items++; }
            var career = Util.Career;
            if (career != null)
                foreach (var b in new[] { "Type IIA", "Type IID", "Type VIIB", "Type VIIC", "Type VIIC41", "Type IXA", "Type IXC", "Type IXC40" })
                    career.UnlockShip(b, true);
            ToolkitRunner.ShowNotice("Freigeschaltet: " + items + " Ausrüstungen + alle Boote", 6f);
        }

        // Jumps to the end of the planned route on the map.
        private static void QuickTravel()
        {
            var w = Game.Wolfpack;
            if (w?.Route == null || w.Route.Count == 0) { ToolkitRunner.ShowNotice("Erst einen Kurs auf der Karte setzen", 4f); return; }
            var target = w.Route[w.Route.Count - 1];
            var ship = Util.PlayerShip;
            if (ship.Docked) ship.Undock();
            w.SetPosition(target, true, true);
            w.Route.Clear();
            ToolkitRunner.ShowNotice("Schnellreise: am Ziel", 4f);
        }

        // Nearest supply U-boat comes to you (3 km away).
        private static void CallMilkCow()
        {
            var hq = Util.PlayerShip.SandboxEntity?.Country?.Headquarters;
            var w = Game.Wolfpack;
            if (hq == null || w == null) return;
            UBOAT.Game.Sandbox.SandboxGroup best = null; float bestDist = float.MaxValue;
            foreach (var t in hq.Tasks.OfType<UBOAT.Game.Sandbox.HQ.AddSupplyShip>())
            {
                var g = t.SupplyShip?.Group;
                if (g == null || t.SupplyShip.IsPacified) continue;
                float d = (g.Position - w.Position).sqrMagnitude;
                if (d < bestDist) { best = g; bestDist = d; }
            }
            if (best == null) { ToolkitRunner.ShowNotice("Keine Milchkuh auf See", 4f); return; }
            best.SetPosition(w.Position + new Vector2(3f, 0f), true);
            ToolkitRunner.ShowNotice("Milchkuh ist da (3 km östlich)", 5f);
        }

        private static void NavInfo(PlayerShip ship)
        {
            string speed = Events.SpeedText;
            if (!Settings.B("NavInfo")) { ToolkitRunner.InfoLine = speed; return; }
            var w = Game.Wolfpack;
            string text = "";
            if (w?.Route != null && w.Route.Count > 0)
            {
                float km = w.Route.TotalLength;
                text = "Ziel: " + km.ToString("0") + " km";
                if (w.Velocity > 0.0001f)
                {
                    float h = km / w.Velocity / 3600f;
                    text += ", Ankunft in " + ((int)h) + " h " + ((int)((h - (int)h) * 60f)).ToString("00") + " min (Spielzeit)";
                }
                text += "  |  ";
            }
            try { text += "Diesel-Reichweite (3. Stufe): " + ship.EstimatedRangeOnThirdGear.ToString("0") + " km"; } catch { }
            ToolkitRunner.InfoLine = speed != null ? speed + "  |  " + text : text;
        }
    }

    [HarmonyPatch(typeof(SandboxDock), nameof(SandboxDock.GetBuyPrice))]
    internal static class Extras_PricePatch
    {
        private static void Postfix(ref int __result)
        {
            float f = Settings.F("CheatPriceFactor");
            if (f <= 0f || f >= 1f || __result <= 0) return;
            __result = Mathf.Max(1, Mathf.CeilToInt(__result * f));
        }
    }

    [HarmonyPatch(typeof(RepairAction), "StartMainCharacter")]
    internal static class Extras_RepairPatch
    {
        private static void Postfix(RepairAction __instance)
        {
            float f = Settings.F("CheatRepairSpeed");
            if (f <= 1f || !Util.IsPlayers(__instance.Activator)) return;
            var t = Traverse.Create(__instance).Field("performanceMultiplier");
            t.SetValue(t.GetValue<float>() * f);
        }
    }

    // Fast torpedoes: safer hit detection so they do not pass through a ship between two physics steps.
    [HarmonyPatch(typeof(Torpedo), "Start")]
    internal static class Extras_TorpedoHitPatch
    {
        private static void Postfix(Torpedo __instance)
        {
            if (Settings.F("CheatTorpedoSpeed") <= 1f || __instance.Owner != Util.PlayerShip) return;
            var rb = __instance.GetComponent<Rigidbody>();
            if (rb != null) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}

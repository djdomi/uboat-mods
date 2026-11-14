using System;
using HarmonyLib;
using UBOAT.Game.Scene.Items;
using UnityEngine;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Emergency key: empty all ballast tanks, empty trim tanks too, log the tank state before/after.
    public class TankReset : ModuleBase
    {
        public override void Frame()
        {
            if (!Keys.Down("KeyTanksEmpty")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            Util.Log("Not-Taste Tanks: vorher " + State(ship));
            if (ship.BallastTanks != null)
                foreach (var t in ship.BallastTanks) if (t != null) t.Water = 0.0;
            foreach (var p in Util.Find<TrimPump>())
            {
                if (!Util.IsPlayers(p)) continue;
                var tanks = Traverse.Create(p).Field("tanks").GetValue<TrimPump.ConnectedTank[]>();
                if (tanks == null) continue;
                foreach (var ct in tanks)
                {
                    if (ct?.Tank == null) continue;
                    
                    
                    ct.Tank.Water = 0.0;
                }
            }
            Util.Log("Not-Taste Tanks: nachher " + State(ship));
            UboatToolkit.ToolkitRunner.ShowNotice("Alle Tauchzellen leer, Trimmtanks leer (Notauftauchen)", 5f);
        }

        // 0.3.41 (DoMe): tank levels in the info lines (in % of capacity)
        public static string Line;
        private float lineAt;
        public override void Tick()
        {
            if (Time.unscaledTime < lineAt) return;
            lineAt = Time.unscaledTime + 1f;
            var ship = Util.PlayerShip;
            if (ship == null || !Settings.B("TankDisplay")) { Line = null; return; }
            var b = new System.Collections.Generic.List<string>();
            if (ship.BallastTanks != null)
                foreach (var t in ship.BallastTanks) if (t != null && t.Capacity > 0) b.Add((t.Water / t.Capacity * 100.0).ToString("0") + "%");
            var tr = new System.Collections.Generic.List<string>();
            foreach (var p in Util.Find<TrimPump>())
            {
                if (!Util.IsPlayers(p)) continue;
                var tanks = Traverse.Create(p).Field("tanks").GetValue<TrimPump.ConnectedTank[]>();
                if (tanks == null) continue;
                foreach (var ct in tanks) if (ct?.Tank != null && ct.Tank.Capacity > 0) tr.Add((ct.Tank.Water / ct.Tank.Capacity * 100.0).ToString("0") + "%");
            }
            Line = Events.Join(b.Count > 0 ? "Tauchzellen " + string.Join(" ", b) : null, tr.Count > 0 ? "Trimm " + string.Join(" ", tr) : null);
        }

        private static string State(PlayerShip ship)
        {
            string s = "Tauchzellen:";
            if (ship.BallastTanks != null)
                foreach (var t in ship.BallastTanks) if (t != null) s += " " + t.Water.ToString("0") + "/" + t.Capacity.ToString("0");
            return s;
        }
    }
}

using System;
using HarmonyLib;
using UBOAT.Game.Scene.Items;
using UnityEngine;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Emergency key: empty all ballast tanks, set trim tanks to half, log the tank state before/after.
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
                    var cap = Traverse.Create(ct.Tank).Property("Capacity").GetValue();
                    double c = cap == null ? ct.Tank.Water : Convert.ToDouble(cap);
                    ct.Tank.Water = c * 0.5;
                }
            }
            Util.Log("Not-Taste Tanks: nachher " + State(ship));
            UboatToolkit.ToolkitRunner.ShowNotice("Alle Tauchzellen leer, Trimmtanks halb voll", 5f);
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

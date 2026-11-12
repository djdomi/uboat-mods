using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.HQ;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #25: supply U-boats (Type XIV "milk cows") are at sea from the start of the campaign,
    // without first ordering them at the flotilla commander in port.
    // The game defines them as headquarters tasks "Spawn Milk Cow", "Spawn Milk Cow II", ...
    // Fix 2026-11-12 ("Nachschubanforderung läuft ins Leere"): the game's radio resupply only counts
    // supply-ship tasks marked completed. We only spawned the boat, so the flag stayed false. Now the flag
    // is set like the game's own start script does, sunk/lost boats are spawned again, and on a radio
    // request the nearest milk cow is brought into reach first.
    public class MilkCows : ModuleBase
    {
        private static readonly string[] Names =
        {
            "Spawn Milk Cow", "Spawn Milk Cow II", "Spawn Milk Cow III", "Spawn Milk Cow IV", "Spawn Milk Cow V",
            "Spawn Milk Cow VI", "Spawn Milk Cow VII", "Spawn Milk Cow VIII", "Spawn Milk Cow IX", "Spawn Milk Cow X",
        };

        private static readonly FieldInfo Completed = AccessTools.Field(typeof(HeadquartersTaskBase), "completed");
        private float next;

        public static bool Alive(AddSupplyShip t) => t?.SupplyShip != null && !t.SupplyShip.IsPacified && t.SupplyShip.Group != null;

        public static IEnumerable<AddSupplyShip> Tasks(Headquarters hq)
        {
            foreach (var name in Names)
                if (hq.GetTask(name) is AddSupplyShip task) yield return task;
        }

        public static void MarkDone(AddSupplyShip t)
        {
            if (t != null && !t.IsCompleted) Completed?.SetValue(t, true);
        }

        public override void Tick()
        {
            if (!Settings.B("MilkCowsAtStart") || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 10f;
            var hq = Util.PlayerShip?.SandboxEntity?.Country?.Headquarters;
            if (hq == null) return;
            foreach (var task in Tasks(hq))
            {
                if (!Alive(task))
                {
                    task.Start();
                    Util.Log("[MilkCows] Versorger gestartet: " + task.Name + (Alive(task) ? "" : " (fehlgeschlagen)"));
                }
                MarkDone(task);
            }
        }

        // Before the game answers a radio resupply request: make sure the nearest milk cow is in reach.
        public static void BringNearest()
        {
            var ps = Util.PlayerShip?.SandboxEntity;
            var hq = ps?.Country?.Headquarters;
            if (hq == null) return;
            AddSupplyShip best = null; float bestD = float.MaxValue;
            foreach (var t in Tasks(hq))
            {
                if (!Alive(t)) { t.Start(); }
                if (!Alive(t)) continue;
                MarkDone(t);
                float d = (t.SupplyShip.Position - ps.Position).sqrMagnitude;
                if (d < bestD) { best = t; bestD = d; }
            }
            if (best == null) { Util.Log("[MilkCows] Funk-Nachschub: keine Milchkuh verfügbar"); return; }
            float km = Settings.F("MilkCowRadioKm");
            if (bestD <= km * km) return;
            best.SupplyShip.Group.SetPosition(ps.Position + new Vector2(km * 0.7071f, km * 0.7071f), true);
            Util.Log("[MilkCows] Funk-Nachschub: " + best.Name + " auf " + km + " km herangeholt (war " + Mathf.Sqrt(bestD).ToString("0") + " km)");
        }
    }

    [HarmonyPatch(typeof(Headquarters), "RespondToResupplyRequest")]
    internal static class MilkCows_RadioRequest
    {
        private static void Prefix()
        {
            if (Settings.B("MilkCowRadio")) Util.Safe("MilkCowRadio", MilkCows.BringNearest);
        }
    }
}

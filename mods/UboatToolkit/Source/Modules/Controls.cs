using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Missions;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;
using UBOAT.Game.Scene.Environment.Terrain;
using UBOAT.Game.UI;

namespace UboatToolkit.Modules
{
    // Standing orders: crash dive on enemy aircraft, "leave port" button right after undocking.
    public class Controls : ModuleBase
    {
        private PlayerShip hooked;
        private bool wasAlarmed, armed = true;
        private SandboxEntity lastDock, leaveArea;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            if (ship != hooked)
            {
                if (hooked != null) hooked.ObservationAdded -= OnSeen;
                ship.ObservationAdded += OnSeen;
                hooked = ship;
                lastDock = leaveArea = null;
            }
            if (!ship.Alarmed) armed = true;
            wasAlarmed = ship.Alarmed;
            if (Settings.B("LeavePortButton")) Util.Safe("Controls.LeavePort", () => LeavePort(ship));
        }

        private void OnSeen(DirectObservationAddedEvent e)
        {
            if (!Settings.B("AutoCrashDive")) return;
            Util.Safe("Controls.CrashDive", () =>
            {
                var ship = hooked;
                var plane = e.Observation?.Entity as Aircraft;
                if (ship == null || plane == null || plane.FoldedUp || plane.Country == null) return;
                if (plane.Country.GetRelationWith(ship.Country) != Country.Relation.Enemy) return;
                if (ship.Docked || ship.SubmergedOrGoingToSubmerge || wasAlarmed || !armed) return;
                armed = false;
                ship.StartAlarm("Crash Diving");
                ship.FollowDiveSchedule = false;
                ship.SetDepthPreset(DepthPreset.MaxSafeDepth, false);
                ship.OrderEngineGear(5, false);
                Util.Log("Alarmtauchen: feindliches Flugzeug gesichtet");
            });
        }

        private void LeavePort(PlayerShip ship)
        {
            if (ship.CurrentDock != null && ship.CurrentDock != lastDock)
            {
                lastDock = ship.CurrentDock;
                leaveArea = FindLeaveArea(lastDock);
            }
            if (lastDock == null || leaveArea == null || ship.Alarmed || ship.SandboxEntity == null) return;
            bool near = (ship.SandboxEntity.Position - lastDock.Position).sqrMagnitude < 25f;
            if (near && ship.CurrentQuickTravelTarget == null)
                ship.SetQuickTravelTarget(leaveArea, QuickTravelTargetType.LeavePortArea);
            else if (!near && ship.CurrentQuickTravelTarget == leaveArea)
            {
                ship.ClearQuickTravelTarget();
                lastDock = null;
            }
        }

        private static SandboxEntity FindLeaveArea(SandboxEntity port)
        {
            var all = port.Group?.Entities;
            if (all == null) return null;
            var hit = all.FirstOrDefault(x => x.Name != null && x.Name.ToLower().Contains("leave port"));
            return hit;   // only the game's own "leave port" point; the far "undock" points could lie behind land
        }
    }

    // Tonnage sunk on the way already counts for the patrol assignment.
    [HarmonyPatch(typeof(PatrolAssignment), "PlayerCareerOnTonnageSunkChanged")]
    internal static class Controls_TonnagePatch
    {
        private static void Prefix(ref int ___tonnageSunkBeforeArrival) { if (Settings.B("TonnageBeforePatrol")) ___tonnageSunkBeforeArrival = 0; }
    }
    [HarmonyPatch]
    internal static class Controls_TonnageStagePatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PatrolAssignment), "OnStageStarted");
            yield return AccessTools.Method(typeof(PatrolAssignment), "OnStageResumed");
        }
        private static void Postfix(PatrolAssignment __instance)
        {
            if (Settings.B("TonnageBeforePatrol"))
                Util.Safe("Controls.Tonnage", () => AccessTools.Method(typeof(PatrolAssignment), "PlayerCareerOnTonnageSunkChanged").Invoke(__instance, null));
        }
    }

    // Map: shows the real water depth under the mouse next to the rough range.
    [HarmonyPatch(typeof(MapUI), "LateUpdate")]
    internal static class Controls_MapDepthPatch
    {
        private static void Postfix(Component ___depthLabel, GameInput ___gameInput, TerrainController ___terrainController, Weather ___weather, UBOAT.Game.Sandbox.Sandbox ___sandbox)
        {
            if (!Settings.B("MapActualDepth") || ___depthLabel == null || ___gameInput == null || ___terrainController == null || ___weather == null) return;
            var grid = ___terrainController.MainGrid;
            var pos = ___gameInput.HoveredScenePosition;
            float h = grid != null ? grid.GetInterpolatedHeight(pos.x, pos.z, float.NaN) : float.NaN;
            string word = "genau";
            if (float.IsNaN(h) && ___sandbox != null)
            {
                // far away: world elevation map (good to a few metres)
                h = WorldMaps.ElevationMap.GetAtWorldSpace(___sandbox.SceneToEncounterSpace(pos));
                word = "ca.";
            }
            if (float.IsNaN(h)) return;
            int depth = Mathf.RoundToInt(___weather.Sea.VerticalOffset - h);
            if (depth <= 0) return;
            var prop = Traverse.Create(___depthLabel).Property("text");
            string t = prop.GetValue<string>() ?? "";
            int i = t.IndexOf("\n(");
            if (i >= 0) t = t.Substring(0, i);
            prop.SetValue(t + "\n(" + word + ": " + depth + " m)");
        }
    }

    // Ports stock an even mix of all torpedo types the country has, instead of random luck.
    [HarmonyPatch(typeof(CargoSpawner), "Spawn", new[] { typeof(ISandboxEntity), typeof(EntityBlueprintSlot), typeof(HashSet<string>) })]
    internal static class Controls_TorpedoMixPatch
    {
        private static readonly Dictionary<ISandboxEntity, int> turn = new Dictionary<ISandboxEntity, int>();

        private static bool Prefix(ISandboxEntity entity, EntityBlueprintSlot slot, ref SandboxEquipment __result, WorldState ___worldState)
        {
            TorpedoTab.Load();
            bool pick = TorpedoTab.PortPick.Count > 0;
            if ((!Settings.B("TorpedoMix") && !pick) || slot == null || slot.EquipmentTag != "Torpedo") return true;
            var se = entity as SandboxEntity;
            if (se == null || se.Blueprint.Type.Category != EntityTypeCategory.Port || se.Country == null) return true;
            var pool = se.Country.GetReservesByTagID(slot.EquipmentTagID);
            if (pool == null) return true;
            var list = pool.Where(x => x?.ItemData != null).OrderBy(x => x.ItemData.Name).ToList();
            if (pick)
            {
                var chosen = list.Where(x => TorpedoTab.PortPick.Contains(x.ItemData.Name)).ToList();
                if (chosen.Count > 0) list = chosen;
                else if (!Settings.B("TorpedoMix")) return true;
            }
            if (list.Count == 0) return true;
            turn.TryGetValue(entity, out int n);
            turn[entity] = n + 1;
            var data = list[n % list.Count].ItemData;
            se.Country.ConsumeItemFromReserves(data);
            float price = entity is SandboxDock dock && ___worldState != null ? dock.GetBuyPriceMultiplier(data) / ___worldState.BuyPriceMultiplier : 1f;
            var eq = (SandboxEquipment)Activator.CreateInstance(data.Type, se, slot, data);
            eq.Count = Mathf.Max(1f, Mathf.Round(UnityEngine.Random.Range(0.3f, 1f) * slot.Capacity / data.Mass / price));
            eq.Initialize();
            __result = eq;
            return false;
        }
    }
}

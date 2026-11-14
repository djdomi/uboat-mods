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

        // Engine telegraph on keys (Tasten tab).
        private static readonly string[] gearKeys = { "KeyGearStop", "KeyGear1", "KeyGear2", "KeyGear3", "KeyGear4", "KeyGear5", "KeyGearBack1", "KeyGearBack2", "KeyGearBack3", "KeyGearBack4" };
        private static readonly int[] gears = { 0, 1, 2, 3, 4, 5, -2, -3, -4, -5 };
        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < gearKeys.Length; i++)
            {
                if (!Keys.Down(gearKeys[i])) continue;
                if (InputBlocked()) return;                                  // typing in a text field, menu open ...
                if (Settings.B("TelegraphBlockScopes") && AtScopeOrFreeCam()) return;   // no engine order by accident
                int g = gears[i];
                Util.Safe("Controls.Gear", () => ship.OrderEngineGear(g, false));
                if (Settings.B("TelegraphLog")) Util.Log("Maschinen-Telegraf: " + gearKeys[i]);
                return;
            }
        }

        private static readonly System.Reflection.FieldInfo GameInputF = AccessTools.Field(typeof(UBOAT.Game.Scene.Camera.MainCamera), "gameInput");
        private static UBOAT.Game.Core.Input.GameInput gameInput;
        private static bool InputBlocked()
        {
            if (gameInput == null && GameInputF != null) gameInput = GameInputF.GetValue(null) as UBOAT.Game.Core.Input.GameInput;
            return gameInput != null && gameInput.HasActiveBlockades();
        }

        private static bool AtScopeOrFreeCam()
        {
            var cam = UBOAT.Game.Scene.Camera.MainCamera.Instance;
            if (cam == null) return false;
            if (cam.CurrentMode == UBOAT.Game.Scene.CameraMode.FreeFly) return true;
            return cam.GenericController != null && cam.GenericController.ExternalController is UBOAT.Game.Scene.Camera.ExternalControllers.PeriscopeController;
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
            yield return AccessTools.DeclaredMethod(typeof(PatrolAssignment), "OnStageStarted", new[] { typeof(PatrolAssignment.Stages), typeof(bool) });
            yield return AccessTools.DeclaredMethod(typeof(PatrolAssignment), "OnStageResumed", new[] { typeof(PatrolAssignment.Stages) });
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

        internal static bool IsMine(string name) => name != null && (name.IndexOf("Mine", StringComparison.OrdinalIgnoreCase) >= 0 || name.StartsWith("TM"));

        private static readonly Dictionary<ISandboxEntity, EntityBlueprintSlot> mineSlots = new Dictionary<ISandboxEntity, EntityBlueprintSlot>();
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
                var chosen = list.Where(x => TorpedoTab.PortPick.Contains(x.ItemData.Name) || IsMine(x.ItemData.Name)).ToList();
                if (chosen.Count > 0) list = chosen;
                else if (!Settings.B("TorpedoMix")) return true;
            }
            if (list.Count == 0) return true;
            turn.TryGetValue(entity, out int n);
            turn[entity] = n + 1;
            // Mines share the "Torpedo" slot: the first slot of every port is always a mine (if the country has any),
            // so mine-laying missions stay possible.
            var mines = list.Where(x => IsMine(x.ItemData.Name)).ToList();
            // 0.3.45: the same slot gets a mine on EVERY restock (before: only the first spawn per session)
            bool mineSlot = false;
            if (mines.Count > 0)
            {
                if (!mineSlots.TryGetValue(entity, out var ms)) { mineSlots[entity] = slot; mineSlot = true; }
                else mineSlot = ReferenceEquals(ms, slot);
            }
            var data = (mineSlot ? mines[n % mines.Count] : list[n % list.Count]).ItemData;
            if (mineSlot) Util.Log("Hafen-Laden: Mine im ersten Torpedo-Platz (" + data.Name + ")");
            se.Country.ConsumeItemFromReserves(data);
            float price = entity is SandboxDock dock && ___worldState != null ? dock.GetBuyPriceMultiplier(data) / ___worldState.BuyPriceMultiplier : 1f;
            var eq = (SandboxEquipment)Activator.CreateInstance(data.Type, se, slot, data);
            eq.Count = Mathf.Max(1f, Mathf.Round(UnityEngine.Random.Range(0.3f, 1f) * slot.Capacity / data.Mass / price));
            eq.Initialize();
            __result = eq;
            return false;
        }
    }

    // No scrolling of the section view when the mouse touches the screen edge.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.SectionViewCameraController), "ParseMouse")]
    internal static class Controls_EdgeScrollPatch
    {
        private static void Prefix(ref bool ___wasOnCentralPartOfScreen) { if (Settings.B("NoEdgeScroll")) ___wasOnCentralPartOfScreen = false; }
    }
    // No jump to deck view when the mouse touches the top/bottom edge.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.SectionViewCameraController), "Update")]
    internal static class Controls_EdgeDeckPatch
    {
        public static float Top() => Settings.B("NoEdgeDeck") ? 2f : 0.98f;
        public static float Bottom() => Settings.B("NoEdgeDeck") ? -1f : 0.02f;
        private static IEnumerable<HarmonyLib.CodeInstruction> Transpiler(IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var list = code.ToList();
            var top = list.Where(c => c.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && c.operand is float f && f == 0.98f).ToList();
            var bottom = list.Where(c => c.opcode == System.Reflection.Emit.OpCodes.Ldc_R4 && c.operand is float f && f == 0.02f).ToList();
            if (top.Count != 1 || bottom.Count != 1) return list;   // game changed: leave untouched
            top[0].opcode = System.Reflection.Emit.OpCodes.Call; top[0].operand = AccessTools.Method(typeof(Controls_EdgeDeckPatch), "Top");
            bottom[0].opcode = System.Reflection.Emit.OpCodes.Call; bottom[0].operand = AccessTools.Method(typeof(Controls_EdgeDeckPatch), "Bottom");
            return list;
        }
    }

}

namespace UboatToolkit.Modules
{
    // Game bug: OrderEngineGear throws IndexOutOfRange for a gear the boat doesn't have (UI telegraph). The order is
    // simply dropped instead of an exception popping the dev console; the gear number is logged once to find the source.
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.OrderEngineGear))]
    internal static class Fix_OrderEngineGearRange
    {
        private static readonly HashSet<int> logged = new HashSet<int>();
        private static System.Exception Finalizer(System.Exception __exception, int gearIndex)
        {
            if (!(__exception is System.IndexOutOfRangeException)) return __exception;
            if (logged.Add(gearIndex)) Util.Log("Fahrstufe " + gearIndex + " gibt es auf diesem Boot nicht, Befehl ignoriert");
            return null;
        }
    }
}

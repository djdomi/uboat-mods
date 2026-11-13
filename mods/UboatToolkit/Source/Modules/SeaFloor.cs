using System.Linq;
using HarmonyLib;
using UnityEngine;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment.Terrain;

namespace UboatToolkit.Modules
{
    // Always load the sea floor in open sea when water is shallower than ~300 m
    // (vanilla only loads it inside encounter areas). Own code.
    internal static class SeaFloor
    {
        [Inject] internal static UserSettings userSettings;
        [Inject] internal static GameTime gameTime;
        [Inject] internal static SandboxPlayerWolfpack wolfpack;

        internal static bool Forced;
        private static Vector2 lastPos = new Vector2(float.NaN, 0f);
        private static SandboxSceneOrigin origin;
        private static float nextOriginSearch;
        private static readonly Vector2[] probe =
        {
            Vector2.zero, new Vector2(8f, 0f), new Vector2(-8f, 0f), new Vector2(0f, 8f), new Vector2(0f, -8f),
            new Vector2(5.66f, 5.66f), new Vector2(5.66f, -5.66f), new Vector2(-5.66f, 5.66f), new Vector2(-5.66f, -5.66f)
        };

        internal static bool Active => Forced && wolfpack != null && !wolfpack.IsInArea;

        internal static void Update(TerrainController tc, WorldGrid[] grids)
        {
            if (!Settings.B("SeaFloorAlways") || wolfpack == null || wolfpack.IsInArea || grids == null || grids.Length == 0) { Forced = false; return; }
            if (userSettings?.GameplaySettings != null && userSettings.GameplaySettings.travelSystem == TravelSystem.Dynamic) { Forced = false; return; }
            if (gameTime != null && (float)gameTime.TimeScale * gameTime.ArtificialTimeScale > Settings.F("SeaFloorMaxCompression")) { Forced = false; lastPos.x = float.NaN; return; }

            var pos = wolfpack.Position;
            if (float.IsNaN(lastPos.x) || (pos - lastPos).sqrMagnitude > 0.01f)
            {
                lastPos = pos;
                float top = probe.Max(o => WorldMaps.ElevationMap.GetAtWorldSpace(pos + o));
                if (top > -300f) Forced = true;
                else if (top < -350f) Forced = false;
            }
            if (!Forced) return;

            var grid = grids[0];
            if (origin == null && Time.unscaledTime > nextOriginSearch)
            {
                nextOriginSearch = Time.unscaledTime + 2f;
                origin = Object.FindObjectOfType<SandboxSceneOrigin>();
            }
            if (origin != null) grid.SetOffset(origin.Position.x * 1000f, origin.Position.y * 1000f, false);
            grid.gameObject.SetActive(true);
            grid.GridEnabled = true;
        }

        // Real loaded seabed under the boat, NaN when not known.
        // Water depth from the game's world elevation map (good to a few metres) when the tile under the boat has no
        // built terrain (deeper than ~650 m). Same source as the map's depth under the mouse.
        internal static float MapDepthUnderShip()
        {
            try
            {
                var ship = Util.PlayerShip;
                var sb = Util.Find<UBOAT.Game.Sandbox.Sandbox>(30f).FirstOrDefault();
                if (ship == null || sb == null) return float.NaN;
                float h = WorldMaps.ElevationMap.GetAtWorldSpace(sb.SceneToEncounterSpace(ship.transform.position));
                return float.IsNaN(h) || h >= 0f ? float.NaN : -h;
            }
            catch { return float.NaN; }
        }

        internal static float SeabedDepthUnderShip()
        {
            var ship = Util.PlayerShip;
            var tc = Util.Find<TerrainController>(30f).FirstOrDefault();
            if (ship == null || tc == null || tc.MainGrid == null) return float.NaN;
            var p = ship.transform.position;
            float h = tc.MainGrid.GetInterpolatedHeight(p.x, p.z, float.NaN);
            return float.IsNaN(h) ? float.NaN : -h;
        }
    }

    [HarmonyPatch(typeof(TerrainController), "LateUpdate")]
    internal static class SeaFloor_TerrainPatch
    {
        private static void Postfix(TerrainController __instance, WorldGrid[] ___terrains)
            => Util.Safe("SeaFloor", () => SeaFloor.Update(__instance, ___terrains));
    }

    // Vanilla starts an "encounter" (no high time compression) when the boat is near the coarse map bottom.
    // With the real floor loaded this is only kept when the real floor is actually close.
    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "DetermineEncounterMode")]
    internal static class SeaFloor_EncounterPatch
    {
        internal static bool Inside;
        private static void Prefix()
        {
            Inside = false;
            if (!SeaFloor.Active) return;
            var ship = Util.PlayerShip;
            float bed = SeaFloor.SeabedDepthUnderShip();
            if (ship == null || float.IsNaN(bed)) return;
            Inside = ship.CurrentColliderBottomDepth < bed - 15f && ship.TargetDepth < bed - 15f;
        }
        private static void Finalizer() => Inside = false;
    }

    [HarmonyPatch(typeof(PlayerShip), "CurrentColliderBottomDepth", MethodType.Getter)]
    internal static class SeaFloor_BottomPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!SeaFloor_EncounterPatch.Inside) return true;
            __result = 0f;
            return false;
        }
    }
}

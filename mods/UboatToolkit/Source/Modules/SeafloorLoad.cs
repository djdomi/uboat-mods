using HarmonyLib;
using UBOAT.Game.Scene.Environment.Terrain;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL (DoMe 2026-11-13): the sea floor around the boat is always really built. The game builds real terrain only for
    // 1 km tiles whose highest point is above -650 m; deeper tiles get a flat "sea floor" stand-in only, and depth queries
    // there (GetInterpolatedHeight) return -2000. After the game's own scan we build every not-yet-built tile within
    // the chosen radius. Sonar picture / depth gauge read the same terrain, so they see the real floor too.
    [HarmonyPatch(typeof(ProceduralTerrainGrid), "ScanNearbySegments")]
    internal static class SeafloorLoad_ScanPatch
    {
        private static float nextScan;
        private static int built;
        private static bool logged;

        private static void Postfix(ProceduralTerrainGrid __instance)
        {
            try
            {
                if (!Settings.B("SeafloorAlways") || Time.unscaledTime < nextScan) return;
                nextScan = Time.unscaledTime + 2f;
                var ship = Util.PlayerShip;
                if (ship == null) return;
                float size = Mathf.Max(100f, __instance.SegmentSize);
                float radius = Mathf.Clamp(Settings.F("SeafloorRadius"), 0.5f, 4f) * 1000f;
                Vector3 p = ship.transform.position;
                __instance.GetSegmentAtSceneCoordinates(p.x, p.z, out int cx, out int cz);
                int n = Mathf.CeilToInt(radius / size);
                for (int dz = -n; dz <= n; dz++)
                    for (int dx = -n; dx <= n; dx++)
                    {
                        // distance from the boat to the nearest point of this tile
                        float ox = Mathf.Max(0f, Mathf.Abs(dx) - 0.5f) * size, oz = Mathf.Max(0f, Mathf.Abs(dz) - 0.5f) * size;
                        if (ox * ox + oz * oz > radius * radius) continue;
                        var seg = __instance.GetSegment(cx + dx, cz + dz);
                        if (seg == null || seg.Loaded || seg.Loading) continue;
                        seg.Generate(1);
                        seg.Active = true;
                        built++;
                    }
                if (built > 0 && !logged) { logged = true; Util.Log("Meeresboden: tiefe Kacheln werden voll gebaut (Radius " + (radius / 1000f).ToString("0.0") + " km)"); }
            }
            catch (System.Exception e) { if (!logged) { logged = true; Util.Log("Meeresboden: " + e.Message); } }
        }
    }
}

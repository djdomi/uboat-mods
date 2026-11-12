using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Net saw: the serrated stem at bow and stern (WH_NetSaw_Bow / WH_NetSaw_Stern in the hull) cuts anti-submarine nets.
    //  - nets near the boat: their colliders ignore ours in advance, so the boat is never stopped or slowed by them
    //  - as soon as a saw end reaches a net segment (the game builds nets from 110 m pieces) the piece is cut out
    //    (deactivated) -> a real gap that stays open while the net is loaded; after loading a save the game rebuilds
    //    the net and the saw cuts it again on contact
    //  - the game's "Anti-submarine Net Collision" warning is suppressed while the saw is on
    public class WarthogNetCutter : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhNetSaw");
        private static readonly HashSet<int> ignored = new HashSet<int>();
        private float scanAt;
        private AntiSubmarineNetPart[] parts = new AntiSubmarineNetPart[0];

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !On) return;
            if (Time.unscaledTime - scanAt > 3f) { parts = Object.FindObjectsOfType<AntiSubmarineNetPart>(); scanAt = Time.unscaledTime; }
            if (parts.Length == 0) return;
            var shipCols = ship.GetComponentsInChildren<Collider>(true);
            foreach (var p in parts)
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                var cols = p.GetComponentsInChildren<Collider>(true);
                if (cols.Length == 0) continue;
                var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
                float dist = Mathf.Sqrt(b.SqrDistance(ship.transform.position));
                if (dist > 250f) continue;
                if (ignored.Add(p.GetInstanceID()))
                {
                    foreach (var c in cols) foreach (var s in shipCols) if (c && s) Physics.IgnoreCollision(c, s, true);
                    Util.Log("[Warthog] net: " + p.name + " ahead (" + dist.ToString("0") + " m) - collisions off, saw ready");
                }
                if (SawTouches(ship, b)) Cut(p, "saw contact");
            }
        }

        public override void Frame()
        {
            // between ticks: fast boats must not slip through a whole 110 m piece without cutting it
            var ship = Util.PlayerShip;
            if (ship == null || !On || parts.Length == 0 || Time.frameCount % 5 != 0) return;
            foreach (var p in parts)
            {
                if (p == null || !p.gameObject.activeInHierarchy || !ignored.Contains(p.GetInstanceID())) continue;
                var cols = p.GetComponentsInChildren<Collider>(false);
                if (cols.Length == 0) continue;
                var b = cols[0].bounds; foreach (var c in cols) b.Encapsulate(c.bounds);
                if (SawTouches(ship, b)) Cut(p, "saw contact");
            }
        }

        private static bool SawTouches(PlayerShip ship, Bounds net)
        {
            var big = net; big.Expand(4f);
            foreach (var end in new[] { 46f, -46f })
            {
                Vector3 tip = ship.transform.TransformPoint(new Vector3(0f, 0f, end));
                Vector3 mid = ship.transform.TransformPoint(new Vector3(0f, 0f, end * 0.9f));
                if (big.Contains(tip) || big.Contains(mid)) return true;
            }
            return false;
        }

        internal static void Cut(AntiSubmarineNetPart p, string why)
        {
            if (p == null || !p.gameObject.activeSelf) return;
            p.gameObject.SetActive(false);
            Util.Log("[Warthog] net: cut " + p.name + " at " + p.transform.position.ToString("F0") + " (" + why + ")");
            ToolkitRunner.ShowNotice("Netzsäge: Netz durchtrennt", 4f);
        }
    }

    // contact before our pre-scan noticed the net: cut it, no warning, no stop
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.OnCollidedWithAntiSubmarineNet))]
    internal static class WarthogNetCutter_CollisionPatch
    {
        private static bool Prefix(PlayerShip __instance, AntiSubmarineNetPart antiSubmarineNetPart)
        {
            if (!WarthogNetCutter.On || !WarthogSettings.IsWarthog(__instance)) return true;
            WarthogNetCutter.Cut(antiSubmarineNetPart, "collision");
            return false;
        }
    }
}

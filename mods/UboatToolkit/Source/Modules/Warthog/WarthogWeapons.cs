using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Weapons of U-1337 (runtime part; the turrets themselves are built into the boat):
    //  - flak can aim down to -WhFlakDepression degrees (game: maxAngle = degrees DOWN, minAngle = degrees UP)
    //  - own-boat fire guard: a gun never fires while its line of fire goes through our own boat
    //  - aircraft: reported from WhAirDetect km, flak range WhAirEngage km
    //  - remembers the last shot, so the thrusters can hold the heading against the recoil
    // Gyro stabilisation per gun + ship gyro is the Toolkit "GunGyro" (GunGyroCrew / GunGyroManual);
    // on U-1337 both are forced on.
    public class WarthogWeapons : ModuleBase
    {
        internal static bool On => WarthogSettings.Active(Util.PlayerShip);
        private readonly HashSet<int> reported = new HashSet<int>();
        private Aircraft[] planes = new Aircraft[0];
        private float planesAt = -99f;

        internal static bool IsFlak(Gun g) => g != null && g.Calibre.Value > 0f && g.Calibre.Value <= 40f;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            bool on = On;
            foreach (var g in Util.Find<Gun>())
            {
                if (!IsFlak(g)) continue;
                float orig = Util.Original<float>(g, "maxAngle");
                Util.SetField(g, "maxAngle", on ? Mathf.Max(orig, Settings.F("WhFlakDepression")) : orig);
                var range = Traverse.Create(g).Field("range").GetValue<UBOAT.Game.Core.Data.Parameter>();
                Util.SetParameter(range, on ? Settings.F("WhAirEngage") * 1000f : 0f);
            }
            foreach (var g in Util.Find<Gun>()) Barrels(g, on);
            if (on) { Settings.Set("GunGyroCrew", 1); Settings.Set("GunGyroManual", 1); }
            if (on) ReportPlanes(ship); else reported.Clear();
        }

        // Our deck guns are built with 4 barrels: twin = the middle two, Overkill = all four.
        // Calibre 88 / 105 for the big guns: calibre value + faster reload for 88.
        private static readonly System.Reflection.FieldInfo BarrelsField = AccessTools.Field(typeof(Gun), "barrels");
        private static readonly System.Reflection.FieldInfo RecoilField = AccessTools.Inner(typeof(Gun), "Barrel") != null ? AccessTools.Field(AccessTools.Inner(typeof(Gun), "Barrel"), "RecoilTarget") : null;
        private static readonly Dictionary<int, System.Array> fullBarrels = new Dictionary<int, System.Array>();

        private static void Barrels(Gun g, bool on)
        {
            if (BarrelsField == null || RecoilField == null) return;
            int id = g.GetInstanceID();
            if (!fullBarrels.TryGetValue(id, out var full))
            {
                full = (System.Array)BarrelsField.GetValue(g);
                if (full == null || full.Length != 4) return;   // not one of our 4-barrel mounts
                fullBarrels[id] = full;
            }
            bool all = !on || Settings.B("WhOverkill") || IsFlak(g);   // flak is always a quad mount
            var want = all ? full : System.Array.CreateInstance(full.GetType().GetElementType(), 2);
            if (!all) { want.SetValue(full.GetValue(1), 0); want.SetValue(full.GetValue(2), 1); }
            if (((System.Array)BarrelsField.GetValue(g)).Length != want.Length) BarrelsField.SetValue(g, want);
            foreach (int i in new[] { 0, 3 })
            {
                var t = RecoilField.GetValue(full.GetValue(i)) as Transform;
                if (t != null && t.gameObject.activeSelf != all) t.gameObject.SetActive(all);
            }
            if (!IsFlak(g) && g.Calibre.Value > 80f)
            {
                bool c88 = on && Settings.B("WhCaliber88");   // exactly 88 mm, else the gun's own 105 mm
                Util.SetParameter(g.Calibre, c88 ? 88f : 0f);
                g.ReloadTime.GetScaleModifier("Warthog Calibre", true).Value = c88 ? 0.75f : 1f;
            }
        }

        private void ReportPlanes(PlayerShip ship)
        {
            if (Time.unscaledTime - planesAt > 5f) { planes = Object.FindObjectsOfType<Aircraft>(); planesAt = Time.unscaledTime; }
            float detect = Settings.F("WhAirDetect") * 1000f;
            var now = new HashSet<int>();
            foreach (var p in planes)
            {
                if (p == null) continue;
                float d = Vector3.Distance(ship.transform.position, p.transform.position);
                if (d > detect) continue;
                int id = p.GetInstanceID(); now.Add(id);
                if (reported.Contains(id)) continue;
                Vector3 to = p.transform.position - ship.transform.position;
                int brg = Mathf.RoundToInt((Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f);
                ToolkitRunner.ShowNotice("Radar: Flugzeug, Peilung " + brg.ToString("000") + "°, " + (d / 1000f).ToString("0") + " km", 6f);
            }
            reported.Clear(); reported.UnionWith(now);
        }
    }

    // remote operation from the fire-control room: our guns stay usable without a man at the gun
    [HarmonyPatch(typeof(Device), nameof(Device.SetDisableFlag))]
    internal static class WarthogWeapons_RemotePatch
    {
        private static void Prefix(Device __instance, DeviceDisableFlag disableFlag, ref bool state)
        {
            if (state && disableFlag == DeviceDisableFlag.Unmanned && __instance is Gun g && WarthogWeapons.On && Settings.B("WhRemoteGuns") && Util.IsPlayers(__instance)
                && WarthogFireControl.Ready(g))   // optional: only when its fire-control station is manned
                state = false;
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class WarthogWeapons_FirePatch
    {
        private static readonly AccessTools.FieldRef<Gun, Transform> Origin = AccessTools.FieldRefAccess<Gun, Transform>("originTransform");
        private static readonly RaycastHit[] hits = new RaycastHit[16];

        // own-boat guard
        private static bool Prefix(Gun __instance, Vector2 ___angles, ref Shell __result)
        {
            if (!WarthogWeapons.On || !Settings.B("WhOwnBoatGuard") || !Util.IsPlayers(__instance)) return true;
            var origin = Origin(__instance);
            var ship = Util.PlayerShip;
            if (origin == null || ship == null) return true;
            Vector3 dir = origin.TransformDirection(Quaternion.Euler(___angles.x, ___angles.y, 0f) * Vector3.forward);
            Vector3 from = origin.position + dir * 2.5f;   // start in front of the gun's own parts
            int n = Physics.RaycastNonAlloc(from, dir, hits, 120f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null || c.transform.IsChildOf(__instance.transform)) continue;
                if (c.transform.IsChildOf(ship.transform)) { __result = null; return false; } // would hit our own boat
            }
            return true;
        }

        private static void Postfix(Gun __instance, Shell __result)
        {
            if (__result != null && Util.IsPlayers(__instance)) WarthogHull.LastGunShot = Time.time;
        }
    }
}

using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Cheats: "Sink the convoy" (every enemy ship nearby gets torpedoes) and
    // "remote torpedo" (fire at the nearest enemy from anywhere, e.g. from your bunk).
    // Torpedoes are spawned close to the target, the same way the game's own developer console does it,
    // and count as fired by your boat.
    public class Strike : ModuleBase
    {
        public override void Frame()
        {
            if (Util.PlayerShip == null) return;
            if (Keys.Down("KeyRemoteTorpedo") && Settings.B("CheatRemoteTorpedo")) Util.Safe("RemoteTorpedo", RemoteTorpedo);
            if (Keys.Down("KeySinkConvoy")) { var o = Settings.Get("CheatSinkConvoy"); if (o != null) o.Value = 1; }
        }

        public override void Tick()
        {
            var o = Settings.Get("CheatSinkConvoy");
            if (o != null && o.On) { o.Value = 0; Util.Safe("SinkConvoy", SinkConvoy); }
        }

        private static bool IsEnemyShip(Entity e, Country me)
        {
            if (!(e is Ship) || e is PlayerShip || e is Torpedo || e.Health <= 0f) return false;
            var se = e.SandboxEntity;
            if (se == null || se.IsPacified || se.Type == null || se.Type.IsAircraft) return false;
            var c = se.PerceivedCountry;
            return c != null && me != null && c.GetRelationWith(me) == Country.Relation.Enemy;
        }

        private static void SinkConvoy()
        {
            var ship = Util.PlayerShip;
            var me = ship.SandboxEntity?.Country;
            float range = Settings.F("SinkConvoyRange") * 1000f;
            int per = Mathf.Max(1, (int)Settings.F("SinkConvoyTorpedoes"));
            int ships = 0;
            foreach (var e in Entity.List.ToArray())
            {
                if (!IsEnemyShip(e, me)) continue;
                if ((e.transform.position - ship.transform.position).magnitude > range) continue;
                for (int i = 0; i < per; i++) Spawn(e, "G7a Torpedo T1 - Pi1.1");
                ships++;
            }
            ToolkitRunner.ShowNotice(ships > 0 ? "Sink the convoy: " + ships + " Schiffe im Visier" : "Kein Gegner in Reichweite", 6f);
        }

        private static void RemoteTorpedo()
        {
            var ship = Util.PlayerShip;
            var me = ship.SandboxEntity?.Country;
            Entity target = null; float best = float.MaxValue;
            foreach (var e in Entity.List)
            {
                if (!IsEnemyShip(e, me)) continue;
                float d = (e.transform.position - ship.transform.position).sqrMagnitude;
                if (d < best) { best = d; target = e; }
            }
            if (target == null) { ToolkitRunner.ShowNotice("Kein Gegner in Reichweite", 3f); return; }

            // A loaded tube gives its torpedo type (and loses it, unless unlimited torpedoes is on).
            string type = "G7a Torpedo T1 - Pi1.1";
            var tube = Util.Find<TorpedoLauncher>(2f).FirstOrDefault(l => l.Torpedo != null && l.IsLoaded && !(l.Torpedo is StoredMinePacket));
            if (tube == null && !Settings.B("CheatInfiniteTorpedoes")) { ToolkitRunner.ShowNotice("Kein Rohr geladen", 3f); return; }
            if (tube != null)
            {
                type = tube.Torpedo.name.Replace("(Clone)", "").Trim();
                if (!Settings.B("CheatInfiniteTorpedoes"))
                {
                    var t = tube.Torpedo;
                    tube.SetTorpedo(null);
                    Object.Destroy(t.gameObject);
                }
            }
            Spawn(target, type);
            ToolkitRunner.ShowNotice("Torpedo los auf " + target.name.Replace("(Clone)", "").Trim(), 3f);
        }

        private static void Spawn(Entity target, string type)
        {
            var sandbox = Object.FindObjectOfType<UBOAT.Game.Sandbox.Sandbox>();
            var w = Game.Wolfpack;
            if (sandbox == null || w == null) return;
            var se = SandboxEntity.Create(type, sandbox.NeutralCountry);
            se.SpawnsInstantly = true;
            w.AddEntity(se);
            se.UpdatePosition();
            if (!(se.SpawnedEntity is Torpedo torp)) return;
            Vector3 aim = target.transform.position;
            if (target is Ship s) { var f = target.transform.forward; f.y = 0f; aim += f.normalized * (s.AverageVelocity * 15f); }
            torp.WasSpawnedInsteadLaunched = true;
            var st = torp.GetComponent<StoredTorpedo>();
            st.Start();
            st.transform.position = aim + Quaternion.AngleAxis(Random.value * 360f, Vector3.up) * new Vector3(0f, 0f, 270f);
            var dir = aim - st.transform.position; dir.y = 0f; dir.Normalize();
            st.transform.rotation = Quaternion.AngleAxis(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Vector3.up);
            st.DudSeed = 1f;
            st.Launch(Util.PlayerShip, target.transform, aim, aim, 0, st.Speed1, new Collider[0]);
        }
    }

    // Tube icons: hovering over the torpedo symbol of a tube shows its exact type.
    [HarmonyPatch(typeof(UBOAT.Game.UI.TorpedoLauncherUI), "ValidateTorpedoIcon")]
    internal static class Strike_TubeTooltipPatch
    {
        private static void Postfix(UBOAT.Game.UI.TorpedoLauncherUI __instance)
        {
            if (!Settings.B("TubeDisplay")) return;
            var img = Traverse.Create(__instance).Field("torpedoImage").GetValue<Image>();
            if (img == null) return;
            var ui = __instance;
            UBOAT.Game.UI.Tooltip.SetTooltip(img.rectTransform, () =>
            {
                var t = ui.Launcher != null ? ui.Launcher.Torpedo : null;
                return t == null ? "leer" : t.name.Replace("(Clone)", "").Trim();
            });
        }
    }
}

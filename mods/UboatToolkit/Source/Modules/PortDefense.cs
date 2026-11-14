using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // DoMe 2026-11-14: port defence can be destroyed for a while. Port guns are equipment of the port entity (not a ship).
    // When OUR shot or torpedo hits near such a gun, the gun gets Health 0 (game then disables it) and is repaired after
    // PortDefenseMinutes (game time).
    public class PortDefense : ModuleBase
    {
        internal static readonly Dictionary<Gun, float> Down = new Dictionary<Gun, float>();
        private static readonly List<Gun> done = new List<Gun>();

        internal static bool IsPortGun(Gun g)
        {
            if (g == null || Util.IsPlayers(g)) return false;
            var e = g.ParentEntity;
            return e != null && !(e is Ship) && !(e is Aircraft);
        }

        internal static void Hit(Vector3 pos, float radius)
        {
            float r = Mathf.Max(radius, Settings.F("PortDefenseHitM"));
            foreach (var g in Util.Find<Gun>())
            {
                if (!IsPortGun(g) || g.Health <= 0f) continue;
                if ((g.transform.position - pos).sqrMagnitude > r * r) continue;
                g.Health = 0f;
                Down[g] = Time.time + Settings.F("PortDefenseMinutes") * 60f;
                Util.Log("Hafenverteidigung: " + g.name + " zerstört (" + g.ParentEntity.name + ")");
                ToolkitRunner.ShowNotice("Hafengeschütz zerstört", 4f);
            }
        }

        private float scanAt;
        public override void Tick()
        {
            // 0.3.37: Health 0 is saved with the game, the timer is not. After load / port scene reload, broken port
            // guns without timer get a new one, so they never stay broken forever.
            if (Time.unscaledTime >= scanAt)
            {
                scanAt = Time.unscaledTime + 10f;
                foreach (var g in Util.Find<Gun>())
                    if (IsPortGun(g) && g.Health <= 0f && !Down.ContainsKey(g))
                        Down[g] = Time.time + Settings.F("PortDefenseMinutes") * 60f;
            }
            if (Down.Count == 0) return;
            done.Clear();
            foreach (var kv in Down) if (kv.Key == null || Time.time >= kv.Value) done.Add(kv.Key);
            foreach (var g in done)
            {
                Down.Remove(g);
                if (g == null) continue;
                g.Health = g.MaxHealth.Value;
                Util.Log("Hafenverteidigung: " + g.name + " wieder einsatzbereit");
            }
        }
    }

    [HarmonyPatch(typeof(DamageUtility), nameof(DamageUtility.ApplyDamage))]
    internal static class PortDefense_HitPatch
    {
        private static void Postfix(Entity appliedBy, Vector3 position, float damageRadius)
        {
            if (!Settings.B("PortDefenseDestroyable") || appliedBy == null || appliedBy != Util.PlayerShip) return;
            Util.Safe("Hafenverteidigung", () => PortDefense.Hit(position, damageRadius));
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // 0.3.95 proof (DoMe: "up to 250 hits from one ship at over 5 km"; "the toolkit must provably only improve the
    // player, never the enemy"). Every enemy shell that bursts near our boat is logged: shooter, shell, distance,
    // damage, and whether any toolkit value is inside (damage/crew-damage modifier, changed body mass, changed speed).
    // Only reads, changes nothing. If a toolkit value is found it is also removed, so the shell flies with game values.
    [HarmonyPatch(typeof(Shell), nameof(Shell.Explode))]
    internal static class EnemyHitLog
    {
        private const string Mod = "UboatToolkit Shell";
        private class Stat { public int Hits, Toolkit; public float MinKm = 999f, MaxKm, Dmg; public string Sample; }
        private static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        private static float nextLog; private static int single;

        private static void Prefix(Shell __instance)
        {
            try
            {
                var ship = Util.PlayerShip;
                var by = __instance.LaunchedBy;
                if (ship == null || by == null || by == ship) return;
                Vector3 pos = __instance.transform.position;
                if ((pos - ship.transform.position).sqrMagnitude > 70f * 70f) return;   // not near our boat
                float km = (by.transform.position - ship.transform.position).magnitude / 1000f;
                var t = Traverse.Create(__instance);
                var dmg = t.Field("damage").GetValue<UBOAT.Game.Core.Data.Parameter>();
                var crew = t.Field("crewDamage").GetValue<UBOAT.Game.Core.Data.Parameter>();
                var mass = t.Field("mass").GetValue<UBOAT.Game.Core.Data.Parameter>();
                var rb = __instance.GetComponent<Rigidbody>();
                bool tk = false; string why = "";
                if (dmg != null && Mathf.Abs(dmg.Value - dmg.GetValueExcludingModifier(Mod)) > 0.001f) { tk = true; why += " Schaden"; dmg.RemoveScaleModifiers(Mod); }
                if (crew != null && Mathf.Abs(crew.Value - crew.GetValueExcludingModifier(Mod)) > 0.001f) { tk = true; why += " Crew-Schaden"; crew.RemoveScaleModifiers(Mod); }
                if (rb != null && mass != null && mass.Value > 0f && Mathf.Abs(rb.mass - mass.Value) > 0.001f) { tk = true; why += " Masse " + rb.mass.ToString("0.###") + "/" + mass.Value.ToString("0.###"); }
                string key = by.name + " / " + __instance.name.Replace("(Clone)", "").Trim();
                if (!stats.TryGetValue(key, out var s)) stats[key] = s = new Stat();
                s.Hits++; if (tk) s.Toolkit++;
                s.MinKm = Mathf.Min(s.MinKm, km); s.MaxKm = Mathf.Max(s.MaxKm, km);
                float d = dmg != null ? dmg.Value : 0f; s.Dmg += d;
                if (single < 40)
                {
                    single++;
                    Util.Log("Treffer am Boot: " + key + ", Schütze " + km.ToString("0.0") + " km entfernt, Schaden " + d.ToString("0.##")
                        + ", Crew-Schaden " + (crew != null ? crew.Value.ToString("0.##") : "?") + ", Toolkit-Werte: " + (tk ? "JA (" + why.Trim() + ", entfernt)" : "nein"));
                }
                if (Time.unscaledTime >= nextLog) Flush();
            }
            catch { }
        }

        internal static void Flush()
        {
            nextLog = Time.unscaledTime + 60f;
            if (stats.Count == 0) return;
            foreach (var kv in stats)
                Util.Log("Treffer-Summe letzte Minute: " + kv.Key + " " + kv.Value.Hits + "x, Entfernung " + kv.Value.MinKm.ToString("0.0") + "-" + kv.Value.MaxKm.ToString("0.0")
                    + " km, Schaden zusammen " + kv.Value.Dmg.ToString("0.#") + ", davon mit Toolkit-Werten: " + kv.Value.Toolkit);
            stats.Clear();
        }
    }
}

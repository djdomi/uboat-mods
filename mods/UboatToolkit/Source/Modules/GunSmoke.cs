using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // 0.3.73 (DoMe: suppress the smoke of the 88). The muzzle flash + smoke of a gun is one particle effect per barrel
    // (Gun.Barrel.FireEffect). With "GunSmokeOff" the emission of that effect (and its child effects) is switched off on
    // the PLAYER's guns, so the game's Play() shows nothing. No object is deactivated. Switched off again = effect back. Enemy guns are never touched.
    // Measured 2026-11-16: all particle effects together cost ~0.1 ms per frame, so this is mainly for the look.
    public class GunSmoke : ModuleBase
    {
        private static readonly System.Reflection.FieldInfo BarrelsField = AccessTools.Field(typeof(Gun), "barrels");
        private static readonly System.Reflection.FieldInfo EffectField =
            AccessTools.Inner(typeof(Gun), "Barrel") != null ? AccessTools.Field(AccessTools.Inner(typeof(Gun), "Barrel"), "FireEffect") : null;
        private static readonly List<ParticleSystem> off = new List<ParticleSystem>();
        private bool logged;

        public override void Tick()
        {
            bool want = Settings.B("GunSmokeOff");
            if (!want)
            {
                if (off.Count == 0) return;
                foreach (var p in off) if (p != null) { var em = p.emission; em.enabled = true; }
                off.Clear(); logged = false;
                return;
            }
            if (BarrelsField == null || EffectField == null) return;
            int n = 0;
            foreach (var g in Util.Find<Gun>())
            {
                if (g == null) continue;
                var arr = BarrelsField.GetValue(g) as System.Array;
                if (arr == null) continue;
                foreach (var b in arr)
                {
                    var ps = b == null ? null : EffectField.GetValue(b) as ParticleSystem;
                    if (ps == null) continue;
                    foreach (var p in ps.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var em = p.emission;
                        if (!em.enabled) continue;
                        em.enabled = false;
                        p.Clear(false);
                        off.Add(p); n++;
                    }
                }
            }
            if (n > 0 && !logged) { logged = true; Util.Log("Mündungsrauch: " + n + " Effekte der eigenen Geschütze abgeschaltet"); }
        }
    }
}

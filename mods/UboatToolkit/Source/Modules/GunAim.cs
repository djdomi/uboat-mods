using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Shell weight and damage, aim assist (QoL) and perfect aim (cheat). Only your own boat's guns.
    //  * Shell weight: the shell's physics mass (own value in kg, 0 = game) times a multiplier.
    //    Note: in this game the damage does NOT come from the weight, so there is an own damage multiplier.
    //  * Aim assist (GunAimAssist, degrees): when YOU fire and an enemy ship you can see is within this
    //    angle of your barrel, the shot flies with the game's own lead calculation (the same one the crew
    //    uses: target speed and shell flight time). 0 = off.
    //  * Cheat perfect aim: crew always at 100 % ranging, no spread at all (also not at night), and your own
    //    shots use the lead calculation for any enemy within 30 degrees.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class GunAim_FirePatch
    {
        private const string ModName = "UboatToolkit Shell";
        private static readonly List<Entity> targets = new List<Entity>();
        private static readonly System.Func<Entity, Vector3> ShootPoint =
            AccessTools.MethodDelegate<System.Func<Entity, Vector3>>(AccessTools.Method(typeof(Gun), "GetEntityShootingTarget"));

        [HarmonyPriority(Priority.High)]
        private static void Prefix(Gun __instance, bool ___manualAim, ref float ___currentAccuracy, ref Entity target)
        {
            if (!Util.IsPlayers(__instance)) return;
            bool cheat = Settings.B("CheatPerfectAim");
            if (cheat) ___currentAccuracy = 1f;
            if (!___manualAim || target != null) return;

            float cone = cheat ? Mathf.Max(30f, Settings.F("GunAimAssist")) : Settings.F("GunAimAssist");
            if (cone <= 0f) return;
            Transform barrel = __instance.VerticalAngleTransform != null ? __instance.VerticalAngleTransform : __instance.HorizontalAngleTransform;
            if (barrel == null) return;
            targets.Clear();
            __instance.GetPotentialTargets(targets);
            Entity best = null; float bestAngle = cone;
            foreach (var e in targets)
            {
                if (e == null || !e.isActiveAndEnabled) continue;
                Vector3 to = ShootPoint(e) - barrel.position;
                float a = Vector3.Angle(barrel.forward, to);
                if (a < bestAngle) { bestAngle = a; best = e; }
            }
            if (best == null) return;
            target = best; // the game now aims this shot with its own lead calculation
            if (Settings.B("CrewSkillLog")) Util.Log("Zielhilfe: Schuss auf " + best.name + " (" + bestAngle.ToString("0.0") + " Grad daneben gezielt)");
        }

        private static void Postfix(Gun __instance, Shell __result)
        {
            if (__result == null || !Util.IsPlayers(__instance)) return;
            string p = Weapons.Prefix(__instance);
            float kg = Settings.F(p + "ShellMass"), massMul = Settings.F(p + "ShellMassMul"), dmgMul = Settings.F(p + "ShellDamageMul");
            var rb = __result.GetComponent<Rigidbody>();
            var massParam = Traverse.Create(__result).Field("mass").GetValue<UBOAT.Game.Core.Data.Parameter>();
            if (rb != null && massParam != null)
            {
                float m = (kg > 0f ? kg : massParam.Value) * (massMul > 0f ? massMul : 1f);
                rb.mass = Mathf.Max(0.0001f, m);
            }
            foreach (var name in new[] { "damage", "crewDamage" })
            {
                var par = Traverse.Create(__result).Field(name).GetValue<UBOAT.Game.Core.Data.Parameter>();
                if (par == null) continue;
                par.RemoveScaleModifiers(ModName); // shells are re-used from a pool
                if (dmgMul > 0f && Mathf.Abs(dmgMul - 1f) > 0.001f) par.AddScaleModifier(ModName).Value = dmgMul;
            }
        }
    }
}

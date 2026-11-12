using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Shell weight and damage, aim assist (QoL) and perfect aim (cheat). Only your own boat's guns.
    //  * Shell weight: the shell's physics mass (own value in kg, 0 = game) times a multiplier.
    //    Note: in this game version the damage does NOT come from weight or speed (Shell.Explode uses only the
    //    Damage / CrewDamage values x a random 1..2). So there are own multipliers for damage and crew damage,
    //    and an optional "weight x speed" mode that scales both like the first release did.
    //    ArmorPiercing exists as a value but the game never reads it, so a setting for it would do nothing.
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
            float kg = Settings.F(p + "ShellMass"), massMul = Settings.F(p + "ShellMassMul");
            float dmgMul = Settings.F(p + "ShellDamageMul"), crewMul = Settings.F(p + "ShellCrewDamageMul");
            if (dmgMul <= 0f) dmgMul = 1f;
            if (crewMul <= 0f) crewMul = 1f;
            var rb = __result.GetComponent<Rigidbody>();
            var massParam = Traverse.Create(__result).Field("mass").GetValue<UBOAT.Game.Core.Data.Parameter>();
            float gameMass = massParam != null ? massParam.Value : 0f, mass = gameMass;
            if (rb != null && massParam != null)
            {
                mass = Mathf.Max(0.0001f, (kg > 0f ? kg : gameMass) * (massMul > 0f ? massMul : 1f));
                rb.mass = mass;
            }
            // Optional (like the first release): damage grows with weight x speed compared to the game's shell.
            float kinetic = 1f;
            if (Settings.B(p + "KineticDamage") && gameMass > 0f)
            {
                float gameSpeed = Util.Original<float>(__instance, "shellSpeed");
                float speed = Traverse.Create(__instance).Field("shellSpeed").GetValue<float>();
                if (gameSpeed > 0f) kinetic = Mathf.Clamp(mass * speed / (gameMass * gameSpeed), 0.01f, 10000f);
            }
            // The shell is a real physics body: heavier/faster shells shoved the hit ship away (one was knocked
            // off the map). Damage comes from the values above, so the body only keeps the game's own push
            // (mass x speed). Trajectory is unchanged: gravity and drag do not depend on mass.
            if (rb != null && gameMass > 0f && Settings.B("NoShellPush"))
            {
                float gs = Util.Original<float>(__instance, "shellSpeed");
                float sp = Traverse.Create(__instance).Field("shellSpeed").GetValue<float>();
                float maxMass = sp > 0f && gs > 0f ? gameMass * gs / sp : gameMass;
                rb.mass = Mathf.Max(0.0001f, Mathf.Min(rb.mass, maxMass));
            }
            Scale(__result, "damage", dmgMul * kinetic);
            Scale(__result, "crewDamage", crewMul * kinetic);
        }

        private static void Scale(Shell shell, string field, float f)
        {
            var par = Traverse.Create(shell).Field(field).GetValue<UBOAT.Game.Core.Data.Parameter>();
            if (par == null) return;
            par.RemoveScaleModifiers(ModName); // shells are re-used from a pool
            if (Mathf.Abs(f - 1f) > 0.001f) par.AddScaleModifier(ModName).Value = f;
        }
    }
}

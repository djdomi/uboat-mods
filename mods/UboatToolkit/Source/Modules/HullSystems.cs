using System;
using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Activators;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Items.Statuses;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator;

namespace UboatToolkit.Modules
{
    // Ballast valve orders are always offered (only not when the valve is broken or in port).
    [HarmonyPatch]
    internal static class Hull_BallastAlwaysPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(PartiallyFloodBallastTanksAction), typeof(PartiallyBlowBallastTanksAction), typeof(FloodBallastTanksAction) })
            {
                var m = AccessTools.Method(t, "GetState", new[] { typeof(Activator), typeof(PlayableCharacter), typeof(bool), typeof(bool), typeof(ActionContext) });
                if (m != null) yield return m;
            }
        }

        private static void Postfix(Activator activator, ref ActionStateData __result)
        {
            if (!Settings.B("BallastAlwaysAvailable") || __result.ActionState != ActionState.Hidden) return;
            var ship = Util.PlayerShip;
            if (ship == null || ship.Docked) return;
            if (!(activator?.AssociatedEquipment is Device d) || d.HasDisableFlag(DeviceDisableFlag.Damage)) return;
            __result = new ActionStateData(ActionState.Enabled);
        }
    }

    // Hatches show the air pressure on both sides and stay shut when it differs too much.
    [HarmonyPatch(typeof(Hatch), nameof(Hatch.GetStateText))]
    internal static class Hull_HatchTextPatch
    {
        private static void Postfix(Hatch __instance, ref string __result)
        {
            if (!Settings.B("HatchPressure")) return;
            var l = __instance.LeftCompartment; var r = __instance.RightCompartment;
            if (l == null || r == null) return;
            string p = "Luftdruck: " + l.AirPressure.ToString("0.00") + " / " + r.AirPressure.ToString("0.00") + " bar";
            if (Hull_HatchLockPatch.TooBig(__instance)) p += "  (gesperrt: Unterschied zu groß)";
            __result = string.IsNullOrEmpty(__result) ? p : __result + "\n" + p;
        }
    }

    [HarmonyPatch(typeof(Hatch), nameof(Hatch.CanBeOpened), MethodType.Getter)]
    internal static class Hull_HatchLockPatch
    {
        public static bool TooBig(Hatch h)
        {
            float lim = Settings.F("HatchPressureLimit");
            if (lim <= 0f || h.LeftCompartment == null || h.RightCompartment == null) return false;
            return Math.Abs(h.LeftCompartment.AirPressure - h.RightCompartment.AirPressure) > lim;
        }

        private static void Postfix(Hatch __instance, ref bool __result)
        {
            if (__result && Settings.B("HatchPressure") && TooBig(__instance)) __result = false;
        }
    }

    // Pressure equalizing valve. A hatch locked by pressure would stay shut forever,
    // so air slowly flows between both rooms (10 % of the difference per second) until the hatch can open again.
    public class HatchEqualizer : ModuleBase
    {
        public override void Tick()
        {
            if (!Settings.B("HatchPressure") || !Settings.B("HatchEqualize")) return;
            foreach (var h in Util.Find<Hatch>())
            {
                if (!Hull_HatchLockPatch.TooBig(h)) continue;
                var a = h.LeftCompartment; var b = h.RightCompartment;
                double pa = a.AirPressure, pb = b.AirPressure;
                if (pa <= 0.0001 || pb <= 0.0001) continue;
                double va = a.Air / pa, vb = b.Air / pb;
                double target = (a.Air + b.Air) / (va + vb);
                double move = (target * va - a.Air) * 0.1;
                a.Air += move;
                b.Air -= move;
            }
        }
    }

    // A leak is sealed like with two sailors (water comes in only half as fast as with one).
    [HarmonyPatch(typeof(BlockLeakAction), nameof(BlockLeakAction.UpdateAction))]
    internal static class Hull_LeakTwoMenPatch
    {
        private static void Postfix(BlockLeakAction __instance)
        {
            if (!Settings.B("LeakTwoSailors")) return;
            var m = Traverse.Create(__instance).Field("leakIntensityModifier").GetValue() as UBOAT.Game.Core.Data.Modifier;
            if (m != null && m.Value > 0.1f) m.Value = 0.1f;
        }
    }
}

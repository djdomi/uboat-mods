using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.HQ;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Course and depth on keys (Tasten tab): turn by 1/5/45/180 degrees, rudder amidships,
    // 5/20 m deeper or shallower, periscope depth, max safe depth, hold current depth.
    public class Helm : ModuleBase
    {
        private static readonly System.Reflection.MethodInfo course = AccessTools.Method(typeof(PlayerShip), "SetTargetCourse", new[] { typeof(float) });
        private static readonly System.Reflection.MethodInfo amid = AccessTools.Method(typeof(PlayerShip), "SetRudderAmidships");
        private static readonly string[] turnKeys = { "KeyTurnL1", "KeyTurnR1", "KeyTurnL5", "KeyTurnR5", "KeyTurnL45", "KeyTurnR45", "KeyTurnAbout" };
        private static readonly float[] turns = { -1, 1, -5, 5, -45, 45, 180 };
        private static readonly string[] depthKeys = { "KeyDeeper5", "KeyShallower5", "KeyDeeper20", "KeyShallower20" };
        private static readonly float[] steps = { 5, -5, 20, -20 };

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < turnKeys.Length; i++)
                if (Keys.Down(turnKeys[i])) { float d = turns[i]; Util.Safe("Helm.Turn", () => course?.Invoke(ship, new object[] { d })); return; }
            if (Keys.Down("KeyAmidships")) { Util.Safe("Helm.Amid", () => amid?.Invoke(ship, null)); return; }
            for (int i = 0; i < depthKeys.Length; i++)
                if (Keys.Down(depthKeys[i]))
                {
                    float s = steps[i];
                    Util.Safe("Helm.Depth", () => { float t = UnityEngine.Mathf.Max(0f, ship.TargetDepth + s); ship.SetDepthPreset(DepthPreset.None, true); ship.SetTargetDepth(t, false, true); });
                    return;
                }
            if (Keys.Down("KeyPeriscopeDepth")) { Util.Safe("Helm.Scope", () => ship.SetDepthPreset(DepthPreset.Periscope, false)); return; }
            if (Keys.Down("KeyMaxDepth")) { Util.Safe("Helm.Max", () => ship.SetDepthPreset(DepthPreset.MaxSafeDepth, false)); return; }
            if (Keys.Down("KeyHoldDepth")) Util.Safe("Helm.Hold", () => { float t = ship.Depth + ship.VerticalOriginOffset; ship.SetDepthPreset(DepthPreset.None, true); ship.SetTargetDepth(UnityEngine.Mathf.Max(0f, t), false, true); });
        }
    }

    // Wolfpack: if HQ is busy sending at that moment, try again later instead of dropping the wolfpack.
    [HarmonyPatch(typeof(Headquarters.WolfpackInitializer), "StartWolfpack")]
    internal static class Helm_WolfpackRetryPatch
    {
        private static readonly Dictionary<object, int> tries = new Dictionary<object, int>();
        private static bool Prefix(Headquarters.WolfpackInitializer __instance, Headquarters ___headquarters, ref float __result)
        {
            if (!Settings.B("FixWolfpackRetry") || ___headquarters == null) return true;
            var e = Traverse.Create(___headquarters).Field("sandboxEntity").GetValue<SandboxEntity>();
            if (e == null || e.Transmission == null) { tries.Remove(__instance); return true; }
            tries.TryGetValue(__instance, out int n);
            if (n >= 20) { tries.Remove(__instance); return true; }
            if (tries.Count > 200) tries.Clear();
            tries[__instance] = n + 1;
            __result = 30f;   // ask again in 30 s
            return false;
        }
    }
}

using HarmonyLib;
using UBOAT.Game.Sandbox;

namespace UboatToolkit.Modules
{
    // Far-away world (ships/convoys outside player range) is simulated in Sandbox.Update():
    // every group gets one "low frequency" AI/observation update per lowFrequencyUpdateInterval
    // (game default 30 s game-time, private field in Sandbox). Cost per second ~ groups / interval.
    // Raising the interval = fewer far-AI updates = less CPU, most noticeable with big mods
    // (many groups) and time compression. Nearby + observed groups are NOT affected (updated every frame).
    // Trade-off: far convoys react/re-route a bit later. Setting "FarAIInterval" in seconds,
    // 30 = game default (no change), 0 = off. Suggested test: 60.
    [HarmonyPatch(typeof(Sandbox), "Update")]
    public static class FarAIThrottle
    {
        private static readonly AccessTools.FieldRef<Sandbox, float> interval =
            AccessTools.FieldRefAccess<Sandbox, float>("lowFrequencyUpdateInterval");

        private static void Prefix(Sandbox __instance)
        {
            float v = Settings.F("FarAIInterval");
            if (v >= 5f && interval(__instance) != v) interval(__instance) = v;
        }
    }
}

using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment;

namespace UboatToolkit.Modules
{
    // QoL #24: deck awash with usable guns.
    // The game closes the exterior hatch whenever the boat runs deck awash
    // (PlayerShip.ShouldExteriorHatchBeClosed), so the crew cannot reach the guns.
    // The guns themselves are only disabled when the boat is submerged (target depth > 0.5 m),
    // and deck awash uses 0.45 m, so opening the hatch is enough.
    // Rain and strong wind still close the hatch, like in the original game.
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.ShouldExteriorHatchBeClosed), MethodType.Getter)]
    internal static class AwashGuns_HatchPatch
    {
        private static void Postfix(PlayerShip __instance, ref bool __result)
        {
            if (!Settings.AwashGuns || !__result) return;
            if (__instance.DepthPreset != DepthPreset.DeckAwash) return;

            // Re-check the weather part of the original rule; only the "deck awash" reason is removed.
            var weather = Traverse.Create(typeof(PlayerShip)).Field("weather").GetValue<Weather>();
            if (weather == null) return;
            float rain = (float)weather.Rain;
            float wind = weather.TotalWindSpeed;
            bool badWeather = rain > 0.025f || (wind > 17f && !__instance.Docked);
            if (!badWeather) __result = false;
        }
    }
}

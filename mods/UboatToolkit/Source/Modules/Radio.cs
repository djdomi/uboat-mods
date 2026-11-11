using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Messages;

namespace UboatToolkit.Modules
{
    // QoL #11 + #12: faster radio messages, enemies cannot direction-find the player's transmissions.
    public class Radio : ModuleBase { }

    [HarmonyPatch(typeof(SandboxEntity), nameof(SandboxEntity.StartTransmission))]
    internal static class Radio_FasterPatch
    {
        private static void Postfix(SandboxEntity __instance, RadioTransmission transmission, bool __result)
        {
            if (!__result || transmission == null || !__instance.IsPlayerShip) return;
            float speed = Settings.F("RadioSpeed");
            if (speed > 1f) transmission.RemainingTransmissionDuration /= speed;
        }
    }

    // SandboxEntity.UpdateTransmission lets nearby enemy groups detect a transmission (direction finding).
    // For the player's boat the method does nothing else, so it is skipped entirely
    // (the "can transmit" check is kept so a transmission still stops when it has to).
    [HarmonyPatch(typeof(SandboxEntity), "UpdateTransmission")]
    internal static class Radio_UndetectablePatch
    {
        private static bool Prefix(SandboxEntity __instance, ref float __result)
        {
            if (!Settings.B("RadioUndetectable") || !__instance.IsPlayerShip) return true;
            if (!Traverse.Create(__instance).Method("CanTransmit").GetValue<bool>()) return true;
            __result = 2f;
            return false;
        }
    }
}

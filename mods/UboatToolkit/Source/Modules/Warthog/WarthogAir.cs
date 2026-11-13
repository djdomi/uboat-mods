using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Tasks;
using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Air on U-1337:
    //  - Ventilation always on and silent (noise x 0), also in silent running: the crew is not sent
    //    to switch it off any more.
    //  - Electrolysis: oxygen is made from sea water with battery power, so the air never goes bad
    //    and no potash absorbers (Kalipatronen) are used up.
    // Overlap with Toolkit "Air": on U-1337 this module wins (noise 0, no absorber use).
    public class WarthogAir : ModuleBase
    {
        internal static bool SilentVent => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhSilentVent");
        internal static bool Electrolysis => WarthogSettings.Active(Util.PlayerShip) && Settings.B("WhElectrolysis");

        public override void Tick()
        {
            bool silent = SilentVent;
            foreach (var v in Util.Find<Ventilation>())
            {
                var noise = Traverse.Create(v).Field("noise").GetValue<Parameter>();
                if (noise != null) noise.GetScaleModifier("Warthog Silent", true).Value = silent ? 0.0001f : 1f;
                if (silent && !v.enabled) v.SetDisableFlag(DeviceDisableFlag.TurnedOff, false);
            }
        }
    }

    // Silent running: do not send a man to switch the ventilation off.
    [HarmonyPatch(typeof(SilentRunningController), "AddAction")]
    internal static class WarthogAir_SilentPatch
    {
        private static bool Prefix(UBOAT.Game.Scene.Characters.Activators.Activator activator)
        {
            if (activator == null || !WarthogAir.SilentVent) return true;
            return activator.GetComponent<Ventilation>() == null;   // skip only the ventilation
        }
    }

    // Electrolysis: the regeneration limit stays full and the absorber is never used up.
    [HarmonyPatch(typeof(Ventilation), "UpdateOxygenModifier")]
    internal static class WarthogAir_OxygenPatch
    {
        private static void Prefix(Ventilation __instance, ref float ___potassiumAbsorberUse, ref float ___oxygenRegenerationLimit, Parameter ___regenerationLimit)
        {
            if (!WarthogAir.Electrolysis || !Util.IsPlayers(__instance)) return;
            ___potassiumAbsorberUse = 0f;                                   // absorber is never consumed
            if (___regenerationLimit != null) ___oxygenRegenerationLimit = ___regenerationLimit.Value; // air quality stays 100 %
        }
    }
}

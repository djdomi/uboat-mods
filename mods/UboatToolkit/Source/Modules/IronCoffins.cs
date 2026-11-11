using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Ideas taken over from the old "Iron Coffins" mod (2022), rebuilt for 2026.1.
    // H: injured crew is not carried away automatically.  J: rebreathers last longer.
    public class IronCoffins : ModuleBase
    {
        public override void Tick()
        {
            float f = Settings.F("RebreatherDurability");
            foreach (var r in Util.PlayerShip.GetComponentsInChildren<Rebreather>(true))
                Util.ScaleParameter(Traverse.Create(r).Field("durability").GetValue<Parameter>(), f);
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.AutoCarryWoundedToSafety), "DoUpdate")]
    internal static class IronCoffins_NoAutoCarryPatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!Settings.B("NoAutoCarryWounded")) return true;
            __result = 5f;
            return false;
        }
    }
}

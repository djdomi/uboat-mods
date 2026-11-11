using HarmonyLib;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Crew gear and wounded handling.
    // H: injured crew is not carried away automatically.  J: rebreathers last longer.
    public class CrewGear : ModuleBase
    {
        public override void Tick()
        {
            float f = Settings.F("RebreatherDurability");
            foreach (var r in Util.Find<Rebreather>())
                Util.ScaleParameter(Traverse.Create(r).Field("durability").GetValue<Parameter>(), f);
        }
    }

    // Only the "carry wounded" part is blocked: the game skips carrying when no bed is free.
    // Pulling divers back on board keeps working.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.AutoCarryWoundedToSafety), "DoUpdate")]
    internal static class CrewGear_NoAutoCarryPatch
    {
        internal static bool Inside;
        private static void Prefix() { Inside = Settings.B("NoAutoCarryWounded"); }
        private static void Finalizer() { Inside = false; }
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Entities.PlayerShip), "HasFreeBed")]
    internal static class CrewGear_NoFreeBedPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!CrewGear_NoAutoCarryPatch.Inside) return true;
            __result = false;
            return false;
        }
    }
}

using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Statuses;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Cheat "deck crew stays": the boat dives even with men on deck, they stay at the guns and never drown.
    // Guns stay usable while diving; they still fire only as long as the gun itself is above the water.

    // The game refuses to close the tower hatch while someone is on deck (and drowns the injured).
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.CloseExteriorHatch), "ValidateHatchOpeness")]
    internal static class DeckCrew_HatchPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Settings.B("CheatDeckCrewStays")) return true;
            var ship = Util.PlayerShip;
            var hatch = ship != null ? ship.GetEquipment<Hatch>("Exterior") : null;
            if (hatch == null) return true;
            hatch.Locked = true;
            __result = true;
            return false;
        }
    }

    // Nobody drowns: "drowning off board" death is blocked.
    [HarmonyPatch(typeof(PlayableCharacterData), nameof(PlayableCharacterData.AddStatus), new[] { typeof(CharacterStatus) })]
    internal static class DeckCrew_NoDrownPatch
    {
        // With "immortal crew" every death is blocked (also deaths the game sets directly, not via health).
        // Immortal also means unhurt: no bleeding wounds and no serious injuries.
        private static bool Prefix(CharacterStatus status)
        {
            if (Settings.B("CheatImmortalCrew") && (status is Dead || status is Bleeding || status is SeriousInjuryGeneration)) return false;
            return !(Settings.B("CheatDeckCrewStays") && status is Dead d && d.Cause == DeathCause.DrowningOffBoard);
        }
    }

    // Guns are not switched off when the boat dives.
    [HarmonyPatch(typeof(Gun), "ParentShip_SubmergeStateChanged")]
    internal static class DeckCrew_GunPatch
    {
        private static void Postfix(Gun __instance)
        {
            if (Settings.B("CheatDeckCrewStays")) __instance.SetDisableFlag(DeviceDisableFlag.Depth, false);
        }
    }

    // Immortal crew does not fall over from hits.
    [HarmonyPatch(typeof(PlayableCharacter), nameof(PlayableCharacter.EnableRagdoll))]
    internal static class DeckCrew_NoRagdollPatch
    {
        private static bool Prefix() => !Settings.B("CheatImmortalCrew");
    }
}

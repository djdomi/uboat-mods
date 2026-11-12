using System;
using HarmonyLib;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Skills;

namespace UboatToolkit.Modules
{
    // Bugs in the game itself that showed up in DoMe's log (2026-11-12).

    // Actions without requirements ("*" in the data sheet) have null lists. A man with a
    // "no helpers" skill then crashed when starting such an action (e.g. a watch station).
    [HarmonyPatch(typeof(DisallowFollowersStatus), "ValidateHelperLimits")]
    internal static class Fix_DisallowFollowersNull
    {
        private static bool Prefix(DisallowFollowersStatus __instance, SingleCharacterAction action, ref int workers, ref int followers)
        {
            try
            {
                if (action == null) return false;
                var hard = action.HardRequirements; var soft = action.SoftRequirements;
                string n = __instance.Name;
                if ((hard != null && Array.IndexOf(hard, n) != -1) || (soft != null && Array.IndexOf(soft, n) != -1))
                {
                    workers = 0;
                    followers = 0;
                }
                return false;
            }
            catch { return true; }
        }
    }

    // "Advanced Equipment" skill: entering a listening room on a boat without a radar detector crashed
    // (the game looks for the detector without checking). Without a detector there is nothing to boost.
    [HarmonyPatch(typeof(AdvancedEquipment), "Character_InteractableSlotChanged")]
    internal static class Fix_AdvancedEquipmentNoDetector
    {
        private static Exception Finalizer(Exception __exception) => __exception is NullReferenceException ? null : __exception;
    }
}

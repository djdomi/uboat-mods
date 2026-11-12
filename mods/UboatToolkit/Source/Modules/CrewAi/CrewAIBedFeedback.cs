// Crew-KI (Teil des UBOAT Toolkit): CrewAIBedFeedback. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.CrewAi
{
    [HarmonyPatch(typeof(GoToBehaviour), "Cancel")]
    internal static class Patch_BedChoice_PathFailureFeedback
    {
        static readonly FieldInfo NavigatorField = AccessTools.Field(typeof(GoToBehaviour), "navigator");
        static readonly FieldInfo TargetField = AccessTools.Field(typeof(GoToBehaviour), "loggingTarget");
        static readonly FieldInfo PathValidField = AccessTools.Field(typeof(GoToBehaviour), "wasPathValid");

        static void Prefix(GoToBehaviour __instance)
        {
            if (!BedChoiceToggles.PreferNearestBed) return;
            try
            {
                if (__instance == null || NavigatorField == null || TargetField == null) return;

                if (PathValidField != null && (bool)PathValidField.GetValue(__instance)) return;

                var tr = TargetField.GetValue(__instance) as Transform;
                if (tr == null) return;
                var bed = tr.GetComponentInParent<Bed>();
                if (bed == null) return;

                var nav = NavigatorField.GetValue(__instance) as NavMeshNavigator;
                if (nav == null) return;
                var c = nav.Character as PlayableCharacter;
                if (c == null) return;

                NearestBed.NoteBedPathFailed(c, bed);
            }
            catch (Exception e)
            {
                DiagLog.WriteError("BedChoice.PathFailureFeedback", e);
            }
        }
    }
}

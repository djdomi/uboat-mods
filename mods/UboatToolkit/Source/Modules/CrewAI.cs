using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.AI.Behaviours;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Tasks;

namespace UboatToolkit.Modules
{
    // Smarter crew: no eating during alarm, fewer bow runners, nearest free bunk, officers leave the periscopes alone.

    // During alarm the crew only eats when really hungry.
    [HarmonyPatch(typeof(EatBehaviour), "OnBehaviourBackgroundUpdate")]
    internal static class CrewAI_EatPatch
    {
        private static void Postfix(EatBehaviour __instance, Need ___hunger)
        {
            if (!Settings.B("CrewNoEatAlarm") || ___hunger == null) return;
            var ship = Util.PlayerShip;
            if (ship != null && ship.Alarmed && ___hunger.stress < 0.85f) __instance.Profit = 0f;
        }
    }

    // Crash dive: at most 8 men run to the bow, not the whole idle crew.
    [HarmonyPatch(typeof(IdleCrewRunsAtBow), "PlayerShipOnSubmergeStateChanged")]
    internal static class CrewAI_BowRunPatch
    {
        private static void Postfix(RunAtBowAction ___action)
        {
            if (!Settings.B("CrewBowRunCap") || ___action == null) return;
            Util.Safe("CrewAI.BowRun", () =>
            {
                var list = ___action.Characters;
                while (list != null && list.Count > 8) ___action.RemoveCharacter(list[list.Count - 1]);
            });
        }
    }

    // Tired men take the nearest free bunk instead of a random one across the boat.
    [HarmonyPatch]
    internal static class CrewAI_BedPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(PlayerShip), "FindFreeBed");
            yield return AccessTools.Method(typeof(PlayerShip), "FindFreeOfficerBed");
        }
        private static void Postfix(NavMeshNavigator navigator, List<Bed> ___bedsBuffer, ref Bed __result)
        {
            if (!Settings.B("CrewNearestBed") || __result == null || navigator == null || ___bedsBuffer == null || ___bedsBuffer.Count < 2) return;
            var p = navigator.transform.position;
            Bed best = __result; float bd = float.MaxValue;
            foreach (var b in ___bedsBuffer)
            {
                if (b == null) continue;
                float d = (b.transform.position - p).sqrMagnitude;
                if (d < bd) { bd = d; best = b; }
            }
            __result = best;
        }
    }

    // Officers (not the commander) do not grab the periscopes on their own; your direct order still works.
    [HarmonyPatch(typeof(BasicObservationPeriscopeUseAction), "GetActionProfit", new[] { typeof(UBOAT.Game.Scene.Characters.Activators.Activator), typeof(PlayableCharacter) })]
    internal static class CrewAI_ScopePatch
    {
        private static void Postfix(PlayableCharacter character, ref ActionProfit __result)
        {
            if (!Settings.B("CrewScopeNoOfficers") || character == null) return;
            if (character.IsOfficer && !character.IsSkipper) __result = new ActionProfit(__result.ProfitForPlayer, 0f);
        }
    }
}

using HarmonyLib;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Cheats: crew walks faster (#32), crew cannot die / lose health, supplies never run out (#10).
    public class CrewCheats : ModuleBase
    {
        private PlayerCrew crew;

        public override void Tick()
        {
            if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew == null) return;
            float f = Mathf.Max(1f, Settings.F("CheatCrewWalkSpeed"));
            foreach (var c in crew.Characters)
            {
                if (c == null) continue;
                var nav = c.GetComponent<NavMeshNavigator>();
                if (nav != null) Util.ScaleParameter(nav.SpeedMultiplier, f);
            }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "SetHealth")]
    internal static class CrewCheats_HealthPatch
    {
        private static void Prefix(PlayableCharacter __instance, ref float value)
        {
            if (!Settings.B("CheatImmortalCrew")) return;
            if (__instance.Data == null) return;
            var t = __instance.Data.CharacterType;
            if (t != PlayableCharacterData.Type.Sailor && t != PlayableCharacterData.Type.Officer && t != PlayableCharacterData.Type.Skipper) return;
            if (value < __instance.Health) value = __instance.Health;
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "BleedoutHealth", MethodType.Setter)]
    internal static class CrewCheats_BleedoutPatch
    {
        private static void Prefix(PlayableCharacter __instance, ref float value)
        {
            if (!Settings.B("CheatImmortalCrew")) return;
            if (value < __instance.BleedoutHealth) value = __instance.BleedoutHealth;
        }
    }

    // Supplies on the boat are never used up (food, spare parts, medkits ...). Cooking still works.
    [HarmonyPatch(typeof(Cargo), "Consume")]
    internal static class CrewCheats_SupplyPatch
    {
        private static bool Prefix(Cargo __instance, float count)
        {
            if (!Settings.B("CheatNoSupplyUse") || count <= 0f) return true;
            return !Util.IsPlayers(__instance);
        }
    }
}

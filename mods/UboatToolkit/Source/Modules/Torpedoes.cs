using System.Linq;
using HarmonyLib;
using UnityEngine;
using DWS.Common.Resources;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // #26 / cheats: unlimited torpedoes (the tube is reloaded right after the shot, the same way the game
    // itself loads tubes at campaign start), optional T5 "Zaunkönig" homing torpedoes, and no duds.
    public class Torpedoes : ModuleBase
    {
        public const string T5 = "G7es Zaunkönig Torpedo T5 - Pi4";
        private static ResourceManager resources;

        public static ResourceManager Resources
        {
            get
            {
                if (resources == null) resources = UnityEngine.Resources.FindObjectsOfTypeAll<ResourceManager>().FirstOrDefault();
                return resources;
            }
        }

        private static UserSettings userSettings;

        public override void Tick()
        {
            // #17: use the game's own automatic torpedo solution (click target -> values are filled in).
            if (Settings.B("EasyTDC"))
            {
                if (userSettings == null) userSettings = UnityEngine.Resources.FindObjectsOfTypeAll<UserSettings>().FirstOrDefault();
                var gs = userSettings?.GameplaySettings;
                if (gs != null)
                {
                    gs.disableAutomaticTorpedoSolutions = false;
                    gs.disableTorpedoSolutionPreviews = false;
                }
            }

            if (!Settings.B("CheatNoDuds")) return;
            foreach (var t in Util.PlayerShip.GetComponentsInChildren<Torpedo>(true))
                t.DudsDisabled = true;
        }
    }

    [HarmonyPatch(typeof(TorpedoLauncher), nameof(TorpedoLauncher.OnLaunchingComplete))]
    internal static class Torpedoes_ReloadPatch
    {
        private static void Prefix(TorpedoLauncher __instance, out string __state)
        {
            __state = null;
            if (!Settings.B("CheatInfiniteTorpedoes") || !Util.IsPlayers(__instance)) return;
            var fired = __instance.Torpedo;
            if (fired != null && !(fired is StoredMinePacket)) __state = fired.name.Replace("(Clone)", "").Trim();
        }

        private static void Postfix(TorpedoLauncher __instance, string __state)
        {
            if (string.IsNullOrEmpty(__state) || __instance.Torpedo != null) return;
            Util.Safe("torpedo reload", () =>
            {
                var rm = Torpedoes.Resources;
                if (rm == null) return;
                string name = Settings.B("CheatAllT5") ? Torpedoes.T5 : __state;
                var go = rm.InstantiatePrefab("Equipment/" + name);
                if (go == null && name != __state) go = rm.InstantiatePrefab("Equipment/" + __state);
                var stored = go != null ? go.GetComponent<StoredTorpedo>() : null;
                if (stored == null) { if (go != null) Object.Destroy(go); return; }
                __instance.SetTorpedo(stored);
                __instance.CompleteLoading(quiet: true);
            });
        }
    }
}

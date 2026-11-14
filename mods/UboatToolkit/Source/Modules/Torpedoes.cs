using System.Linq;
using HarmonyLib;
using UnityEngine;
using DWS.Common.Resources;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Time;
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
            foreach (var t in Util.Find<Torpedo>(1f))
                if (t != null && t.Owner == Util.PlayerShip) t.DudsDisabled = true;   // 0.3.34: own torpedoes only
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
    // QoL: a torpedo the crew has maintained stays maintained (player's boat only). The game normally drops the
    // "WarmedUp" dud bonus when the maintenance cooldown runs out and the crew then maintains the tube again.
    internal static class TorpedoKeep
    {
        private static readonly System.Reflection.FieldInfo GameTimeF = AccessTools.Field(typeof(StoredTorpedo), "gameTime");
        internal static readonly AccessTools.FieldRef<StoredTorpedo, long> WarmUpTicks = AccessTools.FieldRefAccess<StoredTorpedo, long>("warmUpTicks");
        private static bool logged;

        internal static bool Wanted(StoredTorpedo t)
        {
            if (t == null || !Settings.B("TorpedoStayMaintained") || t.IsLaunched || !t.IsMaintained) return false;
            var ship = Util.PlayerShip;
            if (ship == null) return false;
            return (object)t.ParentEntity == ship || Util.IsPlayers(t);
        }

        internal static bool Refresh(StoredTorpedo t)
        {
            var gt = GameTimeF != null ? GameTimeF.GetValue(null) as GameTime : null;
            if (gt == null) return false;
            WarmUpTicks(t) = gt.ContinuousTicks;
            if (!logged) { logged = true; Util.Log("Torpedos bleiben gewartet: " + t.name + " bleibt gewartet"); }
            return true;
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), "RemoveWarmUpModifier")]
    internal static class Torpedoes_KeepMaintainedPatch
    {
        private static bool Prefix(StoredTorpedo __instance, ref float __result)
        {
            try
            {
                if (!TorpedoKeep.Wanted(__instance) || !TorpedoKeep.Refresh(__instance)) return true;
                __result = Mathf.Max(60f, (float)__instance.MaintenanceCooldown);
                return false;
            }
            catch { return true; }
        }
    }

    [HarmonyPatch(typeof(StoredTorpedo), nameof(StoredTorpedo.MaintenanceCooldownProgress), MethodType.Getter)]
    internal static class Torpedoes_KeepProgressPatch
    {
        private static void Postfix(StoredTorpedo __instance, ref float __result)
        {
            try
            {
                if (__result > 0.05f && __result < 1f && TorpedoKeep.Wanted(__instance) && TorpedoKeep.Refresh(__instance)) __result = 0f;
            }
            catch { }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.Achievements;

namespace UboatToolkit.Modules
{
    // - Achievements stay allowed even when the developer console was used (also in old saves:
    //   the save's "locked out" flag is cleared, so it stays clean after the next save).
    // - One-shot cheat: unlock every Steam achievement of the game.
    public class AchievementTools : ModuleBase
    {
        public override void Tick()
        {
            if (Settings.B("AchievementsOn"))
            {
                // Searching all objects every second cost 40-46 ms (part of the lag). The career is cached.
                var pc = Util.Career;
                if (pc != null) Traverse.Create(pc).Field("isPlaythroughLockedOutOfAchievements").SetValue(false);
            }

            var o = Settings.Get("CheatAllAchievements");
            if (o != null && o.On) { o.Value = 0; Util.Safe("AllAchievements", UnlockAll); }
        }

        private static void UnlockAll()
        {
            var ids = typeof(AchievementManager).GetNestedType("AchievementIDs", BindingFlags.NonPublic);
            Type stats = HarmonyLib.AccessTools.TypeByName("Steamworks.SteamUserStats");
            if (ids == null || stats == null) { ToolkitRunner.ShowNotice("Steam nicht gefunden", 4f); return; }
            var set = stats.GetMethod("SetAchievement", new[] { typeof(string) });
            var store = stats.GetMethod("StoreStats", Type.EmptyTypes);
            int n = 0;
            foreach (var f in ids.GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.GetRawConstantValue() is string name) { set.Invoke(null, new object[] { name }); n++; }
            store?.Invoke(null, null);
            ToolkitRunner.ShowNotice(n + " Erfolge freigeschaltet", 5f);
        }
    }

    [HarmonyPatch(typeof(PlayerCareer), nameof(PlayerCareer.IsPlaythroughLockedOutOfAchievements), MethodType.Getter)]
    internal static class AchievementTools_LockPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Settings.B("AchievementsOn")) return true;
            __result = false;
            return false;
        }
    }
}

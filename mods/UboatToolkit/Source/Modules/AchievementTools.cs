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
        private static readonly FieldInfo LockField = AccessTools.Field(typeof(PlayerCareer), "isPlaythroughLockedOutOfAchievements");
        public override void Tick()
        {
            if (Settings.B("AchievementsOn"))
            {
                // Searching all objects every second cost 40-46 ms (part of the lag). The career is cached.
                var pc = Util.Career;
                if (pc != null && LockField != null && (bool)LockField.GetValue(pc))
                {
                    LockField.SetValue(pc, false);
                    Util.Log("[Erfolge] Sperre im Spielstand entfernt (Konsole war benutzt)");
                }
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

    // Diagnosis: the game only tracks achievements after this check passes (polled every 5 s). In DoMe's last two
    // sessions it never passed (no "Starting N achievement objects" line), so no achievement could be earned.
    // Log once per minute which condition is missing, and log every real unlock.
    [HarmonyPatch(typeof(AchievementManager), "ListenForSteamDownload")]
    internal static class AchievementTools_StartDiag
    {
        private static float next;
        private static void Postfix(float __result)
        {
            if (!float.IsNaN(__result)) { if (Time.unscaledTime < next) return; next = Time.unscaledTime + 60f; }
            string why = "";
            Util.Safe("AchDiag", () =>
            {
                var st = AccessTools.TypeByName("Steamworks.SteamUserStats"); object steam = st != null ? st.GetMethod("RequestCurrentStats", Type.EmptyTypes)?.Invoke(null, null) : "?";
                var com = Traverse.Create(typeof(AchievementManager)).Field("campaignObjectivesManager").GetValue();
                int objectives = com != null ? Traverse.Create(com).Property("List").Property("Count").GetValue<int>() : -1;
                var wp = Traverse.Create(typeof(AchievementManager)).Field("wolfpack").GetValue() as UnityEngine.Object;
                why = "Steam-Statistik=" + steam + ", Kampagnenziele=" + objectives + ", Wolfpack=" + (wp != null);
            });
            Util.Log(float.IsNaN(__result) ? "[Erfolge] Erfolgs-Prüfung des Spiels läuft (" + why + ")" : "[Erfolge] Spiel prüft Erfolge noch NICHT: " + why);
        }
    }

    [HarmonyPatch(typeof(AchievementManager), "OnAchievementComplete")]
    internal static class AchievementTools_UnlockLog
    {
        private static void Postfix(IAchievement achievement) =>
            Util.Log("[Erfolge] freigeschaltet: " + achievement?.Name + " (gesperrt=" + (Util.Career != null && Util.Career.IsPlaythroughLockedOutOfAchievements) + ")");
    }
}

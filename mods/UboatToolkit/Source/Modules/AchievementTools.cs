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
            Type stats = UboatToolkit.Util.FindType("Steamworks.SteamUserStats");
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
            // While the console runs a command it must see "already locked", else it asks for the confirmation again
            // and again and never runs the command (DoMe 2026-11-16). The save's real flag stays untouched.
            __result = AchievementTools_ConsolePatch.InSubmit;
            return false;
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.DevConsole.DevConsole), "Submit", new[] { typeof(string), typeof(bool) })]
    internal static class AchievementTools_ConsolePatch
    {
        internal static bool InSubmit;
        private static void Prefix() { InSubmit = true; }
        private static Exception Finalizer(Exception __exception) { InSubmit = false; return __exception; }
    }

    // One-shot cheats for the current assignment (DoMe 2026-11-16: "Mission per Knopfdruck erfolgreich").
    // Same calls as the game's console command "Mission Stage", but without the console, so nothing is flagged.
    public class MissionCheat : ModuleBase
    {
        private static readonly string[] homeStages = { "Report", "Return", "ReturnToPort", "DockInPort", "DockAtDestinationPort", "FinishingTask", "ReportToOfficer" };
        public override void Tick()
        {
            var done = Settings.Get("CheatMissionDone");
            if (done != null && done.On) { done.Value = 0; Util.Safe("MissionDone", () => Run(true)); }
            var step = Settings.Get("CheatMissionStep");
            if (step != null && step.On) { step.Value = 0; Util.Safe("MissionStep", () => Run(false)); }
        }

        private static void Run(bool finish)
        {
            var m = Util.Career == null ? null : Util.Career.CurrentAssignment as UBOAT.Game.Sandbox.Missions.Mission;
            if (m == null) { ToolkitRunner.ShowNotice("Kein laufender Auftrag", 4f); return; }
            // names of the assignment's stages (enum "Stages" of the class or one of its base classes)
            string[] names = new string[0];
            for (var t = m.GetType(); t != null && names.Length == 0; t = t.BaseType)
            {
                var e = t.GetNestedType("Stages", BindingFlags.Public | BindingFlags.NonPublic);
                if (e != null && e.IsEnum) names = Enum.GetNames(e);
            }
            int stage = m.Stage, target = stage;
            if (!finish) target = stage + 1;
            else
                for (int i = stage; i < names.Length; i++)
                    if (Array.IndexOf(homeStages, names[i]) >= 0) { target = i; break; }
            if (names.Length > 0 && target > names.Length - 1) target = names.Length - 1;
            string from = stage < names.Length ? names[stage] : stage.ToString();
            if (target > stage) m.SetDebugStage(target);
            if (finish)
            {
                var p = m.GetType().GetProperty("AssignmentObjectiveState", BindingFlags.Public | BindingFlags.Instance);
                if (p != null && p.CanWrite) p.SetValue(m, UBOAT.Game.Sandbox.Missions.AssignmentObjectiveState.Completed, null);
            }
            string to = m.Stage < names.Length ? names[m.Stage] : m.Stage.ToString();
            Util.Log("[Auftrag] " + m.GetType().Name + ": Schritt " + from + " -> " + to + (finish ? ", Ziel als erfüllt gesetzt" : ""));
            ToolkitRunner.ShowNotice(finish ? "Auftrag als erfüllt gesetzt (Schritt: " + to + "). Im Heimathafen abgeben." : "Auftrag: Schritt " + from + " -> " + to, 6f);
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
                var st = UboatToolkit.Util.FindType("Steamworks.SteamUserStats"); object steam = st != null ? st.GetMethod("RequestCurrentStats", Type.EmptyTypes)?.Invoke(null, null) : "?";
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

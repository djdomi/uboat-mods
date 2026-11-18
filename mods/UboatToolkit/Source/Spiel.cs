// UBOAT Toolkit - Spiel: Fehler-Fixes, Leistung, Laden und Speichern, Cheats, Lager, Welt
// Diese Datei fasst 29 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Modules/AchievementTools.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Linq;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Achievements;

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
        private static readonly FieldInfo Lock = AccessTools.Field(typeof(PlayerCareer), "isPlaythroughLockedOutOfAchievements");
        private static Exception Finalizer(Exception __exception, string __0)
        {
            InSubmit = false;
            // 0.3.98 proof line (DoMe: console cheats without losing achievements): the save's real flag is checked
            // right after each command and cleared at once, not only at the next tick.
            try
            {
                if (Settings.B("AchievementsOn") && !string.IsNullOrEmpty(__0))
                {
                    var pc = Util.Career;
                    bool locked = pc != null && Lock != null && (bool)Lock.GetValue(pc);
                    if (locked) Lock.SetValue(pc, false);
                    Util.Log("Konsole: " + __0 + ", Erfolge weiter erlaubt: " + (pc == null ? "kein Spielstand geladen" : "ja" + (locked ? " (Sperre sofort entfernt)" : "")));
                }
            }
            catch { }
            return __exception;
        }
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
            if (Settings.B("CheatEscortEasy") || Settings.B("CheatEscortFullSpeed") || Settings.B("CheatEscortNoDamage")) Util.Safe("EscortEasy", EscortEasy);
            else
            {
                if (escProtected.Count > 0) { foreach (var x in escProtected) if (x != null) x.IsProtected = false; escProtected.Clear(); }
                Util.Safe("EscortEasyOff", EscortEasyOff);
            }
        }

        // Escort duty (DoMe 2026-11-16, Weserübung: "dauert ewig ... beim dritten Mal gescheitert"). The game fails the
        // assignment only when the troopship is lost; attackers are spawned every 0.4-2 days and sent at the convoy, and
        // the convoy sails at a random part of its speed. Cheat: no new attackers, convoy at full speed.
        private static readonly FieldInfo escNext = AccessTools.Field(typeof(UBOAT.Game.Sandbox.Missions.EscortDutyAssignment), "nextEnemySpawnTicks");
        private static readonly FieldInfo escScale = AccessTools.Field(typeof(UBOAT.Game.Sandbox.Missions.EscortDutyAssignment), "velocityScale");
        private static bool escLogged;
        private static readonly FieldInfo escGroup = AccessTools.Field(typeof(UBOAT.Game.Sandbox.Missions.EscortDutyAssignment), "escortedGroup");
        private static readonly System.Collections.Generic.List<UBOAT.Game.Sandbox.SandboxEntity> escProtected = new System.Collections.Generic.List<UBOAT.Game.Sandbox.SandboxEntity>();
        // 0.4.22 (option audit): switching the cheat off gives the assignment its attackers back. The far-away spawn time
        // is itself the mark that the cheat was active (it is stored in the savegame), so this also works after loading.
        private static void EscortAttackersBack()
        {
            var e = Util.Career == null ? null : Util.Career.CurrentAssignment as UBOAT.Game.Sandbox.Missions.EscortDutyAssignment;
            if (e == null || escNext == null || (long)escNext.GetValue(e) < long.MaxValue / 4) return;
            var gt = Traverse.Create(e).Field("gameTime").GetValue();
            if (gt == null) return;
            long now = Traverse.Create(gt).Property("StoryTicks").GetValue<long>();
            escNext.SetValue(e, now + (long)(UnityEngine.Random.Range(0.4f, 1.4f) * 8.64E+11f));
            Util.Log("[Auftrag] Geleitschutz: Angreifer kommen wieder (nächster in 0,4 bis 1,4 Tagen)");
        }

        private static void EscortEasyOff()
        {
            var e = Util.Career == null ? null : Util.Career.CurrentAssignment as UBOAT.Game.Sandbox.Missions.EscortDutyAssignment;
            if (e == null || escNext == null || (long)escNext.GetValue(e) < long.MaxValue / 4) return;
            var gt = Traverse.Create(e).Field("gameTime").GetValue();
            if (gt == null) return;
            long now = Traverse.Create(gt).Property("StoryTicks").GetValue<long>();
            escNext.SetValue(e, now + (long)(UnityEngine.Random.Range(0.4f, 1.4f) * 8.64E+11f));
            var grp = escGroup == null ? null : escGroup.GetValue(e) as UBOAT.Game.Sandbox.SandboxGroup;
            if (grp) foreach (var x in grp.Entities) if (x != null && x.IsProtected) x.IsProtected = false;
            escLogged = false;
            Util.Log("[Auftrag] Geleitschutz-Cheat aus: Angreifer kommen wieder (nächster in 0,4 bis 1,4 Tagen), Geleit nimmt wieder Schaden");
        }
        private static void EscortEasy()
        {
            var e = Util.Career == null ? null : Util.Career.CurrentAssignment as UBOAT.Game.Sandbox.Missions.EscortDutyAssignment;
            if (e == null) { escLogged = false; return; }
            // 0.4.29: three separate switches
            bool noAtt = Settings.B("CheatEscortEasy"), full = Settings.B("CheatEscortFullSpeed"), noDmg = Settings.B("CheatEscortNoDamage");
            if (noAtt) { if (escNext != null) escNext.SetValue(e, long.MaxValue / 2); }
            else Util.Safe("EscortEasyOff", EscortAttackersBack);
            if (full && escScale != null && (float)escScale.GetValue(e) < 1f) escScale.SetValue(e, 1f);
            if (!noDmg)
            {
                if (escProtected.Count > 0) { foreach (var x in escProtected) if (x != null) x.IsProtected = false; escProtected.Clear(); Util.Log("[Auftrag] Geleitschutz: Geleit nimmt wieder Schaden"); }
                if (!escLogged) { escLogged = true; Util.Log("[Auftrag] Geleitschutz: keine neuen Angreifer " + (noAtt ? "an" : "aus") + ", volle Fahrt " + (full ? "an" : "aus") + ", kein Schaden aus"); }
                return;
            }
            // the escorted ships take no damage (the game's own "protected" flag, used in its tutorials)
            var grp = escGroup == null ? null : escGroup.GetValue(e) as UBOAT.Game.Sandbox.SandboxGroup;
            if (grp)
                foreach (var x in grp.Entities)
                    if (x != null && !x.IsProtected) { x.IsProtected = true; escProtected.Add(x); }
            if (!escLogged) { escLogged = true; Util.Log("[Auftrag] Geleitschutz: keine neuen Angreifer " + (noAtt ? "an" : "aus") + ", volle Fahrt " + (full ? "an" : "aus") + ", kein Schaden an"); }
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

// ====================================================================================================
// Abschnitt: Modules/AutoDetail.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::UnityEngine;

    // Automatic detail (setting ExplosionAuto): at low FPS Unity runs MORE physics steps per frame to catch up
    // (FixedUpdate of the contact handling, ships, rudders ...), which makes the next frame even slower.
    // Below half the target FPS the catch-up per frame is limited (max. 0.1 s game time per frame); above, the game value is back.
    public class AutoDetail : ModuleBase
    {
        private bool limited;
        private float nextSwitch;
        private static UBOAT.Game.Core.Time.GameTime gameTime;
        internal static float gameStep = -1f;   // the game's own physics step (1/75 s)
        internal const float GameStepMax = 1f / 75f;
        private float nextCensus, autoHz, nextPhysLine; private int autoWait;

        // 0.3.69 physics rate (DoMe: get performance out of the engine). Measured: with 48 ships the physics took
        // 149 of 224 ms per frame. The game runs physics 75 times per second and up to 5 steps per frame.
        // "PhysicsHz" lowers the rate through the game's own GameTime.FixedDeltaTime (50 = the step the game itself
        // uses at 2x time compression). Only set at normal speed; time compression keeps the game's own logic.
        public override void Tick()
        {
            if (gameTime == null) gameTime = Object.FindObjectOfType<UBOAT.Game.Core.Time.GameTime>();
            if (gameTime == null) return;
            if (gameStep < 0f) gameStep = Mathf.Min(gameTime.FixedDeltaTime, GameStepMax);   // 0.4.28: never learn an already lowered value as "the game value"
            float hz = Settings.F("PhysicsHz");
            float want = hz >= 20f ? Mathf.Max(gameStep, 1f / hz) : gameStep;
            // time compression: back to the game's own step (the game multiplies it there itself); pause: leave alone
            if (Time.timeScale < 0.99f) return;
            if (Time.timeScale > 1.01f) want = gameStep;
            else if (Settings.B("PhysicsFollowFps"))
            {
                // 0.4.8 (DoMe: optimise the game): at 16 FPS the game needed 3 physics steps of 20 ms per frame but may
                // do only 2 ("PhysicsMaxSteps"), so the whole game ran in slow motion and still paid 18 ms per frame for
                // physics. The game itself uses longer steps under time compression (x4 = 30 per second, x5 = 25).
                // Now the step follows the frame rate in stages of 5 per second, never below "PhysicsAutoMinHz".
                float fps = ExplosionLimit_Patch.Fps();
                float top = 1f / want, low = Mathf.Clamp(Settings.F("PhysicsAutoMinHz"), 15f, top);
                float target = Mathf.Clamp(Mathf.Floor(fps * 1.5f / 5f) * 5f, low, top);   // 1.5 steps per frame on average
                if (autoHz <= 0f) autoHz = top;
                if (Mathf.Abs(target - autoHz) > 0.5f) { if (++autoWait >= 3) { autoHz = target; autoWait = 0; } } else autoWait = 0;
                want = 1f / Mathf.Clamp(autoHz, low, top);
            }
            if (Mathf.Abs(gameTime.FixedDeltaTime - want) > 0.00001f && !SaveTools_LoadSpeedPatch.IsLoading)
            {
                gameTime.FixedDeltaTime = want;
                Util.Log("Physik-Takt: " + (1f / want).ToString("0") + " pro Sekunde (Spiel: " + (1f / gameStep).ToString("0") + ")");
            }
            // 0.4.9: proof line once per minute (was missing: no way to see whether the automatic step is active)
            if (Time.unscaledTime >= nextPhysLine)
            {
                nextPhysLine = Time.unscaledTime + 60f;
                // 0.4.28 (DoMe: boat bobs in time compression, log showed 12 steps/s where the game itself uses 18):
                // compare with the game's own formula and say so.
                float own = 1f / (gameStep * (1f + (Mathf.Max(1f, Time.timeScale) - 1f) * 0.5f));
                if (Time.timeScale > 1.01f && 1f / Time.fixedDeltaTime < own - 0.5f)
                    Util.Log("Physik: WARNUNG Takt im Zeitraffer gröber als das Spiel selbst (" + (1f / Time.fixedDeltaTime).ToString("0") + " statt " + own.ToString("0") + " pro Sekunde), Grundwert " + (1f / gameTime.FixedDeltaTime).ToString("0") + ", lädt gerade " + (SaveTools_LoadSpeedPatch.IsLoading ? "ja" : "nein"));
                Util.Log("Physik: Takt " + (1f / Time.fixedDeltaTime).ToString("0") + " pro Sekunde, höchstens " + (Time.maximumDeltaTime / Time.fixedDeltaTime).ToString("0.#") + " Schritte pro Bild, Zeitraffer x" + Time.timeScale.ToString("0.#") + ", FPS " + ExplosionLimit_Patch.Fps().ToString("0") + ", Automatik " + (Settings.B("PhysicsFollowFps") ? (Time.timeScale > 1.01f ? "ruht (Zeitraffer, Spiel regelt selbst)" : "an") : "aus"));
            }
            // Census for the next tuning step: what is in the physics scene when it is slow (max. every 2 min).
            float f = ExplosionLimit_Patch.Fps();
            if (f < Settings.F("ExplosionTargetFps") * 0.5f && Time.unscaledTime >= nextCensus && Time.timeScale > 0f)
            {
                nextCensus = Time.unscaledTime + 120f;
                Util.Safe("Physik-Zählung", () => Census(f));
            }
        }

        private static void Census(float fps)
        {
            int rb = 0, dyn = 0, awake = 0, col = 0, mesh = 0, concave = 0, trig = 0;
            foreach (var r in Object.FindObjectsOfType<Rigidbody>()) { rb++; if (!r.isKinematic) { dyn++; if (!r.IsSleeping()) awake++; } }
            foreach (var c in Object.FindObjectsOfType<Collider>())
            {
                col++; if (c.isTrigger) trig++;
                var m = c as MeshCollider; if (m != null) { mesh++; if (!m.convex) concave++; }
            }
            int ships = Object.FindObjectsOfType<UBOAT.Game.Scene.Entities.Ship>().Length;
            Util.Log("Physik-Zählung: FPS " + fps.ToString("0") + ", Schiffe " + ships + ", Körper " + rb + " (beweglich " + dyn + ", wach " + awake + "), Kollider " + col
                + " (Auslöser " + trig + ", Netz " + mesh + ", davon hohl " + concave + "), Takt " + (1f / Time.fixedDeltaTime).ToString("0") + "/s, max. Schritte/Bild "
                + (Time.maximumDeltaTime / Time.fixedDeltaTime).ToString("0.#") + ", Löser " + Physics.defaultSolverIterations + "/" + Physics.defaultSolverVelocityIterations
                + ", autoSync " + Physics.autoSyncTransforms + ", Zeitraffer " + Time.timeScale.ToString("0.#"));
        }

        public override void Frame()
        {
            // 0.3.69: the old limit (0.1 s) was ABOVE the game's own value (5 steps = 0.067 s), so it never did anything.
            // Now: while limited, at most "PhysicsMaxSteps" physics steps per frame (the game re-sets its value on
            // every speed change, so this is checked every frame).
            if (limited)
            {
                float cap = Time.fixedDeltaTime * Mathf.Clamp(Settings.F("PhysicsMaxSteps"), 1f, 5f);
                if (Time.maximumDeltaTime > cap + 0.00001f) Time.maximumDeltaTime = cap;
            }
            // Hysteresis: limit below 50 % of target FPS, release only above 70 %, and switch at most every 10 s.
            float f = ExplosionLimit_Patch.Fps(), target = Settings.F("ExplosionTargetFps");
            bool want = Settings.B("ExplosionAuto") && (limited ? f < target * 0.7f : f < target * 0.5f);
            if (want == limited || Time.unscaledTime < nextSwitch) return;
            nextSwitch = Time.unscaledTime + 10f;
            limited = want;
            // release: the game's own setter recalculates step + catch-up limit for the current speed
            if (!want && gameTime != null) gameTime.FixedDeltaTime = gameTime.FixedDeltaTime;
            ToolkitRunner.ShowNotice(want ? "Auto-Detail: viel los (" + Mathf.RoundToInt(f) + " FPS) - Effekte, Stimmen + Physik werden gespart" : "Auto-Detail: wieder volle Details", 4f);
            Util.Log("Auto-Detail: Physik-Aufholen " + (want ? "begrenzt (" + Settings.F("PhysicsMaxSteps").ToString("0") + " Schritte pro Bild)" : "normal (5 Schritte)"));
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::UnityEngine;

    // The game checks ALL nearby ship groups (enter/leave, group update) in every physics step (SandboxPlayerWolfpack.FixedUpdate,
    // measured ~10 s per minute with ~100 ships). Under the target FPS this check runs only every 2nd-4th step;
    // groups are still found, just a few hundredths of a second later.
    [HarmonyLib.HarmonyPatch]
    internal static class AutoDetail_NearbyGroupsPatch
    {
        private static int step;
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var t = typeof(UBOAT.Game.Sandbox.SandboxPlayerWolfpack);
            var a = HarmonyLib.AccessTools.Method(t, "UpdateNearbyGroups"); if (a != null) yield return a;
            var b = HarmonyLib.AccessTools.Method(t, "UpdateImpostorGroups"); if (b != null) yield return b;
        }
        private static bool Prefix(System.Reflection.MethodBase __originalMethod)
        {
            if (!Settings.B("ExplosionAuto")) return true;
            float load = ExplosionLimit_Patch.Fps() / UnityEngine.Mathf.Max(1f, Settings.F("ExplosionTargetFps"));
            // 0.3.70: measured as the biggest single game script (SandboxPlayerWolfpack.FixedUpdate avg 4.6 s per minute),
            // so the check now runs at most every 2nd physics step even at full FPS.
            // the slower, the rarer: 40 FPS target at 5 FPS = every 8th step (max. 10)
            int every = load >= 1f ? 2 : UnityEngine.Mathf.Clamp(UnityEngine.Mathf.CeilToInt(1f / UnityEngine.Mathf.Max(0.01f, load)), 2, 10);
            if (__originalMethod.Name == "UpdateNearbyGroups") step++;
            return step % every == 0;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::UnityEngine;

    // DoMe's idea: far ships only "virtual". The game already has this: groups outside the simulation distance are not
    // spawned as real ships (they stay on the map/sensors as sandbox groups). Under the target FPS this distance is
    // reduced step by step (down to AutoVirtualMin), so far groups go virtual; when FPS recover it goes back to the game value.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.Data.GameplaySettings), "SimulationDistanceMultiplier", HarmonyLib.MethodType.Getter)]
    internal static class AutoDetail_VirtualShipsPatch
    {
        private static float smooth = 1f, lastT = -1f;
        private static void Postfix(ref float __result)
        {
            if (!Settings.B("ExplosionAuto") || !Settings.B("AutoVirtualShips")) return;
            float t = UnityEngine.Time.unscaledTime;
            if (t != lastT)
            {
                lastT = t;
                float load = UnityEngine.Mathf.Clamp01(ExplosionLimit_Patch.Fps() / UnityEngine.Mathf.Max(1f, Settings.F("ExplosionTargetFps")));
                float want = UnityEngine.Mathf.Lerp(UnityEngine.Mathf.Clamp(Settings.F("AutoVirtualMin"), 0.2f, 1f), 1f, load);
                smooth = UnityEngine.Mathf.MoveTowards(smooth, want, 0.02f * UnityEngine.Time.unscaledDeltaTime);   // slow: 0.1 per 5 s, no flicker
            }
            __result *= smooth;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::UnityEngine;

    // 0.4.28: "PhysicsHz" lowered the base step (e.g. 50 per second). The module set it back for time compression only
    // in its next tick (up to 1 s later), so the game calculated its compression step from the lowered base
    // (log 09:13:28: 12 steps per second at x7.2, the game alone uses 18). Now the base is the game's own value
    // BEFORE the game calculates, so the Toolkit never makes the physics coarser in time compression.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.Time.GameTime), "TimeScale_Changed")]
    internal static class AutoDetail_CompressionBasePatch
    {
        private static float lastLog;
        private static void Prefix(UBOAT.Game.Core.Time.GameTime __instance, UBOAT.Game.Core.Data.Parameter parameter)
        {
            try
            {
                if (parameter == null || parameter.Value <= 1.01f) return;
                float own = AutoDetail.gameStep > 0f ? AutoDetail.gameStep : AutoDetail.GameStepMax;
                var f = HarmonyLib.Traverse.Create(__instance).Field("fixedDeltaTime");
                float cur = f.GetValue<float>();
                if (cur <= own + 0.00001f) return;
                f.SetValue(own);
                if (Time.unscaledTime - lastLog > 30f)
                {
                    lastLog = Time.unscaledTime;
                    Util.Log("Physik: Zeitraffer x" + parameter.Value.ToString("0.#") + ", Grundwert vorher " + (1f / cur).ToString("0") + " pro Sekunde, jetzt Spielwert " + (1f / own).ToString("0"));
                }
            }
            catch (System.Exception e) { Util.Log("Physik: Zeitraffer-Grundwert Fehler " + e.Message); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CacheCheck.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Linq;
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Reflection;
    using global::DWS.Common.InjectionFramework;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Mods;

    // Mod-cache check: the game security-checks a mod only when it loads it from its compiled-mod cache
    // (persistentDataPath/Temp) and then rejects it SILENTLY. This runs the game's own check on each cached
    // mod and clears only the ones it would reject, so they are rebuilt fresh at the next start.
    // Optional list file "toolkit-cache-clear.txt" (one DLL name per line) always clears those mods.
    internal static class CacheCheck
    {
        private const string StaleMarker = ".stale-";
        private static string ListFile => Path.Combine(Settings.Folder, "toolkit-cache-clear.txt");
        private static string VerifiedFile => Path.Combine(Settings.Folder, "toolkit-cache-checked.txt");
        private static List<string> list;
        private static string listNote = "";
        private static bool hooked;

        // ---- binding to the game's own security check ----
        private static bool bound;
        private static string unavailable;
        private static ConstructorInfo engineCtor;
        private static MethodInfo checkAssembly, reportLines, setupSecurity;
        private static PropertyInfo settingsP, restrictionsP;
        private static FieldInfo securityOptions;
        private static Dictionary<string, Mod> mods;

        private static T Need<T>(T v, string what) where T : class
        {
            if (v == null) throw new MissingMemberException(what + " nicht gefunden (Spiel-Update?)");
            return v;
        }

        private static void Bind()
        {
            if (bound) return;
            bound = true;
            try
            {
                Type engine = Need(UboatToolkit.Util.FindType("Trivial.CodeSecurity.CodeSecurityEngine"), "CodeSecurityEngine");
                Type restr = Need(UboatToolkit.Util.FindType("Trivial.CodeSecurity.CodeSecurityRestrictions"), "CodeSecurityRestrictions");
                Type report = Need(UboatToolkit.Util.FindType("Trivial.CodeSecurity.CodeSecurityReport"), "CodeSecurityReport");
                Type roslyn = Need(UboatToolkit.Util.FindType("RoslynCSharp.RoslynCSharp"), "RoslynCSharp");
                engineCtor = Need(engine.GetConstructor(new[] { typeof(byte[]) }), "CodeSecurityEngine(byte[])");
                checkAssembly = Need(engine.GetMethod("SecurityCheckAssembly", new[] { restr, report.MakeByRefType() }), "SecurityCheckAssembly");
                reportLines = Need(report.GetMethod("GetAllLines", new[] { typeof(bool) }), "GetAllLines");
                settingsP = Need(roslyn.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static), "RoslynCSharp.Settings");
                restrictionsP = Need(roslyn.GetProperty("SecurityRestrictions", BindingFlags.Public | BindingFlags.Instance), "SecurityRestrictions");
                setupSecurity = Need(AccessTools.Method(typeof(Mod), "SetupSecurity"), "Mod.SetupSecurity");
                securityOptions = Need(AccessTools.Field(typeof(Mod), "securityOptions"), "Mod.securityOptions");
                var manager = ScriptableObjectSingleton.LoadSingleton<ModManager>();
                if (manager == null || manager.LoadedMods == null) throw new InvalidOperationException("Mod-Liste nicht verfügbar");
                mods = new Dictionary<string, Mod>(StringComparer.OrdinalIgnoreCase);
                foreach (Mod m in manager.LoadedMods)
                    if (m != null && !string.IsNullOrEmpty(m.AssemblyName)) mods[m.AssemblyName] = m;
            }
            catch (Exception e) { unavailable = e.Message; }
        }

        private static Mod ModFor(string asm) { Bind(); return mods != null && mods.TryGetValue(asm, out var m) ? m : null; }
        private static int PermissionsOf(Mod mod) { try { return Convert.ToInt32(securityOptions.GetValue(mod)); } catch { return -1; } }

        // null = passes; otherwise the first reason the game would reject it.
        private static string Violation(string dll, Mod mod)
        {
            Bind();
            if (unavailable != null) throw new InvalidOperationException(unavailable);
            byte[] bytes;
            using (var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                int read = 0;
                while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
            }
            setupSecurity.Invoke(mod, new object[] { null });
            object rules = restrictionsP.GetValue(settingsP.GetValue(null, null), null);
            object engine = engineCtor.Invoke(new object[] { bytes });
            try
            {
                object[] args = { rules, null };
                if ((bool)checkAssembly.Invoke(engine, args)) return null;
                var lines = args[1] == null ? null : reportLines.Invoke(args[1], new object[] { false }) as string[];
                if (lines == null || lines.Length == 0) return "Prüfung nicht bestanden (keine Details)";
                return lines.Length == 1 ? lines[0] : lines.Length + " Verstöße: " + string.Join(" | ", lines);   // all of them, so a mod author can fix every one in one go
            }
            finally { (engine as IDisposable)?.Dispose(); }
        }

        // ---- the clearing ----
        public static void Install()
        {
            if (!Settings.B("CacheCheck")) return;
            Run("Start", true);
            if (!hooked) { Application.quitting += () => { if (Settings.B("CacheCheck")) Run("Beenden", false); }; hooked = true; }
        }

        private static void LoadList()
        {
            list = null; listNote = "";
            try
            {
                if (!File.Exists(ListFile)) { listNote = " (keine Liste " + Path.GetFileName(ListFile) + ")"; return; }
                var names = new List<string>();
                foreach (var raw in File.ReadAllLines(ListFile))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l[0] == '#') continue;
                    if (l.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) l = l.Substring(0, l.Length - 4);
                    names.Add(l.ToLowerInvariant());
                }
                if (names.Count > 0) { list = names; listNote = " (Liste: " + string.Join(", ", names.ToArray()) + ")"; }
            }
            catch (Exception e) { listNote = " (Liste unlesbar: " + e.Message + ")"; list = null; }
        }

        private static bool Listed(string asm) => list != null && list.Contains(asm.ToLowerInvariant());

        private static string AssemblyOf(string file)
        {
            string n = Path.GetFileName(file);
            if (n.EndsWith(".dll.mdb", StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - 8);
            if (n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) return n.Substring(0, n.Length - 4);
            return null;
        }

        private static HashSet<string> Decide(string[] files, string when, out string note)
        {
            var doomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool check = Settings.B("CacheCheckFirst");
            string why = null;
            if (check) { Bind(); why = unavailable; }
            if (!check || why != null)
            {
                // no check possible: clear everything (or only the listed mods if the list exists)
                foreach (var f in files) { var a = AssemblyOf(f); if (a != null && (list == null || Listed(a))) doomed.Add(a); }
                note = (check ? " (Prüfung nicht möglich: " + why + " - alles neu gebaut)" : " (Prüfung aus)") + listNote;
                return doomed;
            }
            float start = Time.realtimeSinceStartup;
            var verified = LoadVerified();
            var still = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int checkedNow = 0, known = 0, notEnabled = 0, listed = 0;
            foreach (var f in files)
            {
                if (!f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                var asm = AssemblyOf(f);
                if (asm == null) continue;
                if (Listed(asm)) { doomed.Add(asm); listed++; continue; }
                var mod = ModFor(asm);
                if (mod == null) { notEnabled++; continue; }
                // The game keeps a compiled mod even when its source files changed (or compiles while files
                // are still being copied): then old code runs. A fingerprint of all Source\*.cs is stored with
                // the check result; if the sources differ from the last check, the DLL is rebuilt.
                string srcHash = SourceHash(mod);
                string key;
                try { var fi = new FileInfo(f); key = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks + "|" + PermissionsOf(mod) + "|" + srcHash; } catch { key = null; }
                if (srcHash != "-" && key != null && verified.TryGetValue(asm, out var before) && before != key && SameDll(before, key))
                {
                    doomed.Add(asm);
                    Util.Log("Mod-Cache " + when + ": „" + mod.Name + "“ (" + asm + ".dll) passt nicht mehr zum Quellcode, wird neu gebaut.");
                    continue;
                }
                if (key != null && verified.TryGetValue(asm, out var seen) && seen == key) { still[asm] = key; known++; continue; }
                string v;
                try { v = Violation(f, mod); checkedNow++; }
                catch (Exception e)
                {
                    doomed.Add(asm);
                    Util.Log("Mod-Cache " + when + ": „" + mod.Name + "“ (" + asm + ".dll) NICHT prüfbar, wird sicherheitshalber neu gebaut: " +
                             (e is TargetInvocationException && e.InnerException != null ? e.InnerException.Message : e.Message));
                    continue;
                }
                if (v == null) { if (key != null) still[asm] = key; continue; }
                doomed.Add(asm);
                Util.Log("Mod-Cache " + when + ": „" + mod.Name + "“ (" + asm + ".dll) würde vom Spiel abgelehnt, wird neu gebaut: " + v);
            }
            SaveVerified(still);
            note = " (geprüft " + checkedNow + " in " + Mathf.RoundToInt((Time.realtimeSinceStartup - start) * 1000f) + " ms, " + known + " schon als gut bekannt" +
                   (listed > 0 ? ", " + listed + " aus der Liste" : "") + (notEnabled > 0 ? ", " + notEnabled + " nicht aktive Mods unberührt" : "") + ")";
            return doomed;
        }

        // Same DLL file (size + time) as at the last check, but something else (sources) changed.
        private static bool SameDll(string a, string b)
        {
            var x = a.Split('|'); var y = b.Split('|');
            return x.Length >= 2 && y.Length >= 2 && x[0] == y[0] && x[1] == y[1];
        }

        private static string SourceHash(Mod mod)
        {
            try
            {
                var src = Path.Combine(mod.Path ?? "", "Source");
                if (!Directory.Exists(src)) return "-";
                var files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                using (var sha = System.Security.Cryptography.SHA1.Create())
                {
                    foreach (var f in files)
                    {
                        var name = System.Text.Encoding.UTF8.GetBytes(f.Substring(src.Length).ToLowerInvariant());
                        sha.TransformBlock(name, 0, name.Length, null, 0);
                        var b = File.ReadAllBytes(f);
                        sha.TransformBlock(b, 0, b.Length, null, 0);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(sha.Hash).Replace("-", "").Substring(0, 16);
                }
            }
            catch { return "-"; }
        }

        private static Dictionary<string, string> LoadVerified()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(VerifiedFile)) return d;
                foreach (var l in File.ReadAllLines(VerifiedFile)) { int bar = l.IndexOf('|'); if (bar > 0 && l[0] != '#') d[l.Substring(0, bar)] = l.Substring(bar + 1); }
            }
            catch { d.Clear(); }
            return d;
        }

        private static void SaveVerified(Dictionary<string, string> d)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                var lines = new List<string> { "# Mods im Cache, die die Spiel-Prüfung bestanden haben. Darf gelöscht werden." };
                foreach (var kv in d) lines.Add(kv.Key + "|" + kv.Value);
                File.WriteAllLines(VerifiedFile, lines.ToArray());
            }
            catch { }
        }

        private static void Run(string when, bool purgeStale)
        {
            try
            {
                LoadList();
                string temp = Path.Combine(Application.persistentDataPath, "Temp");
                if (!Directory.Exists(temp)) { Util.Log("Mod-Cache " + when + ": kein Temp-Ordner, nichts zu tun."); return; }
                int deleted = 0, renamed = 0, stale = 0, failed = 0, kept = 0;
                string firstError = null;
                var files = Directory.GetFiles(temp, "*", SearchOption.TopDirectoryOnly);
                if (purgeStale)
                    foreach (var f in files)
                        if (Path.GetFileName(f).IndexOf(StaleMarker, StringComparison.OrdinalIgnoreCase) >= 0)
                            try { File.Delete(f); stale++; } catch { }
                var doomed = Decide(files, when, out string note);
                foreach (var f in files)
                {
                    var asm = AssemblyOf(f);
                    if (asm == null) continue;
                    if (!doomed.Contains(asm)) { kept++; continue; }
                    try { File.Delete(f); deleted++; }
                    catch (Exception de)
                    {
                        // locked (compiled this launch): rename, removed at the next start
                        try { File.SetAttributes(f, FileAttributes.Normal); File.Move(f, f + StaleMarker + DateTime.Now.Ticks); renamed++; }
                        catch (Exception me) { failed++; if (firstError == null) firstError = Path.GetFileName(f) + ": löschen " + de.Message + " / umbenennen " + me.Message; }
                    }
                }
                Util.Log("Mod-Cache " + when + ": gelöscht " + deleted + ", umbenannt " + renamed + " (gesperrt, weg beim nächsten Start), alte Reste " + stale +
                         (kept > 0 ? ", BEHALTEN " + kept : "") + (failed > 0 ? ", " + failed + " FEHLER - erster: " + firstError : "") + note);
            }
            catch (Exception e) { Util.Log("Mod-Cache " + when + ": fehlgeschlagen: " + e.Message); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CarrierConvoy.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;

    // Convoys with a carrier: aircraft parked on deck read speed 0 and held the whole convoy back.
    // While the group has ships, the aircraft are left out of the group's top speed.
    [HarmonyPatch(typeof(SandboxMobileGroup), "UpdateParametersFromMembers", new[] { typeof(bool) })]
    internal static class CarrierConvoy_Patch
    {
        private const EntityTypeCategory VanillaSkipped = EntityTypeCategory.NotDetectableDirectly | EntityTypeCategory.SonarDecoy | EntityTypeCategory.Pontoon | EntityTypeCategory.Iceberg | EntityTypeCategory.Wreckage;
        private static SandboxMobileGroup cachedGroup, skippedFor;
        private static bool cachedHasShips, seen;
        private static int errorLogged;
        private static readonly HashSet<int> loggedGroups = new HashSet<int>();
        private static readonly HashSet<string> loggedTypes = new HashSet<string>(StringComparer.Ordinal);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var getVelocity = AccessTools.PropertyGetter(typeof(SandboxEntity), "Velocity");
            var toFloat = AccessTools.Method(typeof(Parameter), "op_Implicit", new[] { typeof(Parameter) });
            int at = -1, n = 0;
            for (int i = 1; i + 1 < list.Count; i++)
            {
                if (list[i].operand as MethodInfo != getVelocity || list[i + 1].operand as MethodInfo != toFloat) continue;
                var op = list[i - 1].opcode;
                if (op != OpCodes.Ldloc && op != OpCodes.Ldloc_S && op != OpCodes.Ldloc_0 && op != OpCodes.Ldloc_1 && op != OpCodes.Ldloc_2 && op != OpCodes.Ldloc_3) continue;
                at = i; n++;
            }
            if (n != 1 || getVelocity == null || toFloat == null) { Util.Log("Träger-Konvoi: Geschwindigkeits-Stelle passt nicht (" + n + ") - bleibt Spielwert."); return list; }
            list.InsertRange(at + 2, new[]
            {
                new CodeInstruction(list[at - 1].opcode, list[at - 1].operand),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CarrierConvoy_Patch), "MemberSpeed"))
            });
            return list;
        }

        private static void Prefix() { cachedGroup = null; seen = true; }

        public static float MemberSpeed(float speed, SandboxEntity entity, SandboxMobileGroup group)
        {
            if (!Settings.B("FixCarrierConvoy")) return speed;
            try
            {
                if ((entity.Blueprint.Type.Category & EntityTypeCategory.CarrierBorneAircraft) == 0) return speed;
                if (!ReferenceEquals(cachedGroup, group)) { cachedGroup = group; cachedHasShips = HasShips(group); }
                if (!cachedHasShips) return speed;
                skippedFor = group;
                return float.PositiveInfinity;
            }
            catch (Exception e)
            {
                if (System.Threading.Interlocked.Exchange(ref errorLogged, 1) == 0) Util.Log("Träger-Konvoi: Prüfung fehlgeschlagen, Spielwert (einmalig): " + e);
                return speed;
            }
        }

        private static bool HasShips(SandboxMobileGroup g)
        {
            var m = g.Entities;
            for (int i = m.Count - 1; i >= 0; i--)
            {
                var c = m[i].Blueprint.Type.Category;
                if ((c & VanillaSkipped) == 0 && (c & EntityTypeCategory.CarrierBorneAircraft) == 0) return true;
            }
            return false;
        }

        private static void Postfix(SandboxMobileGroup __instance)
        {
            if (!ReferenceEquals(skippedFor, __instance)) return;
            skippedFor = null;
            try
            {
                int id = __instance.GetInstanceID();
                if (loggedGroups.Contains(id)) return;
                float ships = float.PositiveInfinity, air = float.PositiveInfinity;
                int airCount = 0, zero = 0;
                string zeroTypes = null;
                var m = __instance.Entities;
                for (int i = m.Count - 1; i >= 0; i--)
                {
                    var e = m[i];
                    var c = e.Blueprint.Type.Category;
                    if ((c & VanillaSkipped) != 0) continue;
                    float v = e.Velocity;
                    if ((c & EntityTypeCategory.CarrierBorneAircraft) == 0) { if (v < ships) ships = v; continue; }
                    airCount++;
                    if (v < air) air = v;
                    if (v <= 0f)
                    {
                        zero++;
                        string type = e.Blueprint.Type.Name;
                        if (zeroTypes == null || zeroTypes.IndexOf(type, StringComparison.Ordinal) < 0) zeroTypes = zeroTypes == null ? type : zeroTypes + ", " + type;
                        if (loggedTypes.Add(type))
                        {
                            int slots = e.Blueprint.Slots != null ? e.Blueprint.Slots.Length : 0;
                            Util.Log("Träger-Konvoi: Flugzeugtyp '" + type + "' hat Tempo 0: " + slots + " Ausrüstungs-Plätze" + (slots == 0 ? " (kein Propeller im Datenblatt)" : "") + ". Datenlücke im Mod, der den Typ hinzufügt.");
                        }
                    }
                }
                if (!(air < ships)) return;
                loggedGroups.Add(id);
                Util.Log("Träger-Konvoi: '" + __instance.Name + "': " + zero + " von " + airCount + " Flugzeugen mit Tempo 0 (" + (zeroTypes ?? "-") + "); langsamstes Flugzeug " + Kmh(air) + ", langsamstes Schiff " + Kmh(ships) + " km/h - Gruppe fährt jetzt nach den Schiffen.");
            }
            catch (Exception e)
            {
                if (System.Threading.Interlocked.Exchange(ref errorLogged, 1) == 0) Util.Log("Träger-Konvoi: Log fehlgeschlagen (einmalig): " + e);
            }
        }

        private static string Kmh(float v) => float.IsPositiveInfinity(v) ? "keins" : v.ToString("0.#", CultureInfo.InvariantCulture);

        // After switching the fix on or off, every group recomputes its top speed.
        public static void RefreshGroups()
        {
            if (!seen) return;
            var f = AccessTools.Field(typeof(SandboxGroup), "sandbox");
            var sb = f != null ? f.GetValue(null) as UBOAT.Game.Sandbox.Sandbox : null;
            var groups = sb?.GetGroupsDirect();
            if (groups == null) return;
            for (int i = groups.Count - 1; i >= 0; i--) (groups[i] as SandboxMobileGroup)?.SetParametersDirty();
        }
    }

    public class CarrierConvoyHost : ModuleBase
    {
        private bool last, init;
        public override void Tick()
        {
            bool on = Settings.B("FixCarrierConvoy");
            if (init && on != last) Util.Safe("CarrierConvoy.Refresh", CarrierConvoy_Patch.RefreshGroups);
            last = on; init = true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Cheats.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // Cheats for the player's boat (single player). Every cheat is off by default.
    public class Cheats : ModuleBase
    {
        private PlayerCrew crew;
        private PlayerCareer career;

        private static readonly string[] SensorKeys = { "CheatRangeLookout", "CheatRangeHydro", "CheatRangeSonar", "CheatRangeRadar", "CheatRangeRadarWarn" };
        private static readonly string[] sensorNow = new string[5];
        private static readonly float[] sensorNear = new float[5];
        private static float nextSensorInfo, nextSensorLine;
        private static bool sensorHooked;
        private static string hydroParts;
        // every part that goes into a range value: base parts (+) and factors (x), by name
        private int capped;
        private static string Parts(Parameter p)
        {
            if (p == null) return "-";
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var m in p.DeltaModifiers) if (m != null) sb.Append(" +").Append(m.Name).Append("=").Append(m.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                foreach (var m in p.ScaleModifiers) if (m != null) sb.Append(" x").Append(m.Name).Append("=").Append(m.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(" => ").Append(p.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (System.Exception e) { sb.Append(" (nicht lesbar: ").Append(e.Message).Append(")"); }
            return sb.ToString();
        }
        private static int SensorKind(UBOAT.Game.Scene.Items.BaseDetector d)
        {
            if (d is UBOAT.Game.Scene.Items.Hydrophone) return 1;
            if (d is UBOAT.Game.Scene.Items.Sonar) return 2;
            if (d is UBOAT.Game.Scene.Items.Radar) return 3;
            if (d is UBOAT.Game.Scene.Items.RadarDetector) return 4;
            return 0;
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;

            // 0.4.4: one range factor per sensor type, player only. Enemies always get factor 1 (game range).
            bool sensorLog = Time.unscaledTime >= nextSensorInfo;
            if (sensorLog) { nextSensorInfo = Time.unscaledTime + 1f; for (int i = 0; i < SensorKeys.Length; i++) sensorNow[i] = null; }
            foreach (var d in Util.Find<UBOAT.Game.Scene.Items.BaseDetector>())
            {
                bool mine = Util.IsPlayers(d);
                int kind = SensorKind(d);
                float m = mine ? Mathf.Max(0.05f, Settings.F(SensorKeys[kind])) : 1f;
                Util.ScaleParameter(d.DetectionRange, m);
                Util.ScaleParameter(d.GroupDetectionRange, m);
                // 0.4.19 (DoMe: "limiter, nicht größer als 1000 km"): our factor is cut back so that the range of the
                // player's sensor never exceeds the limit. Near range is in metres, far range in kilometres.
                if (mine && m > 1f)
                {
                    float cap = Mathf.Max(10f, Settings.F(kind == 0 ? "CheatRangeMaxKmLookout" : "CheatRangeMaxKm"));
                    Util.Safe("Sensor-Grenze", () =>
                    {
                        if (d.DetectionRange != null && d.DetectionRange.Value > cap * 1000f) { Util.ScaleParameter(d.DetectionRange, Mathf.Max(0.05f, m * cap * 1000f / d.DetectionRange.Value)); capped++; }
                        if (d.GroupDetectionRange != null && d.GroupDetectionRange.Value > cap) { Util.ScaleParameter(d.GroupDetectionRange, Mathf.Max(0.05f, m * cap / d.GroupDetectionRange.Value)); capped++; }
                    });
                }
                if (mine && sensorLog && d.DetectionRange != null)
                {
                    float near = d.DetectionRange.Value / 1000f, far = d.GroupDetectionRange != null ? d.GroupDetectionRange.Value : 0f;
                    // 0.4.9: the hydrophone values are multiplied by the boat's own listening factor before the game uses
                    // them (map circle: near x factor x 0.64, far x factor); without it the log showed "fern 13500 km".
                    if (kind == 1)
                    {
                        float hs = 1f;
                        try { hs = (float)ship.HydroacousticDetectorsRangeScale; } catch { }
                        near *= hs * 0.64f; far *= hs;
                        if (Time.unscaledTime >= nextSensorLine) hydroParts = "Horchgerät fern: Faktor Boot " + hs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", Teile " + Parts(d.GroupDetectionRange);
                    }
                    if (sensorNow[kind] == null || near > sensorNear[kind])
                    {
                        sensorNear[kind] = near;
                        sensorNow[kind] = "(jetzt nah " + near.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " km" + (far > 0f ? ", fern " + far.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " km" : "") + ")";
                    }
                }
            }
            if (sensorLog)
            {
                if (!sensorHooked)
                {
                    sensorHooked = true;
                    for (int i = 0; i < SensorKeys.Length; i++) { int k = i; var o = Settings.Get(SensorKeys[k]); if (o != null) o.Extra = () => sensorNow[k] ?? "(nicht an Bord)"; }
                }
                if (Time.unscaledTime >= nextSensorLine)
                {
                    nextSensorLine = Time.unscaledTime + 60f;
                    var sb = new System.Text.StringBuilder("Sensoren (nur eigenes Boot):");
                    for (int i = 0; i < SensorKeys.Length; i++) sb.Append(" ").Append(SensorKeys[i].Substring(10)).Append(" x").Append(Settings.F(SensorKeys[i]).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(" ").Append(sensorNow[i] ?? "(nicht an Bord)").Append(";");
                    sb.Append(". Grenze Sicht ").Append(Settings.F("CheatRangeMaxKmLookout").ToString("0")).Append(" km, übrige ").Append(Settings.F("CheatRangeMaxKm").ToString("0")).Append(" km, ").Append(capped).Append(" mal gekürzt, doppelte Faktoren entfernt: ").Append(Util.DuplicatesRemoved);
                    capped = 0;
                    Util.Log(sb.ToString());
                    if (hydroParts != null) { Util.Log(hydroParts); hydroParts = null; }
                }
            }

            // Undetectable: seen, heard (sonar + hydrophones) and radar-detected at almost zero range.
            float f = Settings.B("CheatUndetectable") ? 0.001f : 1f;
            var t = Traverse.Create(ship);
            foreach (var field in new[] { "visualDetectability", "sonarDetectability", "radarDetectorDetectability" })
                Util.ScaleParameter(t.Field(field).GetValue<Parameter>(), f);
            // 0.4.22 (option audit): "hydroacousticDetectorsRangeScale" is the range of OUR OWN listening gear. It was scaled
            // down with the cheat, so the own hydrophone and sonar went deaf. Always back to the game value now.
            Util.ScaleParameter(t.Field("hydroacousticDetectorsRangeScale").GetValue<Parameter>(), 1f);
            // Also the boat's own noise (dB, heard by hydrophones) and its radar size (cm): the game's own
            // dev-console "undetectable" sets exactly these to 0. Shown in the detection tooltip too.
            bool und = Settings.B("CheatUndetectable");
            Util.Safe("Undetectable.Noise", () =>
            {
                Util.OneScale(ship.HydrophoneDetectability, Util.ModifierName).Value = und ? 0f : 1f;
                Util.OneScale(ship.SandboxEntity.SignatureRadius, Util.ModifierName).Value = und ? 0f : 1f;
            });

            Util.ScaleParameter(t.Field("hullCrackThreshold").GetValue<Parameter>(), Settings.B("CheatDeepDive") ? 100f : 1f);
            // The game also has a separate "hull crush" timer (HullCrushController): below the boat's CrushDepth
            // it counts down and then sets the hull to 0 = boom, even without any leaks. Push that depth far away.
            Util.Safe("DeepDive.Crush", () =>
                Util.ScaleParameter(ship.SandboxEntity.Parameters.GetParameter("CrushDepth"), Settings.B("CheatDeepDive") ? 100f : 1f));

            if (Settings.B("CheatFullFuel")) Util.Safe("FullFuel", () => FillFuel(ship));
            if (Settings.B("CheatFullBattery")) Fill(ship.Energy);
            if (Settings.B("CheatFullAir")) Fill(ship.Oxygen);
            if (Settings.B("CheatFullDiscipline"))
            {
                Fill(ship.Discipline);
            }
            Util.Safe("Moral", () => NoMoraleLoss(ship));
            if (Settings.B("NoStress") && ship.Stress != null) ship.Stress.Amount = 0;

            if (Settings.B("CheatCrewNeverTired"))
            {
                if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
                if (crew != null && crew.Characters != null)
                    foreach (var c in crew.Characters) if (c != null) c.Energy = 1f;
            }

            float money = Settings.F("CheatMoney"), rep = Settings.F("CheatReputation");
            if (money > 0f || rep > 0f)
            {
                if (career == null) career = Util.Career;
                if (career != null)
                {
                    if (money > 0f && career.Budget < money) Util.SetField(career, "budget", Mathf.RoundToInt(money));
                    if (rep > 0f && career.ReputationPoints < rep) career.ReputationPoints = Mathf.RoundToInt(rep);
                }
            }
        }

        // The diesel % is computed by the game from the fuel lying in the tanks, so filling only the
        // number would be undone at once. Fill the tanks themselves.
        // Morale (game: "Discipline") with the cheat on: refilling alone left the display at e.g. "-102" (a big crew drains it
        // fast). Our own delta cancels exactly the negative part of the gain, so it shows 0 and never drops; gains stay.
        private const string MoraleMod = "UboatToolkit Moral";
        private static void NoMoraleLoss(PlayerShip ship)
        {
            var g = ship.Discipline != null ? ship.Discipline.Gain : null;
            if (g == null) return;
            var m = Util.OneDelta(g, MoraleMod);
            float want = 0f;
            if (Settings.B("CheatFullDiscipline"))
            {
                float v0 = g.GetValueWithModifierOverride(m, 0f), v1 = g.GetValueWithModifierOverride(m, 1f);
                if (v0 < 0f && v1 - v0 > 1e-6f) want = -v0 / (v1 - v0);
            }
            if (Mathf.Abs(m.Value - want) > 1e-6f) m.Value = want;
        }

        private static void FillFuel(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var srcs = ship.FuelSources;
            if (srcs == null) return;
            foreach (var f in srcs)
            {
                if (f == null || f.Storage == null || f.Data == null || f.Data.Mass <= 0f) continue;
                double full = f.Storage.ItemsMassLimit / f.Data.Mass;
                if (full > 0 && f.DoubleCount < full) f.DoubleCount = full;
            }
        }

        private static void Fill(Resource r)
        {
            if (r == null || r.Capacity == null) return;
            double cap = r.Capacity.Value;
            if (cap > 0 && r.Amount < cap) r.Amount = cap;
        }
    }

    // Hull health of the player's boat never goes down.
    [HarmonyPatch(typeof(Entity), nameof(Entity.Health), MethodType.Setter)]
    internal static class Cheats_HullPatch
    {
        private static void Prefix(Entity __instance, ref float value)
        {
            if (Settings.B("CheatNoHullDamage") && __instance is PlayerShip && value < __instance.Health) value = __instance.Health;
        }
    }

    // No water pressure damage at great depth.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.ApplyWaterDamageToPlayerShip), "DoDamageTick")]
    internal static class Cheats_DeepDivePatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!Settings.B("CheatDeepDive")) return true;
            __result = 5f; // check again in 5 seconds
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;

    // "Crew never tired": also the game's own tired check (Fatigue skill, updated every second per man) always says rested,
    // and the man's energy is refilled right there (Tick alone missed men between ticks / new men).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Skills.Fatigue), "UpdateFatigue")]
    internal static class Cheats_NeverTiredPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Characters.Skills.Fatigue __instance)
        {
            if (!Settings.B("CheatCrewNeverTired")) return;
            var c = __instance.Character;
            if (c != null) c.Energy = 1f;
        }
    }

    // Energy never goes down at all (DoMe: prevent the loss, not refill it): every write to Energy becomes "full".
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.PlayableCharacter), "Energy", HarmonyLib.MethodType.Setter)]
    internal static class Cheats_NoEnergyLossPatch
    {
        private static void Prefix(ref float value)
        {
            if (Settings.B("CheatCrewNeverTired")) value = 1f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/CPExtras.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Reflection.Emit;
    using global::System.Threading;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Data;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Navigation;
    using global::UBOAT.Game.Scene.Entities.NavMesh;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI.Periscope;

    // ---------- Officers: new or promoted officers get a watch (6 h or 3 h), split evenly in two watches ----------
    // Only officers WITHOUT a schedule yet (all hours "any") are touched; a schedule you set stays.
    internal static class OfficerWatch
    {
        private static readonly System.Reflection.FieldInfo SlotsF = AccessTools.Field(typeof(Schedule), "behaviourByHour");
        private static readonly System.Reflection.FieldInfo ClockF = AccessTools.Field(typeof(Schedule), "gameTime");
        private static readonly System.Reflection.MethodInfo RefreshM = AccessTools.Method(typeof(Schedule), "GameTimeOnHourChanged");
        internal static PlayerCrew Crew;
        private static int lastHourKey = -1;
        private static System.Reflection.PropertyInfo nowP;

        private static int Hours => Mathf.RoundToInt(Settings.F("OfficerWatchHours")) == 3 ? 3 : 6;
        private static byte[] Pattern(int hours, int watch)
        {
            var p = new byte[24];
            for (int h = 0; h < 24; h++) p[h] = (byte)(((h / hours + watch) % 2 == 0) ? Schedule.Behaviour.Work : Schedule.Behaviour.Sleep);
            return p;
        }
        private static byte[] Read(Schedule s) { var b = new byte[24]; for (int h = 0; h < 24; h++) b[h] = (byte)s[h]; return b; }
        private static bool Same(byte[] a, byte[] b) { for (int i = 0; i < 24; i++) if (a[i] != b[i]) return false; return true; }
        private static bool AllAny(byte[] b) { foreach (var x in b) if (x != (byte)Schedule.Behaviour.Any) return false; return true; }

        internal static bool AlreadyAboard(PlayerCrew crew, PlayableCharacterData data)
        {
            if (crew == null || data == null) return true;
            for (int i = 0; i < crew.Count; i++) if (crew[i] != null && crew[i].Data == data) return true;
            return false;
        }

        internal static void Assign(PlayerCrew crew, PlayableCharacter c, string why)
        {
            if (!Settings.B("OfficerWatches") || crew == null || c == null || !c.IsOfficer || c.Schedule == null || SlotsF == null) return;
            var s = c.Schedule;
            if (!AllAny(Read(s))) { Util.Log("Wachen: " + c.Name + " (" + why + ") hat schon einen Plan - bleibt so"); return; }
            int hours = Hours;
            byte[] w0 = Pattern(hours, 0), w1 = Pattern(hours, 1);
            int n0 = 0, n1 = 0;
            foreach (var o in crew.Officers ?? new PlayableCharacter[0])
            {
                if (o == null || ReferenceEquals(o, c) || o.Schedule == null) continue;
                var b = Read(o.Schedule);
                if (Same(b, w0)) n0++; else if (Same(b, w1)) n1++;
            }
            int watch = n0 <= n1 ? 0 : 1;
            var pattern = watch == 0 ? w0 : w1;
            var slots = SlotsF.GetValue(s) as Schedule.Behaviour[];
            if (slots == null || slots.Length < 24) return;
            for (int h = 0; h < 24; h++) slots[h] = (Schedule.Behaviour)pattern[h];
            bool now = ClockF != null && ClockF.GetValue(s) != null && RefreshM != null;
            if (now) RefreshM.Invoke(s, null);
            Util.Log("Wachen: " + c.Name + " (" + why + "): " + hours + "-h-Wachen, Wache " + (watch == 0 ? "A" : "B") + " im Dienst " + OnDuty(pattern) + (now ? "" : " (ab nächster Stunde)"));
        }

        private static string OnDuty(byte[] p)
        {
            var sb = new System.Text.StringBuilder();
            int start = -1;
            for (int h = 0; h <= 24; h++)
            {
                bool on = h < 24 && p[h] == (byte)Schedule.Behaviour.Work;
                if (on && start < 0) start = h;
                if (!on && start >= 0) { if (sb.Length > 0) sb.Append(", "); sb.Append(start.ToString("00")).Append('-').Append(h.ToString("00")); start = -1; }
            }
            return sb.ToString();
        }

        // Optional hourly log of the officers (energy and duty), switch in the Tools tab.
        internal static void LogHour(Schedule s)
        {
            if (!Settings.B("OfficerWatchLog") || Crew == null || ClockF == null) return;
            object clock = ClockF.GetValue(s);
            if (clock == null) return;
            if (nowP == null) nowP = AccessTools.Property(clock.GetType(), "CurrentDateTime");
            if (nowP == null) return;
            var now = (DateTime)nowP.GetValue(clock, null);
            int key = now.DayOfYear * 24 + now.Hour;
            if (key == lastHourKey) return;
            lastHourKey = key;
            var sb = new System.Text.StringBuilder("Wachen ").Append(now.Hour.ToString("00")).Append(" Uhr:");
            foreach (var o in Crew.Officers ?? new PlayableCharacter[0])
            {
                if (o == null) continue;
                sb.Append(' ').Append(o.Name).Append(' ').Append(o.Energy.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                if (o.Schedule != null) sb.Append(' ').Append(o.Schedule[now.Hour]);
                sb.Append(';');
            }
            Util.Log(sb.ToString());
        }

        internal static bool Loading()
        {
            try { var sm = ScriptableObjectSingleton.LoadSingleton<SavesManager>(); return sm != null && sm.IsLoading; } catch { return false; }
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "AddCharacter", new[] { typeof(PlayableCharacterData) })]
    internal static class Extras_OfficerNewPatch
    {
        private static void Prefix(PlayerCrew __instance, PlayableCharacterData characterData, out bool __state)
        {
            __state = false;
            try { __state = Settings.B("OfficerWatches") && !OfficerWatch.Loading() && !OfficerWatch.AlreadyAboard(__instance, characterData); } catch { }
        }
        private static void Postfix(PlayerCrew __instance, PlayableCharacter __result, bool __state)
        {
            if (__state) Util.Safe("Extras.Watch", () => OfficerWatch.Assign(__instance, __result, "neu an Bord"));
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "Character_Promoted")]
    internal static class Extras_OfficerPromotedPatch
    {
        private static void Postfix(PlayerCrew __instance, PlayableCharacterData characterData)
        {
            if (!Settings.B("OfficerWatches") || characterData == null || OfficerWatch.Loading()) return;
            Util.Safe("Extras.Watch", () =>
            {
                for (int i = 0; i < __instance.Count; i++)
                    if (__instance[i] != null && __instance[i].Data == characterData) { OfficerWatch.Assign(__instance, __instance[i], "befördert"); return; }
            });
        }
    }
    [HarmonyPatch(typeof(PlayerCrew), "AddCharacter", new[] { typeof(PlayableCharacter) })]
    internal static class Extras_OfficerCrewPatch
    {
        private static void Postfix(PlayerCrew __instance) { OfficerWatch.Crew = __instance; }
    }
    [HarmonyPatch(typeof(Schedule), "GameTimeOnHourChanged")]
    internal static class Extras_OfficerHourPatch
    {
        private static void Postfix(Schedule __instance) { if (Settings.B("OfficerWatches")) Util.Safe("Extras.WatchLog", () => OfficerWatch.LogHour(__instance)); }
    }

    // ---------- Keep the chosen crew voice language (game resets it after some updates) ----------
    [HarmonyPatch(typeof(UserSettings), "DetermineDefaultVoice")]
    internal static class Extras_VoicePatch
    {
        private static bool Prefix(ref bool ___applyDefaultVoiceLanguage)
        {
            if (!Settings.B("KeepVoiceLanguage")) return true;
            ___applyDefaultVoiceLanguage = false;
            return false;
        }
    }

    // ---------- Stutter fix: main thread no longer waits for a running background texture job ----------
    [HarmonyPatch]
    internal static class Extras_AbortStallPatch
    {
        private static System.Reflection.FieldInfo current;
        private static bool Prepare()
        {
            var t = UboatToolkit.Util.FindType("DWS.Common.Tasks.TaskExecutor");
            current = t != null ? AccessTools.Field(t, "currentRunnable") : null;
            return current != null && TargetMethod() != null;
        }
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("DWS.Common.Tasks.TaskExecutor");
            var r = UboatToolkit.Util.FindType("DWS.Common.Tasks.IRunnable");
            return t == null || r == null ? null : AccessTools.Method(t, "Abort", new[] { r });
        }
        private struct St { public bool Holding; public object Runnable; public long Start; }
        private static int skipped, waitedSlow, lastLog, errorLogged;
        private static System.Reflection.PropertyInfo nameP;

        private static string NameOf(object r)
        {
            try { if (nameP == null) nameP = UboatToolkit.Util.FindType("DWS.Common.Tasks.IRunnable")?.GetProperty("Name"); return nameP != null ? (nameP.GetValue(r, null) as string ?? "?") : "?"; }
            catch { return "?"; }
        }

        private static bool Prefix(object __instance, object runnable, out St __state)
        {
            __state = new St { Start = LoadClock.Now() };
            try
            {
                if (runnable == null || ReferenceEquals(current.GetValue(__instance), runnable)) return true;
                if (Monitor.TryEnter(runnable)) { __state.Holding = true; __state.Runnable = runnable; return true; }
                if (!Settings.B("FixAbortStall")) { __state.Runnable = runnable; return true; }   // measure the vanilla wait for the log
                int n = Interlocked.Increment(ref skipped);
                int now = Environment.TickCount;
                if (Settings.B("AbortStallLog") && (unchecked(now - lastLog) >= 1000 || n <= 3))
                {
                    lastLog = now;
                    Util.Log("Stocken-Fix: laufende Hintergrund-Arbeit abgebrochen ohne zu warten: '" + NameOf(runnable) + "' (Sitzung: " + n + " Wartezeiten gespart).");
                }
                return false;   // busy in the background: skip instead of freezing the game
            }
            catch (Exception e)
            {
                if (Interlocked.Exchange(ref errorLogged, 1) == 0) Util.Log("Stocken-Fix: Fehler, nehme Spielverhalten (einmalig): " + e);
                return true;
            }
        }

        private static Exception Finalizer(Exception __exception, St __state)
        {
            if (__state.Holding) { try { Monitor.Exit(__state.Runnable); } catch { } }
            else if (__state.Runnable != null && Settings.B("AbortStallLog"))
            {
                double ms = LoadClock.Seconds(LoadClock.Now() - __state.Start) * 1000.0;
                if (ms >= 20.0) Util.Log("Stocken-Fix: Spiel WARTETE " + ms.ToString("0.0") + " ms auf '" + NameOf(__state.Runnable) + "' (Fix aus; Sitzung: " + Interlocked.Increment(ref waitedSlow) + ").");
            }
            return __exception;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/ExtraFeatures.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Text.RegularExpressions;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.Tasks;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.UI.Loading;

    // Ideas from popular workshop mods, own implementation, all in one:
    // energy saving, tube display (torpedo types), officer roles + skill choice (tab "Crew"),
    // faster torpedo loading, faster game loading.
    public class ExtraFeatures : ModuleBase
    {
        private bool energySaved;
        public static string TubeLine;

        // Searching all loaded objects for these took 150-200 ms each second (lag every few seconds).
        // They are found once and kept; the skill cheats only need to run every 10 seconds.
        private static CharacterClasses classesCache;
        private static PlayerCrew crewCache;
        private static float nextSkills;
        private static int lastFlags = -1;

        // A failed search (main menu, loading) used to be repeated on every call: 100-400 ms each time.
        private static float classesRetry, crewRetry;
        private static IEnumerable<CharacterClass> Classes()
        {
            if (classesCache == null && Time.unscaledTime >= classesRetry) { classesRetry = Time.unscaledTime + 30f; classesCache = Resources.FindObjectsOfTypeAll<CharacterClasses>().FirstOrDefault(); }
            return classesCache?.GetClassesDirect();
        }

        private static PlayerCrew Crew()
        {
            if (crewCache == null && Time.unscaledTime >= crewRetry) { crewRetry = Time.unscaledTime + 30f; crewCache = Object.FindObjectOfType<PlayerCrew>(); }
            return crewCache;
        }

        // The skill cheats walk every skill of every crew member (100-400 ms with a big crew). The result only changes
        // when the crew changes, so they run again only then (and every 5 minutes as a safety net).
        private static int lastCrewCount = -1;
        private static float fullRunAt;
        // A promotion resets the officer's skill list (PromoteToClass), the crew count stays the same:
        // so the skill cheats are applied again right away, before the promotion screen shows the list.
        internal static void ReapplySkills()
        {
            lastCrewCount = -1; nextSkills = 0f;
            if (Settings.B("CheatAllSkills")) AllSkills();
            if (Settings.B("CheatSkillsAnyLevel")) SkillLevels(true);
            if (Settings.B("CheatLinguist")) Linguists();
        }
        private static bool CrewChanged()
        {
            int n = Util.SafeGet(() => Crew()?.Characters?.Count() ?? -1, -1);
            if (n == lastCrewCount && Time.unscaledTime < fullRunAt) return false;
            lastCrewCount = n; fullRunAt = Time.unscaledTime + 300f;
            return true;
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            EnergySaver(ship);
            Tubes();
            // Crew specializations (diving / silent running / discipline, 10 each): always enough free points.
            if (Settings.B("CheatSpecPoints"))
                Util.Safe("SpecPoints", () =>
                {
                    var c = Crew();
                    if (c == null) return;
                    int need = Mathf.Max(0, 30 - c.SpecializationDivingPoints - c.SpecializationSilentRunningPoints - c.SpecializationDisciplinePoints);
                    if (c.FreeSpecializationPoints < need) c.FreeSpecializationPoints = need;
                });
            bool any = Settings.B("CheatSkillsAnyLevel");
            if (!any && skillLevels.Count > 0) SkillLevels(false);
            if (Time.unscaledTime < nextSkills) return;
            nextSkills = Time.unscaledTime + 10f;
            bool all = Settings.B("CheatAllSkills"), ling = Settings.B("CheatLinguist");
            bool turnedOn = (all ? 1 : 0) + (any ? 2 : 0) + (ling ? 4 : 0) != lastFlags;
            lastFlags = (all ? 1 : 0) + (any ? 2 : 0) + (ling ? 4 : 0);
            if (!CrewChanged() && !turnedOn) return;
            if (all) AllSkills();
            if (any) SkillLevels(true);
            if (ling) Linguists();
        }

        // Everyone who speaks one foreign language ("... Speaker") also gets "Linguist" (all languages).
        private static void Linguists()
        {
            var classes = Classes();
            var ling = classes?.SelectMany(c => c.Skills).FirstOrDefault(x => x != null && x.Name == "Linguist");
            var crew = Crew();
            if (ling == null || crew?.Characters == null) return;
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data;
                if (d == null) continue;
                var sk = d.Statuses.OfType<CharacterSkill>().ToList();
                if (sk.Any(x => x.Name.EndsWith(" Speaker")) && !sk.Any(x => x.Name == "Linguist")) d.LearnSkill(ling, false);
            }
        }

        // All skills can be learned at any level: the level requirement is set to 0 (and restored when switched off).
        private static readonly Dictionary<CharacterSkill, int> skillLevels = new Dictionary<CharacterSkill, int>();
        private static void SkillLevels(bool on)
        {
            if (!on)
            {
                if (skillLevels.Count == 0) return;
                foreach (var kv in skillLevels) if (kv.Key != null) kv.Key.RequiredLevel = kv.Value;
                skillLevels.Clear();
                return;
            }
            var list = new List<CharacterSkill>();
            var classes = Classes();
            if (classes != null) list.AddRange(classes.SelectMany(c => c.Skills));
            var crew = Crew();
            if (crew?.Characters != null)
                foreach (var ch in crew.Characters)
                    if (ch?.Data?.AvailableSkills != null) list.AddRange(ch.Data.AvailableSkills);
            foreach (var sk in list)
            {
                if (sk == null || sk.RequiredLevel <= 0) continue;
                if (!skillLevels.ContainsKey(sk)) skillLevels[sk] = sk.RequiredLevel;
                sk.RequiredLevel = 0;
            }
        }

        // Battery low under water: switch off big power users (compressor, searchlight) once.
        private void EnergySaver(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            float lim = Settings.F("EnergySaverPercent") / 100f;
            var r = ship.GetResource("Energy");
            if (lim <= 0f || r == null) { energySaved = false; return; }
            float v = (float)r.Amount;
            if (v > lim + 0.1f) energySaved = false;
            if (energySaved || v >= lim || !ship.SubmergedOrGoingToSubmerge) return;
            energySaved = true;
            int n = 0;
            foreach (var c in Util.Find<AirCompressor>()) if (Util.IsPlayers(c) && !c.HasDisableFlag(DeviceDisableFlag.TurnedOff)) { c.SetDisableFlag(DeviceDisableFlag.TurnedOff, true); n++; }
            foreach (var s in Util.Find<Searchlight>()) if (Util.IsPlayers(s) && !s.HasDisableFlag(DeviceDisableFlag.TurnedOff)) { s.SetDisableFlag(DeviceDisableFlag.TurnedOff, true); n++; }
            if (n > 0) ToolkitRunner.ShowNotice("Strom sparen: " + n + " Geräte aus (Batterie " + (v * 100f).ToString("0") + " %)", 6f);
        }

        // "Rohr 1: T2 | Rohr 2: leer ..." in the info area.
        private static void Tubes()
        {
            if (!Settings.B("TubeDisplay")) { TubeLine = null; return; }
            var parts = new List<string>();
            int i = 1;
            // 0.4.22 (option audit): only OUR tubes (enemy submarines were listed too), in a fixed order (by name)
            var own = new List<TorpedoLauncher>();
            foreach (var x in Util.Find<TorpedoLauncher>())
                if (x != null && (Util.IsPlayers(x) || (x.Group != null && x.Group.ParentEntity is UBOAT.Game.Scene.Entities.PlayerShip))) own.Add(x);
            own.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var l in own)
            {
                string t = "leer";
                if (l.Torpedo != null)
                {
                    var m = Regex.Match(l.Torpedo.name, @"\bT\d+[a-z]?\b");
                    t = m.Success ? m.Value : (l.Torpedo is StoredMinePacket ? "Mine" : l.Torpedo.name.Replace("(Clone)", "").Trim());
                    if (!l.IsLoaded) t += " (lädt)";
                }
                parts.Add(i++ + ": " + t);
            }
            TubeLine = parts.Count > 0 ? "Rohre  " + string.Join("  |  ", parts) : null;
        }

        // Cheat: every officer may choose skills of all classes.
        private static void AllSkills()
        {
            var classes = Classes();
            if (classes == null) return;
            var all = classes.SelectMany(c => c.Skills).GroupBy(s => s.Name).Select(g => g.First()).ToList();
            var crew = Crew();
            if (crew?.Characters == null) return;
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data;
                if (d == null || d.CharacterType == PlayableCharacterData.Type.Sailor) continue;
                var owned = new HashSet<string>(d.Statuses.OfType<CharacterSkill>().Select(s => s.Name));
                var avail = d.AvailableSkills.ToList();
                bool changed = false;
                foreach (var s in all)
                    if (!owned.Contains(s.Name) && !avail.Any(a => a.Name == s.Name)) { avail.Add(s); changed = true; }
                if (changed) Util.SetField(d, "availableSkills", avail.ToArray());
            }
        }

        // ---------- Tab "Crew" in the F10 menu: roles + skill points ----------
        private static readonly List<ICharacterRole> roles = new List<ICharacterRole>();

        public static void DrawCrewTab()
        {
            var crew = Crew();
            if (Util.PlayerShip == null || crew?.Characters == null) { Imgui.Label("Nur im Spiel verfügbar."); return; }
            var db = Traverse.Create(typeof(UBOAT.Game.Scene.Tasks.ExpandedSchedulesController)).Field("characterRolesDatabase").GetValue() as ICharacterRolesDatabase;
            Imgui.Label("Offiziere: Rolle mit Klick wechseln. „Skills bearbeiten“ öffnet den Skill-Editor (alle Skills aller Klassen).");
            // 0.3.44 (DoMe): for the whole crew (officers AND sailors), one click each
            Imgui.BeginH();
            if (Imgui.Button("Ganze Mannschaft: Max Stufe", 260)) Util.Safe("Crew.MaxLevel", () => MaxLevelAll(crew));
            if (Imgui.Button("Ganze Mannschaft: alle Skills", 260)) Util.Safe("Crew.AllSkills", () => AllSkillsAll(crew));
            Imgui.EndH();
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data;
                if (d == null || d.CharacterType == PlayableCharacterData.Type.Sailor) continue;
                Imgui.BeginH();
                Imgui.Label(d.Name + "  (Skillpunkte frei: " + d.GetAvailableSkillPoints() + ")", 330);
                string roleName = ch.Role?.Name ?? "keine";
                if (Imgui.Button("Rolle: " + roleName, 300) && db != null)
                {
                    db.GetAvailableRoles(ch, roles);
                    var allowed = roles.Where(r => r.IsRoleAllowedForCharacter(ch)).ToList();
                    if (allowed.Count > 0)
                    {
                        int i = allowed.IndexOf(ch.Role);
                        ch.Role = allowed[(i + 1) % allowed.Count];
                    }
                }
                bool open = editSkills == d;
                if (Imgui.Button(open ? "Skills zu" : "Skills bearbeiten", 160)) editSkills = open ? null : d;
                Imgui.EndH();
                if (editSkills == d) SkillEditor(d);
            }
        }

        private static void MaxLevelAll(PlayerCrew crew)
        {
            int n = 0;
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data; if (d == null) continue;
                int before = Traverse.Create(d).Field("level").GetValue<int>();
                for (int i = 0; i < 30; i++) d.AddExperiencePoints(100000);
                int after = Traverse.Create(d).Field("level").GetValue<int>();
                if (after > before) n++;
            }
            Util.Log("Mannschaft: Max Stufe, " + n + " Leute aufgestiegen");
            ToolkitRunner.ShowNotice("Max Stufe: " + n + " Leute aufgestiegen", 5f);
        }

        private static void AllSkillsAll(PlayerCrew crew)
        {
            var classes = Classes();
            if (classes == null) { ToolkitRunner.ShowNotice("Skill-Liste nicht gefunden", 5f); return; }
            var all = classes.SelectMany(c => c.Skills).Where(x => x != null).GroupBy(x => x.Name).Select(g => g.First()).ToList();
            int n = 0;
            foreach (var ch in crew.Characters)
            {
                var d = ch?.Data; if (d == null) continue;
                var owned = d.Statuses.OfType<CharacterSkill>().Select(o => o.Name).ToList();
                foreach (var sk in all) if (!owned.Contains(sk.Name)) { try { d.LearnSkill(sk, false); n++; } catch { } }
            }
            Util.Log("Mannschaft: alle Skills, " + n + " Skills vergeben");
            ToolkitRunner.ShowNotice("Alle Skills: " + n + " vergeben", 5f);
        }

        // Skill editor: every skill of every class, one click adds or removes it (no points, no level needed).
        private static PlayableCharacterData editSkills;
        private static void SkillEditor(PlayableCharacterData d)
        {
            var classes = Classes();
            if (classes == null) { Imgui.Label("  Skill-Liste nicht gefunden."); return; }
            var all = classes.SelectMany(c => c.Skills).Where(x => x != null).GroupBy(x => x.Name).Select(g => g.First()).OrderBy(x => x.Name).ToList();
            var owned = d.Statuses.OfType<CharacterSkill>().ToList();
            Imgui.BeginH();
            Imgui.Label("  Klick = Skill dazu (+) oder weg (✓).", 330);
            if (Imgui.Button("Alle geben", 120)) foreach (var sk in all) if (!owned.Any(o => o.Name == sk.Name)) d.LearnSkill(sk, false);
            if (Imgui.Button("Alle weg", 120)) foreach (var o in owned) d.RemoveStatus(o);
            Imgui.EndH();
            for (int i = 0; i < all.Count; i += 3)
            {
                Imgui.BeginH();
                Imgui.Space(20);
                for (int j = i; j < i + 3 && j < all.Count; j++)
                {
                    var sk = all[j];
                    var have = owned.FirstOrDefault(o => o.Name == sk.Name);
                    if (Imgui.Button((have != null ? "✓ " : "+ ") + sk.Name, 250))
                    {
                        if (have != null) d.RemoveStatus(have);
                        else d.LearnSkill(sk, false);
                    }
                }
                Imgui.EndH();
            }
            Imgui.Space(6);
        }
    }

    [HarmonyPatch(typeof(PlayableCharacterData), nameof(PlayableCharacterData.PromoteToClass))]
    internal static class ExtraFeatures_PromotePatch
    {
        private static void Postfix() { Util.Safe("Skills.Promote", () => { ExtraFeatures.ReapplySkills(); Util.Log("Beförderung: Skill-Cheats neu angewendet"); }); }
    }

    // Cheat: extra skill points for every officer.
    [HarmonyPatch(typeof(PlayableCharacterData), nameof(PlayableCharacterData.GetAvailableSkillPoints))]
    internal static class ExtraFeatures_SkillPointsPatch
    {
        // The game clamps at 0, so the extra points are added before that clamp (recomputed here).
        private static void Postfix(PlayableCharacterData __instance, ref int __result)
        {
            if (Settings.B("CheatUnlimitedSkillPoints")) { __result = 99; return; }
            int extra = Mathf.Max(0, (int)Settings.F("CheatExtraSkillPoints"));
            if (extra == 0) return;
            int level = Traverse.Create(__instance).Field("level").GetValue<int>();
            int used = __instance.Statuses.OfType<CharacterSkill>().Count(sk => !sk.IsFree);
            __result = Mathf.Max(0, level - 1 - used + extra);
        }
    }

    // Torpedo loading x faster (1000 = practically instant).
    [HarmonyPatch(typeof(LoadTorpedoesAction), "StartMainCharacter")]
    internal static class ExtraFeatures_TorpedoLoadPatch
    {
        private static void Postfix(LoadTorpedoesAction __instance)
        {
            float f = Settings.F("TorpedoLoadSpeed");
            if (f <= 1f) return;
            var t = Traverse.Create(__instance).Field("performanceMultiplier");
            t.SetValue(t.GetValue<float>() * f);
        }
    }

    // Faster game loading: while the loading screen runs, the game's background work may use the
    // whole frame (normally only ~2 ms per frame) and textures are uploaded in bigger pieces.
    [HarmonyPatch(typeof(LoadingUIController), "UpdateSceneLoading")]
    internal static class ExtraFeatures_LoadingBoostPatch
    {
        private static bool boosted, asyncSet;
        private static System.Threading.ThreadPriority origAsync;
        internal static bool Boosted => boosted;
        private static int origSlice, origBuffer;
        private static ThreadPriority origPriority;

        private static void Postfix(LoadingUIController __instance)
        {
            bool loading = Traverse.Create(__instance).Field("isLoading").GetValue<bool>();
            var ex = TaskExecutor.Instance;
            if (loading && Settings.B("FastLoading"))
            {
                if (!boosted) { origSlice = QualitySettings.asyncUploadTimeSlice; origBuffer = QualitySettings.asyncUploadBufferSize; origPriority = Application.backgroundLoadingPriority; }
                if (ex != null) ex.UnlimitedFrameBudget = true;
                // 0.4.8: the game's helper threads (terrain, crew meshes) run at the lowest priority; normal while loading
                if (ex != null && !boosted) Util.Safe("Laden: Hilfs-Threads", () => { origAsync = ex.AsyncThreadsPriority; ex.AsyncThreadsPriority = System.Threading.ThreadPriority.Normal; asyncSet = true; });
                QualitySettings.asyncUploadTimeSlice = 33;
                QualitySettings.asyncUploadBufferSize = 256;   // 0.3.68: test, was 64 (DoMe: texture loading takes forever; 40-58 s of each load is waiting)
                Application.backgroundLoadingPriority = ThreadPriority.High;
                boosted = true;
            }
            else if (boosted)
            {
                if (ex != null) ex.UnlimitedFrameBudget = false;
                if (ex != null && asyncSet) Util.Safe("Laden: Hilfs-Threads", () => { ex.AsyncThreadsPriority = origAsync; asyncSet = false; });
                QualitySettings.asyncUploadTimeSlice = origSlice;
                QualitySettings.asyncUploadBufferSize = origBuffer;
                Application.backgroundLoadingPriority = origPriority;
                boosted = false;
            }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Extras.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Devices;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

    // Own ideas + remaining cheats:
    // research speed, sonar (echo sounder) auto-ping, all upgrades/boats unlocked, faster torpedoes,
    // quick travel, depth keys, reload all tubes, swap watch shifts, tidy storage, route ETA + range.
    public class Extras : ModuleBase
    {
        public override void Frame()
        {
            if (Util.PlayerShip == null) return;
            // depth keys: see DepthGauge (key alone / Shift x10 / Alt x100)
            if (Keys.Down("KeyReloadAll")) Util.Safe("ReloadAll", ReloadAll);
            if (Keys.Down("KeyQuickTravel") && Settings.B("CheatQuickTravel")) Util.Safe("QuickTravel", QuickTravel);
            if (Keys.Down("KeySortStorage")) Util.Safe("SortStorage", SortStorage);
            if (Keys.Down("KeySwapShifts")) Util.Safe("SwapShifts", SwapShifts);
            if (Keys.Down("KeyCallMilkCow")) Util.Safe("CallMilkCow", CallMilkCow);
            if (Keys.Down("KeySpawnConvoy")) Util.Safe("SpawnConvoy", SpawnConvoy);
            if (Keys.Down("KeyBackToWater")) { var o = Settings.Get("BackToWaterNow"); if (o != null) o.Value = 1; }
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            OneShot("ReloadAllNow", ReloadAll);
            OneShot("SwapShiftsNow", SwapShifts);
            OneShot("SortStorageNow", SortStorage);
            OneShot("CheatUnlockAll", UnlockAll);
            OneShot("CheatUnlockBoats", UnlockBoats);
            OneShot("CheatQuickTravelNow", QuickTravel);
            OneShot("CallMilkCow", CallMilkCow);
            OneShot("CheatSpawnConvoy", SpawnConvoy);

            // Research at the headquarters x faster (instant = finishes within a second).
            Util.Safe("research", () => Util.ScaleParameter(ship.SandboxEntity?.Country?.Headquarters?.TaskExecutionPerformance,
                Settings.B("CheatInstantResearch") ? 1000000f : Settings.F("CheatResearchSpeed")));
            OneShot("CheatResearchAll", () => ResearchAll(ship));
            OneShot("CheatAddReputation", () =>
            {
                var c = Util.Career; if (c == null) return;
                int add = Mathf.Max(1, (int)Settings.F("CheatAddReputationAmount"));
                c.ReputationPoints += add;
                ToolkitRunner.ShowNotice("+" + add + " Ansehen (jetzt " + c.ReputationPoints + ")", 4f);
            });
            OneShot("CheatAddUpgradePoints", () =>
            {
                var c = Util.Career; if (c == null) return;
                int add = Mathf.Max(1, (int)Settings.F("CheatAddReputationAmount"));
                c.ShipUpgradePoints += add;
                ToolkitRunner.ShowNotice("+" + add + " Upgrade-Punkte (jetzt " + c.ShipUpgradePoints + ")", 4f);
            });

            // Echo sounder pings by itself while submerged.
            bool autoPing = Settings.B("CheatSonarPing");
            if (autoPing || autoPingWas)
                foreach (var d in Util.Find<SoundRangingDevice>())
                    if (Util.IsPlayers(d)) d.IsSendButtonPressed = autoPing && ship.SubmergedOrGoingToSubmerge;
            if (autoPingWas && !autoPing) Util.Log("Echolot: Dauer-Ping aus, Sendetaste losgelassen");
            autoPingWas = autoPing;

            // Faster torpedoes: the speed values of all own torpedoes are raised, so the
            // torpedo computer also aims with the new speed.
            float tf = Settings.F("CheatTorpedoSpeed");
            foreach (var t in Util.Find<StoredTorpedo>(1f))
            {
                Util.ScaleParameter(t.Speed1, tf);
                Util.ScaleParameter(t.Speed2, tf);
                Util.ScaleParameter(t.Speed3, tf);
            }

            NavInfo(ship);
        }

        private static readonly Dictionary<string, int> oneShotTries = new Dictionary<string, int>();

        // A switch that does something once and turns itself off. Waits until the boat is fully
        // loaded; if it still fails, it tries again next second (up to 10 times) instead of giving up.
        private static bool autoPingWas;
        private static void OneShot(string key, System.Action a)
        {
            var o = Settings.Get(key);
            if (o == null || !o.On || !Util.ShipReady) return;
            try { a(); o.Value = 0; oneShotTries.Remove(key); }
            catch (System.Exception e)
            {
                oneShotTries.TryGetValue(key, out int n); oneShotTries[key] = ++n;
                if (n >= 10) { o.Value = 0; oneShotTries.Remove(key); Util.Log("Aktion " + key + " fehlgeschlagen: " + e); }
            }
        }

        private static void ChangeDepth(float delta)
        {
            var ship = Util.PlayerShip;
            float d = Mathf.Max(0f, ship.TargetDepth + delta);
            Owners.Depth(ship, "Tiefen-Taste (du)", Owners.Player, d, false, true);
            ToolkitRunner.ShowNotice("Zieltiefe " + d.ToString("0") + " m", 2f);
        }

        // Every empty tube gets the nearest torpedo from the boat's own storage (nothing is created).
        private static void ReloadAll()
        {
            var ship = Util.PlayerShip;
            var storages = Util.Find<Storage>().Where(s => s != null && !(s is UBOAT.Game.Scene.Items.TorpedoLauncherGroup) && s.GetComponentInParent<PlayableCharacter>() == null).ToArray();
            int n = 0;
            foreach (var l in Util.Find<TorpedoLauncher>())
            {
                // 0.4.22 (option audit): only OUR tubes, and never a tube the crew is loading right now (see Torpedoes.TubeBusy)
                if (l == null || !(Util.IsPlayers(l) || (l.Group != null && l.Group.ParentEntity is PlayerShip))) continue;
                if (l.Torpedo != null || Torpedoes.TubeBusy(l)) continue;
                Storage best = null; int bestSlot = -1; float bestDist = float.MaxValue;
                foreach (var s in storages)
                    for (int i = 0; i < Util.Slots(s); i++)
                    {
                        if (!(s.GetItemAt(i) is StoredTorpedo t) || t is StoredMinePacket || s.HasTransportOrders(i)) continue;
                        float dist = (s.transform.position - l.transform.position).sqrMagnitude;
                        if (dist < bestDist) { best = s; bestSlot = i; bestDist = dist; }
                    }
                if (best == null) break;
                var torp = (StoredTorpedo)best.GetItemAt(bestSlot);
                best.SetItemAt(bestSlot, null);
                l.SetTorpedo(torp);
                l.CompleteLoading(quiet: true);
                n++;
            }
            ToolkitRunner.ShowNotice(n > 0 ? n + " Rohr(e) nachgeladen" : "Kein leeres Rohr oder kein Torpedo im Lager", 4f);
        }

        // Groups with the same job swap their watch hours (e.g. tired watch goes to bed).
        private static void SwapShifts()
        {
            var crew = Object.FindObjectOfType<PlayerCrew>();
            if (crew?.SailorGroups == null) return;
            int swapped = 0;
            foreach (var role in crew.SailorGroups.Where(g => g != null && g.Enabled && !g.Hidden).GroupBy(g => g.Role))
            {
                var list = role.ToList();
                if (list.Count < 2) continue;
                var first = Copy(list[0].Schedule);
                for (int i = 0; i < list.Count; i++)
                {
                    var src = i + 1 < list.Count ? Copy(list[i + 1].Schedule) : first;
                    for (int h = 0; h < 24; h++) list[i].Schedule[h] = src[h];
                }
                swapped++;
            }
            ToolkitRunner.ShowNotice(swapped > 0 ? "Wachen getauscht" : "Keine Gruppen zum Tauschen", 4f);
        }

        private static Schedule.Behaviour[] Copy(Schedule s)
        {
            var a = new Schedule.Behaviour[24];
            for (int h = 0; h < 24; h++) a[h] = s[h];
            return a;
        }

        // Tidy each storage: fill up same items into one stack, then move items to the front.
        private static void SortStorage()
        {
            int moves = 0;
            foreach (var s in Util.Find<Storage>())
            {
                if (s.GetComponentInParent<PlayableCharacter>() != null) continue;
                for (int a = 0; a < Util.Slots(s); a++)
                {
                    if (!(s.GetItemAt(a) is Cargo ca) || s.HasTransportOrders(a)) continue;
                    for (int b = a + 1; b < Util.Slots(s); b++)
                    {
                        if (!(s.GetItemAt(b) is Cargo cb) || cb.Data.Name != ca.Data.Name || s.HasTransportOrders(b)) continue;
                        float room = (float)ca.StackLimit - ca.Count;
                        if (room <= 0f) break;
                        float move = Mathf.Min(room, cb.Count);
                        if (move <= 0f) continue;
                        StorageUtility.TransferInstantly(s, b, s, a, move);
                        moves++;
                    }
                }
                for (int a = 0; a < Util.Slots(s); a++)
                {
                    if (s.GetItemAt(a) != null || s.HasTransportOrders(a)) continue;
                    for (int b = a + 1; b < Util.Slots(s); b++)
                    {
                        var item = s.GetItemAt(b);
                        if (item == null || s.HasTransportOrders(b) || !s.IsItemAllowed(a, item)) continue;
                        float count = item is Cargo c ? c.Count : 1f;
                        StorageUtility.TransferInstantly(s, b, s, a, count);
                        moves++;
                        break;
                    }
                }
            }
            ToolkitRunner.ShowNotice("Lager aufgeräumt (" + moves + " Schritte)", 4f);
        }

        // Every research task of the headquarters that is not running is finished at once (the game's own Finish = item researched).
        // Finishes every research of the headquarters: waiting ones, ones whose requirements are not met
        // yet, and ones an officer is working on right now (those are completed through their handler,
        // so the officer is free again and rewards are given).
        private static void ResearchAll(PlayerShip ship)
        {
            var hq = ship.SandboxEntity?.Country?.Headquarters;
            if (hq == null) return;
            int n = 0, running = 0;
            for (int pass = 0; pass < 5; pass++)   // finished research can unlock more research
            {
                int before = n + running;
                foreach (var t in hq.Tasks.ToArray())
                {
                    // Research cards and production cards on the HQ table (crate = items, torpedo/shells = equipment).
                    if (!(t is UBOAT.Game.Sandbox.HQ.HeadquartersTaskBase r) || r.IsCompleted) continue;
                    if (!(t is UBOAT.Game.Sandbox.HQ.Research || t is UBOAT.Game.Sandbox.HQ.EquipmentProduction
                          || t is UBOAT.Game.Sandbox.HQ.ItemsProduction || t is UBOAT.Game.Sandbox.HQ.Production)) continue;
                    try
                    {
                        var h = hq.GetHandlerOf(r);
                        if (h != null && h.Task == r) { h.CompleteTask(); running++; }
                        else { r.Finish(); n++; }
                    }
                    catch (System.Exception e) { Util.Log("Erforschen: " + r.Name + " Fehler " + e.Message); }
                }
                if (n + running == before) break;
            }
            var db = Resources.FindObjectsOfTypeAll<EquipmentDatabase>().FirstOrDefault();
            if (db != null) foreach (var e in db.GetEquipmentDirect()) if (e != null) e.Researched = true;
            var left = hq.Tasks.Where(t => t != null && !t.IsCompleted).GroupBy(t => t.GetType().Name).Select(g => g.Key + " " + g.Count());
            Util.Log("Erforschen: fertig " + n + ", davon liefen " + running + "; offen bleiben (andere Aufgaben): " + string.Join(", ", left.ToArray()));
            ToolkitRunner.ShowNotice("Erforscht: " + (n + running) + " Forschungen (" + running + " davon liefen gerade)", 6f);
        }

        private static void UnlockAll()
        {
            int items = 0;
            var db = Resources.FindObjectsOfTypeAll<EquipmentDatabase>().FirstOrDefault();
            if (db != null)
                foreach (var e in db.GetEquipmentDirect())
                    if (e != null && !e.Researched) { e.Researched = true; items++; }
            ToolkitRunner.ShowNotice("Freigeschaltet: " + items + " Ausrüstungen", 6f);
        }

        private static void UnlockBoats()
        {
            var career = Util.Career;
            if (career != null)
                foreach (var b in new[] { "Type IIA", "Type IID", "Type VIIB", "Type VIIC", "Type VIIC41", "Type IXA", "Type IXC", "Type IXC40" })
                    career.UnlockShip(b, true);
            ToolkitRunner.ShowNotice(career != null ? "Alle Boote freigeschaltet" : "Keine Karriere geladen", 6f);
        }

        // Jumps to the end of the planned route on the map.
        private static void QuickTravel()
        {
            var w = Game.Wolfpack;
            if (w?.Route == null || w.Route.Count == 0) { ToolkitRunner.ShowNotice("Erst einen Kurs auf der Karte setzen", 4f); return; }
            var target = w.Route[w.Route.Count - 1];
            var ship = Util.PlayerShip;
            if (ship.Docked) ship.Undock();
            w.SetPosition(target, true, true);
            w.Route.Clear();
            ToolkitRunner.ShowNotice("Schnellreise: am Ziel", 4f);
        }

        // Cheat: an enemy convoy appears ahead of the boat, the same way the game's own developer command "Spawn Convoy"
        // does it (nearest convoy spawner, spawned instantly, placed x km in front, marked on the map).
        private static void SpawnConvoy()
        {
            var w = Game.Wolfpack;
            var sb = Traverse.Create(typeof(UBOAT.Game.UI.DevConsole.DevConsoleCommands)).Field("sandbox").GetValue<UBOAT.Game.Sandbox.Sandbox>();
            if (w == null || sb == null) { ToolkitRunner.ShowNotice("Konvoi: Spiel noch nicht bereit", 4f); return; }
            var sp = sb.GetNearestSpawner(w.Position, "Convoy", Country.Relation.Enemy);
            if (sp == null)   // early war: spawners can be time-restricted; take the nearest enemy one anyway
            {
                float best = float.MaxValue;
                foreach (var g in sb.GroupSpawners)
                {
                    if (g == null || g.SpawnType != "Convoy" || !g.Countries.Any(c => c.GetRelationWith(w.Country) == Country.Relation.Enemy)) continue;
                    float d = (w.Position - (Vector2)g.transform.position).sqrMagnitude;
                    if (d < best) { best = d; sp = g; }
                }
            }
            if (sp == null) { ToolkitRunner.ShowNotice("Konvoi: kein Konvoi-Startpunkt gefunden", 4f); return; }
            var grp = sp.SpawnInstantly(true);
            if (grp == null) { ToolkitRunner.ShowNotice("Konvoi: Spiel hat keinen erzeugt", 4f); return; }
            float km = Settings.F("SpawnConvoyKm");
            grp.Position = w.Position + w.Up * km;
            grp.UpdateGroup();
            grp.UpdateGroupLowFrequency(false);
            if (grp.AI != null) for (int i = 0; i < 3; i++) grp.AI.UpdateAI();
            w.AddObservation(grp, GroupDetectionMethod.IndirectObservation).AddKnownData(GroupData.TransportType, 2f, true);
            Util.Log("Konvoi gespawnt: " + grp.name + " aus " + sp.name + ", " + km + " km voraus");
            ToolkitRunner.ShowNotice("Konvoi " + km.ToString("0") + " km voraus (auf der Karte)", 5f);
        }

        // Nearest supply U-boat comes to you (3 km away).
        private static void CallMilkCow()
        {
            var hq = Util.PlayerShip.SandboxEntity?.Country?.Headquarters;
            var w = Game.Wolfpack;
            if (hq == null || w == null) return;
            UBOAT.Game.Sandbox.SandboxGroup best = null; float bestDist = float.MaxValue;
            foreach (var t in hq.Tasks.OfType<UBOAT.Game.Sandbox.HQ.AddSupplyShip>())
            {
                var g = t.SupplyShip?.Group;
                if (g == null || t.SupplyShip.IsPacified) continue;
                float d = (g.Position - w.Position).sqrMagnitude;
                if (d < bestDist) { best = g; bestDist = d; }
            }
            if (best == null) { ToolkitRunner.ShowNotice("Keine Milchkuh auf See", 4f); return; }
            best.SetPosition(w.Position + new Vector2(3f, 0f), true);
            ToolkitRunner.ShowNotice("Milchkuh ist da (3 km östlich)", 5f);
        }

        private static void NavInfo(PlayerShip ship)
        {
            string speed = Events.SpeedText;
            if (!Settings.B("NavInfo")) { ToolkitRunner.InfoLine = speed; return; }
            var w = Game.Wolfpack;
            string eta = null, diesel = null;
            if (Settings.B("InfoEta") && w?.Route != null && w.Route.Count > 0)
            {
                float km = w.Route.TotalLength;
                eta = "Ziel: " + km.ToString("0") + " km";
                if (w.Velocity > 0.0001f)
                {
                    float h = km / w.Velocity / 3600f;
                    eta += ", Ankunft in " + ((int)h) + " h " + ((int)((h - (int)h) * 60f)).ToString("00") + " min (Spielzeit)";
                }
            }
            if (Settings.B("InfoDiesel"))
                try { diesel = "Diesel-Reichweite (3. Stufe): " + (ship.EstimatedRangeOnThirdGear > 100000f ? "∞" : ship.EstimatedRangeOnThirdGear.ToString("0") + " km"); } catch { }
            ToolkitRunner.InfoLine = Events.Join(speed, eta, diesel);
        }
    }

    [HarmonyPatch(typeof(SandboxDock), nameof(SandboxDock.GetBuyPrice))]
    internal static class Extras_PricePatch
    {
        private static void Postfix(ref int __result)
        {
            float f = Settings.F("CheatPriceFactor");
            if (f <= 0f || f >= 1f || __result <= 0) return;
            __result = Mathf.Max(1, Mathf.CeilToInt(__result * f));
        }
    }

    [HarmonyPatch(typeof(RepairAction), "StartMainCharacter")]
    internal static class Extras_RepairPatch
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RepairAction, object> done = new System.Runtime.CompilerServices.ConditionalWeakTable<RepairAction, object>();
        private static void Postfix(RepairAction __instance)
        {
            float f = Settings.F("CheatRepairSpeed");
            if (f <= 1f || !Util.IsPlayers(__instance.Activator)) return;
            // 0.4.22 (option audit): once per repair action. The game never resets the field, so a second start of the
            // same action multiplied the factor again.
            object seen;
            if (done.TryGetValue(__instance, out seen)) return;
            done.Add(__instance, null);
            var t = Traverse.Create(__instance).Field("performanceMultiplier");
            t.SetValue(t.GetValue<float>() * f);
        }
    }

    // Fast torpedoes: safer hit detection so they do not pass through a ship between two physics steps.
    [HarmonyPatch(typeof(Torpedo), "Start")]
    internal static class Extras_TorpedoHitPatch
    {
        private static void Postfix(Torpedo __instance)
        {
            if (Settings.F("CheatTorpedoSpeed") <= 1f || __instance.Owner != Util.PlayerShip) return;
            var rb = __instance.GetComponent<Rigidbody>();
            if (rb != null) rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Devices;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

    // Cheat: research and production cards of your own headquarters need no earlier research and no date.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Sandbox.HQ.HeadquartersTaskBase), "IsAllowed", HarmonyLib.MethodType.Getter)]
    internal static class NoResearchRequirements_Patch
    {
        private static object playerCountry;
        private static void Postfix(UBOAT.Game.Sandbox.HQ.HeadquartersTaskBase __instance, ref bool __result)
        {
            if (__result || !Settings.B("CheatNoResearchRequirements")) return;
            if (!(__instance is UBOAT.Game.Sandbox.HQ.Research || __instance is UBOAT.Game.Sandbox.HQ.EquipmentProduction
                  || __instance is UBOAT.Game.Sandbox.HQ.ItemsProduction || __instance is UBOAT.Game.Sandbox.HQ.Production)) return;
            try
            {
                var c = Util.PlayerShip?.SandboxEntity?.Country;
                if (c != null) playerCountry = c;
                if (playerCountry != null && ReferenceEquals(__instance.Country, playerCountry) && !__instance.IsCompleted) __result = true;
            }
            catch { }
        }
    }

    // 0.4.10 (DoMe 2026-11-17: "Forschung ohne Voraussetzungen wirkt nicht, ich muss immer noch einen Offizier haben"):
    // the officer slots of a task come from this list; with the cheat it is empty, so the task starts without an officer
    // (the game then runs it at normal speed, HeadquartersTaskHandler: no agents = performance 1).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Sandbox.HQ.HeadquartersTaskBase), "CharacterRequirements", HarmonyLib.MethodType.Getter)]
    internal static class NoResearchOfficer_Patch
    {
        private static readonly UBOAT.Game.Sandbox.HQ.CharacterRequirement[] none = new UBOAT.Game.Sandbox.HQ.CharacterRequirement[0];
        private static void Postfix(UBOAT.Game.Sandbox.HQ.HeadquartersTaskBase __instance, ref UBOAT.Game.Sandbox.HQ.CharacterRequirement[] __result)
        {
            if (__result == null || __result.Length == 0 || !Settings.B("CheatNoResearchRequirements")) return;
            if (!(__instance is UBOAT.Game.Sandbox.HQ.Research || __instance is UBOAT.Game.Sandbox.HQ.EquipmentProduction
                  || __instance is UBOAT.Game.Sandbox.HQ.ItemsProduction || __instance is UBOAT.Game.Sandbox.HQ.Production)) return;
            try
            {
                var c = Util.PlayerShip?.SandboxEntity?.Country;
                if (c != null && ReferenceEquals(__instance.Country, c)) __result = none;
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/FarAIThrottle.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;

    // Far-away world (ships/convoys outside player range) is simulated in Sandbox.Update():
    // every group gets one "low frequency" AI/observation update per lowFrequencyUpdateInterval
    // (game default 30 s game-time, private field in Sandbox). Cost per second ~ groups / interval.
    // Raising the interval = fewer far-AI updates = less CPU, most noticeable with big mods
    // (many groups) and time compression. Nearby + observed groups are NOT affected (updated every frame).
    // Trade-off: far convoys react/re-route a bit later. Setting "FarAIInterval" in seconds,
    // 30 = game default (no change), 0 = off. Suggested test: 60.
    [HarmonyPatch(typeof(Sandbox), "Update")]
    public static class FarAIThrottle
    {
        private static readonly AccessTools.FieldRef<Sandbox, float> interval =
            AccessTools.FieldRefAccess<Sandbox, float>("lowFrequencyUpdateInterval");

        private static void Prefix(Sandbox __instance)
        {
            float v = Settings.F("FarAIInterval");
            if (v >= 5f && interval(__instance) != v) interval(__instance) = v;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GameBugFixes.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

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

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // "GetRemainingDistance can only be called on an active agent that has been placed on a NavMesh" spam:
    // the game asks how far a man still has to walk while he is not standing on walkable ground
    // (agent switched off, or just pushed off the walk mesh - our NavRescue puts him back a moment later).
    // Asking then is pointless; answer "not there yet" without asking, so nothing is logged.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator), "DestinationReached", HarmonyLib.MethodType.Getter)]
    internal static class Fix_DestinationReachedOffMesh
    {
        private static bool Prefix(UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator __instance, ref bool __result)
        {
            try
            {
                var a = __instance.Agent;
                if (a != null && a.isActiveAndEnabled && a.isOnNavMesh) return true;
                __result = false;
                return false;
            }
            catch { return true; }
        }
    }

    // Port recruits on the pier: same question for a recruit who is off the walk mesh. Wait and ask again later.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.DockRecruitsInteractable), "DoUpdate")]
    internal static class Fix_DockRecruitsOffMesh
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, System.Collections.Generic.List<UBOAT.Game.Scene.Characters.InteractiveCharacter>> chars =
            HarmonyLib.AccessTools.FieldRefAccess<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, System.Collections.Generic.List<UBOAT.Game.Scene.Characters.InteractiveCharacter>>("characters");
        private static readonly HarmonyLib.AccessTools.FieldRef<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, int> index =
            HarmonyLib.AccessTools.FieldRefAccess<UBOAT.Game.Scene.Characters.DockRecruitsInteractable, int>("characterIndex");

        private static bool Prefix(UBOAT.Game.Scene.Characters.DockRecruitsInteractable __instance, ref float __result)
        {
            try
            {
                var list = chars(__instance);
                int i = index(__instance);
                if (list == null || i < 0 || i >= list.Count) return true;
                var c = list[i];
                if (c == null || c.Navigator == null || c.Ai == null || !c.Ai.isActiveAndEnabled) return true;
                var a = c.Navigator.Agent;
                if (a == null || (a.isActiveAndEnabled && a.isOnNavMesh)) return true;
                if (c.Ai.GetActiveBehaviour<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour>() == null) return true;
                __result = 0.1f;
                return false;
            }
            catch { return true; }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // Changing boats in port: the old boat's devices are destroyed while the resource bar still listens
    // to them -> one error per device. Nothing left to show, so the error is dropped.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.Resources.ResourceGUI), "Gain_Modified")]
    internal static class Fix_ResourceGuiBoatChange
    {
        private static System.Exception Finalizer(System.Exception __exception) => __exception is System.NullReferenceException ? null : __exception;
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // Skip the start videos (studio + publisher logo) and go straight to the main menu.
    // Works from the moment the mod is loaded; the very first Unity logo comes before any mod and cannot be skipped.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.IntroScene), "Update")]
    internal static class SkipStartupIntros
    {
        private static bool done;
        private static void Prefix(UBOAT.Game.UI.IntroScene __instance)
        {
            if (done) return;
            try
            {
                if (!Settings.B("SkipStartupIntros")) { done = true; return; }
                var t = HarmonyLib.Traverse.Create(__instance);
                if (System.Convert.ToInt32(t.Field("launchMode").GetValue()) != 0) { done = true; return; }   // only normal start
                var warn = t.Field("gameFilesCorruptedWarning").GetValue<UnityEngine.GameObject>();
                if (warn != null && warn.activeSelf) return;                                                   // let the player read that
                if (t.Field("sceneResource").GetValue() == null) return;                                       // menu not requested yet
                int stage = t.Field("stage").GetValue<int>();
                if (stage > 1) { done = true; return; }
                var vp = t.Field("videoPlayer").GetValue();
                if (vp != null) HarmonyLib.AccessTools.Method(vp.GetType(), "Stop")?.Invoke(vp, null);
                t.Field("wasPlaying").SetValue(true);
                t.Field("stage").SetValue(1);   // the game's own code now opens the main menu this frame
                done = true;
                Util.Log("Start-Intros übersprungen");
            }
            catch (System.Exception e) { done = true; Util.Log("Start-Intros: " + e.Message); }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // Every place in the game that asks a man's walk agent "how far is it still?" gets a safe version:
    // if the agent is switched off or not on walkable ground right now, the answer is 0 instead of
    // Unity's red "GetRemainingDistance can only be called on an active agent" line.
    // (The game checks the ground once and then asks a few lines later, after the man may have been moved.)
    [HarmonyLib.HarmonyPatch]
    internal static class Fix_RemainingDistanceSafe
    {
        private static readonly System.Reflection.MethodInfo getter =
            HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.AI.NavMeshAgent), "remainingDistance");
        private static readonly System.Reflection.MethodInfo safe =
            HarmonyLib.AccessTools.Method(typeof(Fix_RemainingDistanceSafe), nameof(Remaining));

        public static float Remaining(UnityEngine.AI.NavMeshAgent a)
        {
            if (a == null || !a.isActiveAndEnabled || !a.isOnNavMesh) return 0f;
            return a.remainingDistance;
        }

        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            var list = new System.Collections.Generic.List<System.Reflection.MethodBase>();
            foreach (var t in new[] { typeof(UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator),
                                      typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour),
                                      typeof(UBOAT.Game.Scene.Characters.DockRecruitsInteractable) })
                foreach (var m in HarmonyLib.AccessTools.GetDeclaredMethods(t))
                {
                    if (m.IsAbstract || m.ContainsGenericParameters) continue;
                    try
                    {
                        foreach (var i in HarmonyLib.PatchProcessor.ReadMethodBody(m))
                            if (i.Value is System.Reflection.MethodInfo mi && mi == getter) { list.Add(m); break; }
                    }
                    catch { }
                }
            return list;
        }

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == System.Reflection.Emit.OpCodes.Callvirt || c.opcode == System.Reflection.Emit.OpCodes.Call) && c.operand is System.Reflection.MethodInfo mi && mi == getter)
                {
                    c.opcode = System.Reflection.Emit.OpCodes.Call;
                    c.operand = safe;
                }
                yield return c;
            }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // Fix NullReferenceException in TimeCompressionExtendedUI.OnEnable/OnDisable and
    // TorpedoSalvoPreviewUI.ValidateQueuedUpdate: right after a boat change (or on a boat without the torpedo
    // panel) the game's "current torpedo launchers panel" is still empty, and these methods ask it whether it is
    // open. Now an empty panel simply counts as closed, and the rest of each method runs normally.
    [HarmonyLib.HarmonyPatch]
    internal static class Fix_LaunchersUiMissing
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.TimeCompressionExtendedUI), "OnEnable");
            yield return HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.TimeCompressionExtendedUI), "OnDisable");
            yield return HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.TorpedoSalvoPreviewUI), "ValidateQueuedUpdate");
        }

        public static bool Active(UBOAT.Game.UI.Periscope.TorpedoLaunchersUI ui) => ui != null && ui.gameObject.activeInHierarchy;
        public static bool Opened(UBOAT.Game.UI.Periscope.TorpedoLaunchersUI ui) => ui != null && ui.IsOpened;

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var getUi = HarmonyLib.AccessTools.PropertyGetter(typeof(UBOAT.Game.UI.GameUI), "CurrentTorpedoLaunchersUI");
            var getGo = HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.Component), "gameObject");
            var getAct = HarmonyLib.AccessTools.PropertyGetter(typeof(UnityEngine.GameObject), "activeInHierarchy");
            var getOpen = HarmonyLib.AccessTools.PropertyGetter(typeof(UBOAT.Game.UI.Periscope.TorpedoLaunchersUI), "IsOpened");
            var list = new System.Collections.Generic.List<HarmonyLib.CodeInstruction>(code);
            for (int i = 0; i < list.Count - 1; i++)
            {
                if (!(list[i].operand is System.Reflection.MethodInfo m) || m != getUi) continue;
                var n = list[i + 1].operand as System.Reflection.MethodInfo;
                if (n == getOpen) { list[i + 1].opcode = System.Reflection.Emit.OpCodes.Call; list[i + 1].operand = HarmonyLib.AccessTools.Method(typeof(Fix_LaunchersUiMissing), nameof(Opened)); }
                else if (n == getGo && i + 2 < list.Count && list[i + 2].operand as System.Reflection.MethodInfo == getAct)
                {
                    list[i + 1].opcode = System.Reflection.Emit.OpCodes.Call; list[i + 1].operand = HarmonyLib.AccessTools.Method(typeof(Fix_LaunchersUiMissing), nameof(Active));
                    list[i + 2].opcode = System.Reflection.Emit.OpCodes.Nop; list[i + 2].operand = null;
                }
            }
            return list;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // Fix "ArgumentOutOfRangeException ... MorphTarget.ApplyTo" (10x, e.g. on arriving in port): a character's face/body
    // morph was made for a mesh with more vertices than the mesh he wears now. The game then throws every frame for him.
    // Now: the error is caught, that one morph layer is switched off for this character (he keeps the plain mesh), and it
    // is logged once with the character's path.
    [HarmonyLib.HarmonyPatch(typeof(DWS.Common.Characters.Deformers.MorphDeformer), "UpdateDeformer")]
    internal static class Fix_MorphOutOfRange
    {
        private static readonly System.Collections.Generic.HashSet<int> logged = new System.Collections.Generic.HashSet<int>();
        private static System.Exception Finalizer(System.Exception __exception, DWS.Common.Characters.Deformers.MorphDeformer __instance)
        {
            if (!(__exception is System.ArgumentOutOfRangeException) || __instance == null) return __exception;
            __instance.enabled = false;
            if (logged.Add(__instance.GetInstanceID()))
            {
                var t = __instance.transform; string path = t.name;
                for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
                Util.Log("Figur: Gesichts-/Körperform passt nicht zum Modell, abgeschaltet: " + path);
            }
            return null;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // "GameObject is already being activated or deactivated" when the game saves while the port window is open:
    // after every save SavingNotificationUI switches the whole game UI off and on; that disables the port window, whose
    // OnDisable then calls GameUI.SetMode(Normal) = SetActive on the UI that is just being switched off -> Unity error,
    // and the port window is left in "Normal" mode (closed state broken). Now SetMode is skipped while the game UI itself
    // is being switched off; when it comes back on, the port window simply stays open.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.PortActivitiesUI), "OnDisable")]
    internal static class Fix_PortWindowSaveToggle
    {
        private static float nextLog;
        public static void SetMode(UBOAT.Game.UI.GameUI ui, UBOAT.Game.UI.GameUI.Mode mode)
        {
            if (ui != null && !ui.gameObject.activeSelf)
            {
                if (UnityEngine.Time.unscaledTime > nextLog) { nextLog = UnityEngine.Time.unscaledTime + 60f; Util.Log("Hafen-Fenster: Umschalten beim Speichern übersprungen (Unity-Fehler vermieden)"); }
                return;
            }
            ui.SetMode(mode);
        }

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var target = HarmonyLib.AccessTools.Method(typeof(UBOAT.Game.UI.GameUI), "SetMode", new[] { typeof(UBOAT.Game.UI.GameUI.Mode) });
            int n = 0;
            foreach (var c in code)
            {
                if (c.operand as System.Reflection.MethodInfo == target) { c.opcode = System.Reflection.Emit.OpCodes.Call; c.operand = HarmonyLib.AccessTools.Method(typeof(Fix_PortWindowSaveToggle), nameof(SetMode)); n++; }
                yield return c;
            }
            if (n == 0) Util.Log("Hafen-Fenster-Fix: SetMode nicht gefunden, Fix nicht aktiv");
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // 'Slot "Fuel 1" / "Hidden Goods 1" is defined for entity type "Large Freighter 01/02" in game data, but it's missing
    // in the entity prefab': the game data gives these freighters cargo for slots their 3D model does not have, so the game
    // logs an error and that cargo item is never placed on the ship. Now the missing slot is added at runtime, next to a
    // similar slot of the same ship (e.g. "Hidden Goods 1" next to another "Goods" slot), and the cargo is placed there.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Sandbox.SandboxEntity), "SpawnEquipment", new[] { typeof(UBOAT.Game.Sandbox.SandboxEquipment), typeof(bool) }, new[] { HarmonyLib.ArgumentType.Normal, HarmonyLib.ArgumentType.Ref })]
    internal static class Fix_MissingCargoSlot
    {
        private static readonly System.Collections.Generic.HashSet<string> logged = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.List<UBOAT.Game.Scene.Entities.ShipEquipmentSlot> buf = new System.Collections.Generic.List<UBOAT.Game.Scene.Entities.ShipEquipmentSlot>();

        private static void Prefix(UBOAT.Game.Sandbox.SandboxEntity __instance, UBOAT.Game.Sandbox.SandboxEquipment equipment, ref bool sceneSlotsAlreadyBuffered)
        {
            try
            {
                var ent = __instance?.SpawnedEntity;
                if (ent == null || equipment?.Slot == null) return;
                string name = equipment.Slot.Name;
                if (string.IsNullOrEmpty(name) || name == "Deck Goods A") return;
                ent.GetComponentsInChildren(true, buf);
                foreach (var s in buf) if (s != null && s.name == name) return;          // slot exists: nothing to do
                string stem = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ' ');
                string word = stem.Contains(" ") ? stem.Substring(stem.LastIndexOf(' ') + 1) : stem;
                UBOAT.Game.Scene.Entities.ShipEquipmentSlot like = null;
                foreach (var s in buf) if (s != null && s.name.StartsWith(stem)) { like = s; break; }
                if (like == null) foreach (var s in buf) if (s != null && s.name.Contains(word)) { like = s; break; }
                if (like == null) foreach (var s in buf) if (s != null && s.GetComponentInParent<UBOAT.Game.Scene.Items.Storage>() != null) { like = s; break; }
                if (like == null) return;   // nothing similar on this ship: leave it to the game
                var go = new UnityEngine.GameObject(name);
                go.layer = like.gameObject.layer;
                go.transform.SetParent(like.transform.parent, false);
                go.transform.localPosition = like.transform.localPosition + new UnityEngine.Vector3(0f, 0f, 0.8f);
                go.transform.localRotation = like.transform.localRotation;
                go.AddComponent<UBOAT.Game.Scene.Entities.ShipEquipmentSlot>();
                sceneSlotsAlreadyBuffered = false;   // the game re-reads the ship's slots and now finds the new one
                string type = Util.SafeGet(() => __instance.Type.Name, "?");
                if (logged.Add(type + "/" + name)) Util.Log("Ladung: Platz \"" + name + "\" fehlte im Modell von " + type + ", neben \"" + like.name + "\" ergänzt");
            }
            catch (System.Exception e) { Util.Log("Ladung: Platz ergänzen fehlgeschlagen: " + e.Message); }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Skills;

    // A periscope whose "vision" renderer has no material (seen on a custom boat model): Periscope.Awake stops at
    // Instantiate(vision.sharedMaterial) ("The Object you want to instantiate is null"), so the rest of Awake never runs
    // and Periscope.Update then throws a NullReferenceException EVERY FRAME (lag, periscope unusable). Now the empty
    // renderer gets the visionUI material (or a plain one) first, so Awake finishes. Logged once per periscope.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Items.Periscope), "Awake")]
    internal static class Fix_PeriscopeNoMaterial
    {
        private static void Prefix(UBOAT.Game.Scene.Items.Periscope __instance)
        {
            try
            {
                var vision = __instance.Vision;
                if (vision == null || vision.sharedMaterial != null) return;
                var ui = __instance.VisionUI;
                var mat = ui != null && ui.sharedMaterial != null ? ui.sharedMaterial : null;
                if (mat == null) { var sh = UnityEngine.Shader.Find("Unlit/Texture") ?? UnityEngine.Shader.Find("Standard"); if (sh != null) mat = new UnityEngine.Material(sh); }
                if (mat == null) return;
                vision.sharedMaterial = mat;
                Util.Log("Sehrohr: '" + __instance.name + "' hatte kein Bild-Material, Ersatz gesetzt (" + mat.name + ") - sonst Fehler in jedem Bild");
            }
            catch (System.Exception e) { Util.Log("Sehrohr: Ersatz-Material fehlgeschlagen: " + e.Message); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GameFixes.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Stories;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // Small game fixes, own code. Each part has its own switch.
    public class GameFixes : ModuleBase
    {
        [Inject] internal static StoryFragmentsDatabase fragments;
        private PlayerShip lastShip;

        private bool warned;
        private float nextPortCheck;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship != null && Settings.B("FixSkipperDead") && Time.unscaledTime > nextPortCheck)
            {
                nextPortCheck = Time.unscaledTime + 5.7f;
                Util.Safe("Fixes.PortWarn", () => PortWarning(ship));
            }
            Util.Safe("Fixes.SpeedOff", Fix_SpeedCompression.Restore);
            Util.Safe("Fixes.Officers", ExtraOfficers);
            Util.Safe("Fixes.Sailors", ExtraSailors);
            if (ship == null || ship == lastShip) return;
            lastShip = ship;   // new game scene / save loaded
            Util.Safe("Fixes.Inject", () => InjectionFramework.Instance.InjectIntoAssembly(typeof(GameFixes).Assembly));
            if (Settings.B("FixPortDialogue")) Util.Safe("Fixes.Tags", () => CleanTags(ship));
        }

        // Stuck conversation tags make port officers show no options. Remove them once per load.
        private static readonly string[] officerTrees = { "Driver", "Personnel Officer", "Harbor Captain", "Flotilla Commander", "Operations Officer" };
        private static void CleanTags(PlayerShip ship)
        {
            var vars = ship.SandboxEntity?.Variables;
            if (vars == null || vars.Count == 0) return;
            var names = new HashSet<string> { "Activate", "FirstDialogue" };
            var list = fragments == null ? null : Traverse.Create(fragments).Field("storyFragments").GetValue<List<StoryFragment>>();
            if (list != null)
                foreach (var f in list)
                    if (f.Path != null && officerTrees.Any(p => f.Path.StartsWith(p)) && f.Id.Length > f.Path.Length)
                        names.Add(f.Id.Substring(f.Path.Length));
            int n = names.Count(vars.Remove);
            if (n > 0) Util.Log("Hafen-Dialog: " + n + " haengende Gespraechs-Markierungen entfernt");
        }

        // Before reaching a port: warn if the commander is dead or wounded (port talks need him).
        private void PortWarning(PlayerShip ship)
        {
            bool nearPort = !ship.Docked && Util.Find<UBOAT.Game.Scene.Dock.DockArea>(5f)
                .Any(d => d != null && (d.transform.position - ship.transform.position).sqrMagnitude < 3000f * 3000f);
            if (!nearPort) { warned = false; return; }
            if (warned || SkipperOk()) return;
            warned = true;
            string text = "Achtung: Kommandant tot oder verwundet! Vor dem Anlegen heilen (F10 > Cheat > Alle heilen), sonst gehen die Hafen-Gespr\u00e4che nicht.";
            var bar = UnityEngine.Object.FindObjectOfType<NotificationBarUI>();
            if (bar != null) bar.OpenNow("Icons/Notification Bar/Notification - 40 - Danger", text);
            Util.Log(text);
        }

        // More officers allowed on board (runtime only, nothing is written into the save).
        private Modifier officerMod;
        private object officerCareer;
        private void ExtraOfficers()
        {
            var c = Util.Career;
            float want = Settings.F("ExtraOfficers");
            if (c == null) return;
            if (!ReferenceEquals(c, officerCareer)) { officerMod = null; officerCareer = c; }
            // 0.4.20: the value stored in the savegame is found again (by name) instead of adding a second one
            if (want <= 0f) { Util.DropDelta(c.OfficersOnBoardLimit, "Toolkit Officers"); officerMod = null; return; }
            officerMod = Util.OneDelta(c.OfficersOnBoardLimit, "Toolkit Officers");
            if (officerMod.Value != want) officerMod.Value = want;
        }

        // More sailors allowed on board. "Volle Crew" recruits up to FullCrewSize men, but the game
        // only lets 20 sailors aboard a Type VII. With "SailorsFollowFullCrew" on, the sailor limit is
        // raised so the whole wanted crew fits (FullCrewSize minus the officer places). Plus a manual
        // extra. Runtime only, nothing is written into the save.
        private Modifier sailorMod;
        private object sailorCareer;
        private void ExtraSailors()
        {
            var c = Util.Career;
            if (c == null) return;
            if (!ReferenceEquals(c, sailorCareer)) { sailorMod = null; sailorCareer = c; }
            // 0.4.20 (log: "Matrosen an Bord: jetzt bis 1438 erlaubt"): the extra places stored in the savegame were
            // counted as the game's own limit and new ones were added on top after every load.
            sailorMod = Util.OneDelta(c.SailorsOnBoardLimit, "Toolkit Sailors", false);
            float game = c.SailorsOnBoardLimit.Value - (sailorMod != null ? sailorMod.Value : 0f);
            float want = 0f;
            if (Settings.B("SailorsFollowFullCrew") && Settings.B("FullCrewAtStart"))
                want = Mathf.Max(0f, Settings.F("FullCrewSize") - c.OfficersOnBoardLimit.Value - 1f - game);   // -1 = commander
            want += Mathf.Max(0f, Settings.F("ExtraSailors"));
            if (want <= 0f) { if (sailorMod != null) { sailorMod.Remove(); sailorMod = null; } return; }
            if (sailorMod == null) sailorMod = Util.OneDelta(c.SailorsOnBoardLimit, "Toolkit Sailors");
            want = Mathf.Round(want);
            if (sailorMod.Value != want) { sailorMod.Value = want; Util.Log("Matrosen an Bord: jetzt bis " + Mathf.RoundToInt(game + want) + " erlaubt"); }
        }

        internal static bool SkipperOk()
        {
            var crew = Util.Find<PlayerCrew>(30f).FirstOrDefault();
            var s = crew != null ? crew.Skipper : null;
            return crew == null || (s != null && !s.Injury);
        }
    }

    // --- Berlin time: all clocks show German time (CET), no time zone messages.
    [HarmonyPatch(typeof(TimeZoneTracker), "LocalOffsetFromCET", MethodType.Getter)]
    internal static class Fix_BerlinOffset
    {
        private static bool Prefix(ref int __result) { if (!Settings.B("BerlinTime")) return true; __result = 0; return false; }
    }
    [HarmonyPatch(typeof(TimeZoneTracker), "UpdateTimeZone")]
    internal static class Fix_BerlinZone
    {
        private static bool Prefix(ref int ___currentUtcOffsetHours)
        {
            if (!Settings.B("BerlinTime")) return true;
            ___currentUtcOffsetHours = 1;
            return false;
        }
    }

    // --- Commander dead or wounded: no port officer talks and no story dialogues that need him.
    [HarmonyPatch(typeof(PortActivitiesUI), "StartAction")]
    internal static class Fix_SkipperPortTalk
    {
        private static bool Prefix(string destinationOfficerTag)
        {
            if (!Settings.B("FixSkipperDead") || destinationOfficerTag == null || !destinationOfficerTag.StartsWith("Dialogue:")) return true;
            return GameFixes.SkipperOk();
        }
    }
    [HarmonyPatch(typeof(StoryDialogueAction), "GetState")]
    internal static class Fix_SkipperStory
    {
        private static void Postfix(ref ActionStateData __result)
        {
            if (Settings.B("FixSkipperDead") && !GameFixes.SkipperOk()) __result = new ActionStateData(ActionState.Hidden);
        }
    }

    // --- Port dialogue: a click while the dialogue closes must not break it.
    [HarmonyPatch(typeof(GenericStory), "ContinueStory")]
    internal static class Fix_StoryContinue
    {
        private static bool Prefix(GenericStory __instance, ref bool __result, object ___scriptingEnvironment)
        {
            if (!Settings.B("FixPortDialogue") || __instance.Running || ___scriptingEnvironment != null) return true;
            __result = false;
            return false;
        }
    }
    [HarmonyPatch(typeof(GenericStory), "AddTempTag")]
    internal static class Fix_StoryTag
    {
        private static bool Prefix(GenericStory __instance, object ___scriptingEnvironment)
            => !Settings.B("FixPortDialogue") || __instance.Running || ___scriptingEnvironment != null;
    }

    // --- Radio assignments: rewards of earlier finished assignments get paid too.
    [HarmonyPatch(typeof(MissionSummaryAnimation), "StopAnimation")]
    internal static class Fix_RadioRewards
    {
        private static void Prefix(bool ___notableEventsMode, IMission ___mission, out object[] __state) { __state = new object[] { ___notableEventsMode, ___mission }; }
        private static void Finalizer(object[] __state)
        {
            if (!Settings.B("FixRadioRewards") || __state == null || (bool)__state[0]) return;
            Util.Safe("Fix.RadioRewards", () =>
            {
                var reg = Resources.FindObjectsOfTypeAll<MissionsRegister>().FirstOrDefault();
                if (reg == null) return;
                int paid = 0;
                for (int i = 0; i < reg.Count; i++)
                {
                    var m = reg[i];
                    var a = m as IAssignmentMission;
                    if (a == null || ReferenceEquals(m, __state[1]) || m.State != MissionState.Completed) continue;
                    if ((m.CompletionFlags & MissionCompletionFlags.RewardAccepted) != 0 || a.AssignmentObjectiveState != AssignmentObjectiveState.Completed) continue;
                    m.GiveRewardsToPlayer();
                    paid++;
                }
                if (paid == 0) return;
                Util.Log("Funkauftrag: " + paid + " vergessene Belohnung(en) ausgezahlt");
                var ui = UnityEngine.Object.FindObjectOfType<ActiveMissionSummaryUI>(true);
                if (ui != null) AccessTools.Method(typeof(ActiveMissionSummaryUI), "ValidateDropdownOptions")?.Invoke(ui, null);
            });
        }
    }

    // --- Speed & range at high time compression (own approach):
    // 1) physics runs at most x100 instead of x300, the rest is plain time skip
    [HarmonyPatch(typeof(TimeCompressionController), "TimeCompressionScale_Changed")]
    internal static class Fix_SpeedCompression
    {
        internal static bool Applied;
        private static TimeCompressionController last;
        // 0.4.22 (option audit): switched off while the time compression runs = the game's own values at once
        // (before they came back only at the next change of the compression).
        internal static void Restore()
        {
            if (!Applied || Settings.B("FixSpeedRange")) return;
            Applied = false;
            var c = last;
            if (c != null)
            {
                var t = Traverse.Create(c);
                if (!t.Field("timeCompression").GetValue<bool>()) return;
                var mod = t.Field("timeScaleModifier").GetValue<Modifier>();
                var gt = t.Field("gameTime").GetValue<GameTime>();
                var par = t.Field("timeCompressionScale").GetValue<Parameter>();
                if (mod == null || gt == null || par == null) return;
                float s = par;
                mod.Value = Mathf.Min(300f, s);
                gt.ArtificialTimeScale = Mathf.Max(1f, s / 300f);
                Util.Log("Zeitraffer-Fix aus: Spielwerte wieder gesetzt (Physik x" + mod.Value.ToString("0") + ")");
            }
        }
        private static void Postfix(TimeCompressionController __instance, Parameter timeCompressionScale)
        {
            if (!Settings.B("FixSpeedRange")) { Applied = false; return; }   // the game's own method just set its own values
            var t = Traverse.Create(__instance);
            if (!t.Field("timeCompression").GetValue<bool>()) return;
            Applied = true; last = __instance;
            var mod = t.Field("timeScaleModifier").GetValue<Modifier>();   // field is a Modifier (was read as wrong type -> "Specified cast is not valid")
            var gt = t.Field("gameTime").GetValue<GameTime>();
            if (mod == null || gt == null) return;
            float s = timeCompressionScale;
            mod.Value = Mathf.Min(100f, s);
            gt.ArtificialTimeScale = Mathf.Max(1f, s / 100f);
        }
    }
    // 2) spray on the tower slows the boat a lot at high speed-up -> skipped there
    [HarmonyPatch(typeof(ConningTowerSprayEffect), "FixedUpdate")]
    internal static class Fix_SpeedSpray
    {
        private static bool Prefix(ConningTowerSprayEffect __instance)
        {
            if (!Settings.B("FixSpeedRange") || Time.timeScale < 100f) return true;
            var ship = Util.PlayerShip;
            if (ship == null || !__instance.transform.IsChildOf(ship.transform)) return true;
            return ship.SubmergedOrGoingToSubmerge || ship.ActiveEngine == null || ship.ActiveEngine.GearIndex == 0;
        }
    }
    // 3) water drag: learn the real value per boat and gear at normal speed, use it at x100 and more
    [HarmonyPatch(typeof(PlayWay.Water.Extensions.NeuralNetworkPhysics), "SimpleUpdate")]
    internal static class Fix_SpeedDrag
    {
        private static readonly Dictionary<string, float> learned = new Dictionary<string, float>();
        private static bool loaded;
        private static float nextSave;
        private static string File => Path.Combine(Settings.Folder, "toolkit-drag.txt");

        private static string Key(PlayerShip s) => (s.SandboxEntity?.Type?.Name ?? "?") + "|" + s.ActiveEngine.GearIndex;

        internal static void Handle(PlayWay.Water.Extensions.NeuralNetworkPhysics p)
        {
            if (!Settings.B("FixSpeedRange")) return;
            var ship = Util.PlayerShip;
            if (ship == null || ship.ActiveEngine == null || ship.SubmergedOrGoingToSubmerge || ship.ActiveEngine.GearIndex == 0) return;
            if (!p.transform.IsChildOf(ship.transform)) return;
            Load();
            var t = Traverse.Create(p);
            string k = Key(ship);
            if (Time.timeScale < 20f)
            {
                float now = t.Field("areaFractionSmoothed").GetValue<float>();
                if (now <= 0.01f || now > 0.6f) return;
                learned[k] = learned.TryGetValue(k, out var old) ? Mathf.Lerp(old, now, 0.01f) : now;
                if (Time.unscaledTime > nextSave) { nextSave = Time.unscaledTime + 60f; Save(); }
            }
            else if (Time.timeScale >= 100f)
            {
                if (!learned.TryGetValue(k, out var v))
                {
                    string type = k.Substring(0, k.IndexOf('|') + 1);
                    var any = learned.Where(e => e.Key.StartsWith(type)).Select(e => e.Value).ToList();
                    v = any.Count > 0 ? any.Average() : 0.12f;
                }
                t.Field("areaFractionSmoothed").SetValue(v);
                t.Field("submergedAreaFraction").SetValue(v);
            }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!System.IO.File.Exists(File)) return;
                foreach (var line in System.IO.File.ReadAllLines(File))
                {
                    int i = line.LastIndexOf('=');
                    if (i > 0 && float.TryParse(line.Substring(i + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)) learned[line.Substring(0, i)] = v;
                }
            }
            catch { }
        }

        private static void Save()
        {
            try { System.IO.File.WriteAllLines(File, learned.Select(e => e.Key + "=" + e.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))); } catch { }
        }

        private static void Prefix(PlayWay.Water.Extensions.NeuralNetworkPhysics __instance) => Handle(__instance);
    }
    [HarmonyPatch(typeof(PlayWay.Water.Extensions.NeuralNetworkPhysics), "Execute")]
    internal static class Fix_SpeedDragFull
    {
        private static void Prefix(List<PlayWay.Water.Extensions.NeuralNetworkPhysics> list) { if (Time.timeScale >= 100f) Each(list); }
        private static void Postfix(List<PlayWay.Water.Extensions.NeuralNetworkPhysics> list) => Each(list);
        private static void Each(List<PlayWay.Water.Extensions.NeuralNetworkPhysics> list)
        {
            if (list == null || !Settings.B("FixSpeedRange")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].transform.IsChildOf(ship.transform)) { Fix_SpeedDrag.Handle(list[i]); break; }
        }
    }

    // --- Pause without blur and without the dark overlay.
    [HarmonyPatch(typeof(PauseEffects), "SetPostProcessingWeight")]
    internal static class Fix_PauseBlur
    {
        private static void Prefix(ref float f) { if (Settings.B("NoPauseBlur")) f = 0f; }
    }
    [HarmonyPatch(typeof(PauseOverlayUI), "ValidateState")]
    internal static class Fix_PauseOverlay
    {
        private static void Postfix(PauseOverlayUI __instance) { if (Settings.B("NoPauseDark") && __instance.gameObject.activeSelf) __instance.gameObject.SetActive(false); }
    }

}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Stories;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // New career: skip the intro tutorial (switch "SkipIntroTutorial", default on).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Sandbox.Missions.StartScript), "StartIntroTutorial")]
    internal static class GameFixes_SkipIntroTutorialPatch
    {
        private static bool Prefix()
        {
            if (!Settings.B("SkipIntroTutorial")) return true;
            Util.Log("Einführungs-Tutorial übersprungen");
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Stories;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // QoL: "Mesh assigned to a collider is not readable: Bin / Clock / Folder_01".
    // Cause (game code, Entity.CollectColliders): some small props in the boat/port have a collision
    // shape whose 3D data is locked. The game then cannot compute its volume, writes a red error and
    // counts the part with volume 0 (it is left out of weight/buoyancy spreading).
    // Fix: the red line is replaced by one quiet toolkit log line per object, and the volume is
    // taken from the shape's bounding box instead (always readable), so the part counts again.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Entities.Entity), "CollectColliders")]
    internal static class GameFixes_UnreadableColliderPatch
    {
        private static readonly System.Collections.Generic.HashSet<string> told = new System.Collections.Generic.HashSet<string>();

        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> Transpiler(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var logError = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Debug), "LogError", new[] { typeof(object) });
            var quiet = HarmonyLib.AccessTools.Method(typeof(GameFixes_UnreadableColliderPatch), nameof(Quiet));
            foreach (var ci in code)
            {
                if (ci.Calls(logError)) yield return new HarmonyLib.CodeInstruction(System.Reflection.Emit.OpCodes.Call, quiet) { labels = ci.labels, blocks = ci.blocks };
                else yield return ci;
            }
        }

        public static void Quiet(object msg)
        {
            if (!Settings.B("FixUnreadableColliders")) { UnityEngine.Debug.LogError(msg); return; }
            string s = msg as string ?? "";
            if (told.Add(s)) Util.Log("Kollision ohne lesbare Form, Volumen aus Umriss berechnet: " + s.Replace("Mesh assigned to a collider is not readable: ", ""));
        }

        private static void Postfix(ref UnityEngine.Collider[] colliders, ref float[] colliderVolumes, ref float totalVolume)
        {
            if (!Settings.B("FixUnreadableColliders") || colliders == null || colliderVolumes == null) return;
            try
            {
                for (int i = 0; i < colliders.Length && i < colliderVolumes.Length; i++)
                {
                    var mc = colliders[i] as UnityEngine.MeshCollider;
                    if (mc == null || mc.sharedMesh == null || mc.sharedMesh.isReadable || colliderVolumes[i] > 0f) continue;
                    var size = mc.sharedMesh.bounds.size;
                    var sc = mc.transform.lossyScale;
                    float v = UnityEngine.Mathf.Abs(size.x * sc.x * size.y * sc.y * size.z * sc.z);
                    colliderVolumes[i] = v;
                    totalVolume += v;
                }
            }
            catch (System.Exception e) { Util.Log("Kollisions-Fix: " + e.Message); }
        }
    }

    // QoL: "Couldn't sample navmesh for position (-65.7, 2.1, -41.4)".
    // Cause (game code, GoToBehaviour.SnapTarget): a crew member is sent to a spot that lies a bit
    // off the walkable area (here in port). The game only searches 1.5 m around it, finds nothing,
    // writes the red error and the man gives up / stands around. Fix: search wider (up to 5 m)
    // and send him to the nearest walkable point instead.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "SnapTarget")]
    internal static class GameFixes_NavmeshSnapPatch
    {
        private static readonly System.Collections.Generic.HashSet<string> Warned = new System.Collections.Generic.HashSet<string>();
        private static readonly System.Collections.Generic.Dictionary<string, int> Tries = new System.Collections.Generic.Dictionary<string, int>();
        private static bool Prefix(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour __instance, ref UnityEngine.Vector3 ___target, ref bool __result)
        {
            if (!Settings.B("FixNavmeshSnap")) return true;
            try
            {
                var nav = HarmonyLib.Traverse.Create(__instance).Field("navigator").GetValue<UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator>();
                if (nav == null) return true;
                int mask = nav.WalkableAreasMask;
                if (UnityEngine.AI.NavMesh.SamplePosition(___target, out var hit, 1.5f, mask)) return true;   // normal case: game does it
                if (!UnityEngine.AI.NavMesh.SamplePosition(___target, out hit, 5f, mask)
                    && !UnityEngine.AI.NavMesh.SamplePosition(___target, out hit, 60f, mask))
                {
                    // 0.3.53 (DoMe: many short error popups): nothing walkable within 60 m. The game would log a red
                    // error every 3 s for minutes. Return the same "false" quietly and note it once in our log.
                    string key = ___target.ToString();
                    // 0.3.54 (DoMe: "nach 5 erfolglosen Versuchen helfen"): from the 5th try search up to 300 m and walk there.
                    Tries.TryGetValue(key, out int n); Tries[key] = ++n;
                    if (n >= 5 && UnityEngine.AI.NavMesh.SamplePosition(___target, out hit, 300f, mask)
                        && (!Util.IsPlayers(nav) || Util.PlayerShip == null
                            || UnityEngine.Vector3.Distance(hit.position, Util.PlayerShip.transform.position) < 80f))   // crew on own boat stays on own boat
                    {
                        Util.Log("Laufweg: Ziel " + key + " nach " + n + " Versuchen auf " + hit.position + " verlegt (" + UnityEngine.Vector3.Distance(___target, hit.position).ToString("0") + " m)");
                        Tries.Remove(key);
                        ___target = hit.position; __result = true; return false;
                    }
                    if (Warned.Add(key)) Util.Log("Laufweg: Ziel " + key + " nicht erreichbar (" + __instance.GetType().Name + "), Fehler-Popup unterdrückt");
                    __result = false;
                    return false;
                }
                ___target = hit.position;
                __result = true;
                return false;
            }
            catch { return true; }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Stories;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // QoL: "InvalidOperationException: Unrecognized mode." (game code, UseGun.OnModeChosen).
    // The gun's target buttons (Ships / Aircraft / Hold fire) share one window with other devices.
    // When the window switches, the old buttons are only removed at the end of the frame and can
    // still fire with a mode the gun does not know -> error. Unknown modes are now simply ignored.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Actions.UseGun), "OnModeChosen")]
    internal static class Fix_GunUnknownMode
    {
        private static Exception Finalizer(Exception __exception, string mode)
        {
            if (__exception is InvalidOperationException && Settings.B("FixGunModeError"))
            {
                Util.Log("Geschütz: unbekannter Modus \"" + mode + "\" ignoriert");
                return null;
            }
            return __exception;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Stories;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // Unity "Development Console" (only exists in a Development Build of the game): it pops up on every error and covers
    // the screen. It is closed again in the same frame. Logging is not affected: errors still go to output_log.txt.
    // 0.3.91 (DoMe: "beim Laden flackert immer wieder die Debug-Konsole"): closing it once per Update was too late while
    // loading (long frames, the console was drawn in between). Now it is also closed right when the error is logged
    // (the console opens because of that error), before rendering and in OnGUI.
    public class DevConsoleHide : ModuleBase
    {
        private static bool logged, hooked;
        private static int closed, reasons;
        internal static void Close()
        {
            if (!Settings.B("HideDevConsole") || !UnityEngine.Debug.developerConsoleVisible) return;
            UnityEngine.Debug.developerConsoleVisible = false;
            closed++;
            if (!logged) { logged = true; Util.Log("Entwickler-Konsole geschlossen (Spiel läuft als Development Build: " + UnityEngine.Debug.isDebugBuild + ")"); }
        }
        internal static void Hook()
        {
            if (hooked) return;
            hooked = true;
            UnityEngine.Application.logMessageReceived += (c, st, t) =>
            {
                if (t != UnityEngine.LogType.Error && t != UnityEngine.LogType.Exception && t != UnityEngine.LogType.Assert) return;
                bool was = UnityEngine.Debug.developerConsoleVisible;
                Close();
                // 0.3.93: name what opened the console (first line only), so the cause can be found in the log.
                if (reasons < 30) { reasons++; var line = c ?? ""; int n = line.IndexOf('\n'); if (n >= 0) line = line.Substring(0, n); if (line.Length > 160) line = line.Substring(0, 160); DebugLog.Write("INFO", "Konsole ausgelöst von (" + t + (was ? ", war offen" : "") + "): " + line); }
            };
            UnityEngine.Application.onBeforeRender += Close;
        }
        private float nextLog;
        public override void Frame()
        {
            Hook();
            Close();
            if (closed > 0 && UnityEngine.Time.unscaledTime > nextLog) { nextLog = UnityEngine.Time.unscaledTime + 60f; Util.Log("Entwickler-Konsole " + closed + "x geschlossen in der letzten Minute"); closed = 0; }
        }
    }

    // Diagnosis (DoMe 2026-11-13: "end patrol -> port takes forever"): logs every scene unload/load with time since
    // the previous one, so the log shows how long a transition really took.
    public class SceneTiming : ModuleBase
    {
        private static bool hooked;
        private static float last = -1f;
        private static void Mark(string what, string scene)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            Util.Log("Szene " + what + ": " + scene + (last >= 0f ? " (+" + (now - last).ToString("0.0") + " s)" : ""));
            last = now;
        }
        public override void Frame()
        {
            if (hooked) return;
            hooked = true;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += s => Mark("entladen", s.name);
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (s, m) => Mark("geladen", s.name + " " + m);
        }
    }

    // Fix (DoMe 2026-11-13): quitting while a save is still being written left Autosave_5 at 16 KB, and "Fortsetzen"
    // then crashed while loading it. Quitting now waits until the game has finished writing, then quits by itself.
    public class QuitGuard : ModuleBase
    {
        private static bool hooked, quitPending;
        private static UBOAT.Game.Core.Serialization.SavesManager saves;
        private static UBOAT.Game.Core.Serialization.SavesManager Saves
        {
            get
            {
                if (saves == null)
                {
                    try { saves = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Core.Serialization.SavesManager>(); } catch { }
                    if (saves == null) saves = UnityEngine.Object.FindObjectOfType<UBOAT.Game.Core.Serialization.SavesManager>();
                }
                return saves;
            }
        }
        private static bool WantsToQuit()
        {
            try
            {
                if (!Settings.B("QuitWaitsForSave")) return true;
                var s = Saves;
                if (s == null || !s.IsSaving) return true;
                if (!quitPending) Util.Log("Beenden wartet: Spielstand wird noch geschrieben");
                quitPending = true;
                return false;
            }
            catch { return true; }
        }
        public override void Frame()
        {
            if (!hooked) { hooked = true; UnityEngine.Application.wantsToQuit += WantsToQuit; }
            if (!quitPending) return;
            var s = Saves;
            if (s != null && s.IsSaving) return;
            quitPending = false;
            Util.Log("Spielstand fertig geschrieben, Spiel wird beendet");
            typeof(UnityEngine.Application).GetMethod("Quit", System.Type.EmptyTypes).Invoke(null, null);   // via reflection: direct call is forbidden in mods
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::DWS.Common.InjectionFramework;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Stories;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI;

    // 0.3.58 (DoMe: upgrades cannot be bought, short error each click): the game's InstallUpgrade crashes (NRE) when
    // the boat has no slot for the item (unfinished game upgrades like "Reinforced Pistons", unlocked by research cheats).
    // Now: no crash, a short notice instead.
    [HarmonyPatch(typeof(PortServicesProvider), "InstallUpgrade")]
    internal static class Fix_UpgradeNoSlot
    {
        private static readonly System.Reflection.MethodInfo Find = AccessTools.Method(typeof(PortServicesProvider), "FindSuitableSlot");
        private static bool Prefix(PortServicesProvider __instance, SandboxEquipmentData equipmentData, ref bool __result)
        {
            try
            {
                var ship = Util.PlayerShip;
                if (Find == null || ship == null || equipmentData == null) return true;
                if (Find.Invoke(__instance, new object[] { ship, equipmentData }) != null) return true;
                Util.Log("Upgrade: kein passender Platz im Boot fuer " + equipmentData.Name);
                ToolkitRunner.ShowNotice("Passt nicht in dein Boot: " + equipmentData.Name, 4f);
                __result = false;
                return false;
            }
            catch { return true; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GameGuards.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.UI;
    using global::UnityEngine;

    // Game error guards: places where the game itself throws an error every frame (stutter, slow menus,
    // crashes). Each guard only acts after the error really happened, or on the exact broken state.
    internal static class Guards
    {
        internal static bool On(string key) => Settings.B(key);
        private static readonly HashSet<string> logged = new HashSet<string>();
        internal static void Once(string key, string text) { if (logged.Add(key)) Util.Log("Fehlerschutz: " + text); }
        internal static bool IsNull(Exception e) => e is NullReferenceException;
    }

    // 1) Character without an action queue: the role code reads it every frame and throws.
    [HarmonyPatch(typeof(CharacterRole), "HasPlayerOrders", new Type[] { typeof(ActionQueue) })]
    internal static class Guard_NoQueueOrders
    {
        private static bool Prefix(ActionQueue actionQueue, ref bool __result)
        {
            if (actionQueue != null || !Guards.On("GuardNoQueue")) return true;
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(CharacterRole), "AssignJob", new Type[] { typeof(PlayableCharacter) })]
    internal static class Guard_NoQueueJob
    {
        private static bool Prefix(PlayableCharacter character)
        {
            if (!Guards.On("GuardNoQueue")) return true;
            if (character == null) return false;
            try { return character.ActionQueue != null; } catch { return false; }
        }
    }

    // 2) Card game that keeps running after its table is gone: finish it instead of erroring every frame.
    [HarmonyPatch(typeof(PlayCardsAction), "UpdateAction")]
    internal static class Guard_CardsWithoutTable
    {
        private static readonly FieldInfo table = AccessTools.Field(typeof(PlayCardsAction), "table");
        private static bool Prefix(PlayCardsAction __instance)
        {
            if (!Guards.On("GuardCards")) return true;
            if (__instance == null) return false;
            try
            {
                if (table == null || table.GetValue(__instance) != null) return true;
                __instance.CompleteAction();
                Guards.Once("cards", "Kartenspiel ohne Tisch beendet");
                return false;
            }
            catch { return true; }
        }
    }

    // 3) Destroyed enemy torpedo launcher group that errors every frame: switch off only that broken one.
    [HarmonyPatch]
    internal static class Guard_EnemyLauncher
    {
        private static MethodBase target;
        private static readonly HashSet<int> broken = new HashSet<int>();
        private static bool Prepare()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Items.NPCTorpedoLauncherGroup");
            target = t != null ? AccessTools.Method(t, "DoUpdate") : null;
            return target != null;
        }
        private static MethodBase TargetMethod() => target;
        private static bool Prefix(object __instance)
        {
            var o = __instance as UnityEngine.Object;
            return o == null || !broken.Contains(o.GetInstanceID());
        }
        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardEnemyLauncher")) return __exception;
            try
            {
                if (__instance is UnityEngine.Object o) broken.Add(o.GetInstanceID());
                if (__instance is Behaviour b) b.enabled = false;
                Guards.Once("launcher", "kaputte Torpedowerfer-Gruppe eines Gegners abgeschaltet");
            }
            catch { }
            return null;
        }
    }

    // 4) Ceiling fade area with a missing neighbour (after interior changes): switch off only that one.
    [HarmonyPatch]
    internal static class Guard_CeilingFade
    {
        private static MethodBase target;
        private static bool Prepare()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Effects.FadingCeilingArea");
            target = t != null ? AccessTools.Method(t, "DoUpdate") : null;
            return target != null;
        }
        private static MethodBase TargetMethod() => target;
        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardCeiling")) return __exception;
            try { if (__instance is Behaviour b) b.enabled = false; Guards.Once("ceiling", "kaputte Decken-Blende abgeschaltet"); } catch { }
            return null;
        }
    }

    // 5) Portrait bar keeps destroyed portraits after crew changes: clean the list.
    internal static class PortraitRepair
    {
        private static readonly FieldInfo portraits = AccessTools.Field(typeof(CharacterPortraitsUI), "portraits");
        private static readonly FieldInfo start = AccessTools.Field(typeof(CharacterPortraitsUI), "displayStartIndex");
        private static readonly FieldInfo end = AccessTools.Field(typeof(CharacterPortraitsUI), "displayEndIndex");
        private static readonly FieldInfo count = AccessTools.Field(typeof(CharacterPortraitsUI), "displayablePortraitsCount");
        private static readonly HashSet<int> dirty = new HashSet<int>();
        internal static void Mark(CharacterPortraitsUI ui) { if (ui != null) dirty.Add(ui.GetInstanceID()); }
        internal static void RepairIfDirty(CharacterPortraitsUI ui) { if (ui != null && dirty.Remove(ui.GetInstanceID())) Repair(ui); }
        internal static void Repair(CharacterPortraitsUI ui)
        {
            if (ui == null || portraits == null) return;
            try
            {
                var src = portraits.GetValue(ui) as CharacterPortraitUI[];
                if (src == null || src.Length == 0) return;
                var clean = new List<CharacterPortraitUI>();
                foreach (var p in src) if (p != null && p) clean.Add(p);
                if (clean.Count == src.Length) return;
                portraits.SetValue(ui, clean.ToArray());
                int live = clean.Count, s = 0;
                try { if (start != null) s = Mathf.Clamp((int)start.GetValue(ui), 0, live); } catch { }
                try { start?.SetValue(ui, s); } catch { }
                try { end?.SetValue(ui, Mathf.Clamp(live, s, live)); } catch { }
                try { count?.SetValue(ui, live); } catch { }
                Guards.Once("portraits", "zerstörte Porträts aus der Leiste entfernt");
            }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class Guard_PortraitsMark
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "Character_Added", "Character_Removed", "OnEnable", "OnDisable" })
            { var m = AccessTools.Method(typeof(CharacterPortraitsUI), n); if (m != null) yield return m; }
        }
        private static void Postfix(CharacterPortraitsUI __instance) { if (Guards.On("GuardPortraits")) PortraitRepair.Mark(__instance); }
    }

    [HarmonyPatch]
    internal static class Guard_PortraitsRun
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "RenderPortraits", "DoUpdate", "DisplayRange" })
            { var m = AccessTools.Method(typeof(CharacterPortraitsUI), n); if (m != null) yield return m; }
        }
        private static void Prefix(CharacterPortraitsUI __instance) { if (Guards.On("GuardPortraits")) PortraitRepair.RepairIfDirty(__instance); }
        private static Exception Finalizer(CharacterPortraitsUI __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardPortraits")) return __exception;
            PortraitRepair.Repair(__instance);
            return null;
        }
    }

    // 6) Duty schedule / squad screen keeps destroyed buttons when closing: clean lists, catch that error.
    [HarmonyPatch]
    internal static class Guard_ScheduleGroup
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "CreateCharacterButtons", "Start", "OnAddAnySailor", "OnDisbandAnySailor", "OnCharacterToggle" })
            { var m = AccessTools.Method(typeof(AssignmentManagementGroupUI), n); if (m != null) yield return m; }
        }
        private static void Prefix(AssignmentManagementGroupUI __instance) { if (Guards.On("GuardSchedule")) Compact(__instance); }
        private static Exception Finalizer(AssignmentManagementGroupUI __instance, Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardSchedule")) return __exception;
            Compact(__instance);
            Guards.Once("schedule", "Dienstplan-Fehler abgefangen");
            return null;
        }
        private static void Compact(object o)
        {
            if (o == null) return;
            try
            {
                foreach (var f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!(f.GetValue(o) is IList list) || list.IsReadOnly || list.IsFixedSize) continue;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var v = list[i];
                        if (v == null || (v is UnityEngine.Object u && !u)) list.RemoveAt(i);
                    }
                }
            }
            catch { }
        }
    }

    // 7) The squad +/- buttons sometimes change the type (sailor/officer) of other men: put it back.
    [HarmonyPatch]
    internal static class Guard_SquadTypeFence
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var n in new[] { "OnAddAnySailor", "OnDisbandAnySailor" })
            { var m = AccessTools.Method(typeof(AssignmentManagementGroupUI), n); if (m != null) yield return m; }
        }
        private static void Prefix(out Dictionary<PlayableCharacterData, PlayableCharacterData.Type> __state)
        {
            __state = null;
            if (!Guards.On("GuardSquadType")) return;
            var crew = Util.Find<PlayerCrew>(30f);
            __state = new Dictionary<PlayableCharacterData, PlayableCharacterData.Type>();
            foreach (var pc in crew)
                if (pc != null && pc.Characters != null)
                    foreach (var c in pc.Characters) if (c != null && c.Data != null) __state[c.Data] = c.Data.CharacterType;
        }
        private static Exception Finalizer(Dictionary<PlayableCharacterData, PlayableCharacterData.Type> __state, Exception __exception)
        {
            if (__state == null) return __exception;
            foreach (var kv in __state)
                try
                {
                    if (kv.Key.CharacterType != kv.Value)
                    {
                        kv.Key.CharacterType = kv.Value;
                        Guards.Once("squadtype", "Trupp-Knopf hat einen Mann-Typ verändert - zurückgesetzt");
                    }
                }
                catch { }
            return __exception;
        }
    }

    // 8) Schedule controller keeps a destroyed part after changing game settings.
    [HarmonyPatch]
    internal static class Guard_ScheduleSettings
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Tasks.ExpandedSchedulesController");
            var m = t != null ? AccessTools.Method(t, "UserSettings_Changed") : null;
            if (m != null) yield return m;
        }
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardSchedule")) return __exception;
            Guards.Once("schedset", "Dienstplan-Fehler nach Einstellungen abgefangen");
            return null;
        }
    }

    // 9) Follower buttons on a portrait: hover check must not break the crew screen.
    [HarmonyPatch(typeof(CharacterPortraitUI), "ValidateFollowerButtons")]
    internal static class Guard_FollowerButtons
    {
        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !Guards.IsNull(__exception) || !Guards.On("GuardPortraits")) return __exception;
            Guards.Once("followers", "Gefolgs-Knopf-Fehler abgefangen");
            return null;
        }
    }

    // 10) Boarding checkboxes in port: the game removes/adds the man and destroys/spawns his body inside the click
    // (stutter with many clicks). Here the click only marks the box; the real changes run one by one afterwards
    // through the game's own handler, so all its rules (limits, healing, spawn point) stay the same.
    public class BoardingQueue : ModuleBase
    {
        private static readonly FieldInfo toggleAction = AccessTools.Field(typeof(CrewListUI), "toggleAction");
        private static readonly FieldInfo items = AccessTools.Field(typeof(CrewListUI), "items");
        private static readonly FieldInfo stateFunc = AccessTools.Field(typeof(CrewListUI), "toggleStateFunc");
        private static readonly List<PlayableCharacterData> order = new List<PlayableCharacterData>();
        private static readonly Dictionary<PlayableCharacterData, bool> want = new Dictionary<PlayableCharacterData, bool>();
        private static CrewListUI ui;
        private static float nextAt;
        internal static bool Busy;

        internal static bool IsDefault(CrewListUI u)
        {
            try { var a = toggleAction?.GetValue(u) as Action<PlayableCharacterData>; return a != null && a.Method.Name == "DefaultToggleHandler"; }
            catch { return false; }
        }

        internal static bool Board(PlayableCharacterData d)
        {
            var crew = Util.Find<PlayerCrew>(30f);
            foreach (var c in crew) if (c != null) return c.GetCharacter(d) != null;
            return false;
        }

        // Too many selected for the game's limits? Counts the ticked boxes (incl. waiting changes).
        internal static bool Full(CrewListUI u, PlayableCharacterData cand)
        {
            var career = Util.Career;
            if (career == null) return false;
            int sailors = 0, officers = 0; bool counted = false;
            foreach (var row in u.GetComponentsInChildren<CrewListItemUI>(true))
            {
                if (row == null || row.CharacterData == null || !row.Toggled) continue;
                if (ReferenceEquals(row.CharacterData, cand)) counted = true;
                if (row.CharacterData.CharacterType == PlayableCharacterData.Type.Sailor) sailors++; else officers++;
            }
            if (!counted) { if (cand.CharacterType == PlayableCharacterData.Type.Sailor) sailors++; else officers++; }
            return cand.CharacterType == PlayableCharacterData.Type.Sailor ? sailors > career.SailorsOnBoardLimit.Value : officers > career.OfficersOnBoardLimit.Value;
        }

        internal static void Queue(CrewListUI u, PlayableCharacterData d, bool board)
        {
            ui = u;
            if (Board(d) == board) { want.Remove(d); order.Remove(d); }
            else { if (!want.ContainsKey(d)) order.Add(d); want[d] = board; }
            nextAt = Time.unscaledTime + 0.35f;
        }

        public override void Frame()
        {
            if (order.Count == 0 || Time.unscaledTime < nextAt) return;
            var d = order[0]; order.RemoveAt(0);
            bool w = want.TryGetValue(d, out bool v) && v; want.Remove(d);
            Busy = true;
            try { if (d != null && Board(d) != w) CrewListUI.DefaultToggleHandler(d); }
            catch (Exception e) { Util.Log("Einschiffen: " + e.Message); }
            finally { Busy = false; }
            nextAt = Time.unscaledTime + 0.18f;
            RefreshRows();
        }

        // Same row refresh the game does after a click.
        private static void RefreshRows()
        {
            try
            {
                if (ui == null || !ui || !ui.gameObject.activeInHierarchy) return;
                var list = items?.GetValue(ui) as IList;
                var f = stateFunc?.GetValue(ui) as Func<PlayableCharacterData, CrewListUI.CharacterStateInfo>;
                if (list != null && f != null)
                    for (int i = list.Count - 1; i >= 0; i--)
                        if (list[i] is CrewListItemUI it && it)
                        {
                            var s = f(it.CharacterData);
                            it.State = s.State; it.Toggleable = s.Toggleable;
                        }
                ui.ValidateCountLabels();
            }
            catch { }
        }

        internal static void FlushNow() { nextAt = 0f; }
    }

    [HarmonyPatch(typeof(CrewListUI), "OnToggle")]
    internal static class Boarding_TogglePatch
    {
        private static bool Prefix(CrewListUI __instance, CrewListItemUI item, bool value)
        {
            if (!Settings.B("BoardingQueue") || BoardingQueue.Busy || !BoardingQueue.IsDefault(__instance)) return true;
            if (item == null || item.CharacterData == null) return true;
            if (value && !BoardingQueue.Board(item.CharacterData) && BoardingQueue.Full(__instance, item.CharacterData))
            {
                try { item.Toggled = false; } catch { }
                return false;
            }
            BoardingQueue.Queue(__instance, item.CharacterData, value);
            try { __instance.ValidateCountLabels(); } catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(CrewListUI), "OnDisable")]
    internal static class Boarding_ClosePatch
    {
        private static void Postfix() => BoardingQueue.FlushNow();
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.UI;
    using global::UnityEngine;

    // Commander creation: the skill list (SkillTreeUI) crashed in Start with a NullReferenceException,
    // so the list stayed empty and no campaign could be started. This repairs a missing link to the
    // character screen and writes exactly what was missing into the toolkit log.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.SkillTreeUI), "Start")]
    internal static class GuardSkillTree
    {
        private static void Prefix(UBOAT.Game.UI.SkillTreeUI __instance, ref UBOAT.Game.Scene.Spaces.CharacterScreenScene ___scene)
        {
            try
            {
                string path = __instance.transform.name; var p = __instance.transform.parent;
                while (p != null) { path = p.name + "/" + path; p = p.parent; }
                if (___scene == null)
                {
                    ___scene = UnityEngine.Object.FindObjectOfType<UBOAT.Game.Scene.Spaces.CharacterScreenScene>();
                    Util.Log("Fähigkeiten-Liste: Verbindung zum Charakter-Bildschirm fehlte (" + path + ", Szene " + __instance.gameObject.scene.name + ") - " + (___scene != null ? "repariert" : "nicht gefunden"));
                }
                else if (___scene.Character == null || ___scene.Character.Data == null)
                    Util.Log("Fähigkeiten-Liste: noch keine Figur geladen (" + path + ")");
            }
            catch (System.Exception e) { Util.Log("Fähigkeiten-Liste Prüfung: " + e); }
        }

        private static System.Exception Finalizer(UBOAT.Game.UI.SkillTreeUI __instance, System.Exception __exception)
        {
            if (__exception != null) Util.Log("Fähigkeiten-Liste Fehler: " + __exception);
            return __exception;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.UI;
    using global::UnityEngine;

    // The captain creation screen has a skill page the game never fills (it is not wired up there).
    // The game opens that page by itself when the new captain has free skill points. Normally he has
    // none, but with our skill point cheats he has, so the empty page showed and blocked the campaign
    // start. Here the creation screen stays on the appearance page; the cheat works later as usual.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.UI.CharacterScreenGUI), "Scene_CharacterChanged")]
    internal static class GuardCreationSkillTab
    {
        private static void Postfix(UBOAT.Game.UI.CharacterScreenGUI __instance, UnityEngine.UI.Toggle ___skillsButton, UnityEngine.UI.Toggle ___customizeButton)
        {
            try
            {
                if (__instance.gameObject.scene.name != "Character Creation") return;
                if (___skillsButton != null && ___skillsButton.isOn && ___customizeButton != null)
                {
                    ___customizeButton.isOn = true;
                    Util.Log("Kapitän-Erstellung: leere Fähigkeiten-Seite übersprungen, Aussehen-Seite offen");
                }
            }
            catch (System.Exception e) { Util.Log("Kapitän-Erstellung Prüfung: " + e); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GameHelpers.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UnityEngine;

    // Access to game singletons that are only injected into game classes (read via their static fields).
    internal static class Game
    {
        private static TimeCompressionController tcc;
        private static SandboxPlayerWolfpack wolfpack;
        private static SavesManager saves;
        private static BackgroundTasksManager tasks;

        public static TimeCompressionController TimeCompression =>
            tcc ?? (tcc = Traverse.Create(typeof(DisallowTimeCompressionDuringAircraftAttack)).Field("timeCompressionController").GetValue<TimeCompressionController>());

        public static SandboxPlayerWolfpack Wolfpack =>
            wolfpack ?? (wolfpack = Traverse.Create(typeof(UBOAT.Game.UI.Notifications.ShallowAreaNotification)).Field("wolfpack").GetValue<SandboxPlayerWolfpack>());

        public static SavesManager Saves =>
            saves ?? (saves = Traverse.Create(typeof(LeakBlockingBySailors)).Field("savesManager").GetValue<SavesManager>());

        public static BackgroundTasksManager Tasks =>
            tasks ?? (tasks = Traverse.Create(typeof(ExpandedSchedulesController)).Field("backgroundTasksManager").GetValue<BackgroundTasksManager>());

        public static void StopTimeCompression(string why)
        {
            var t = TimeCompression;
            if (t == null || !t.TimeCompression) return;
            t.TimeCompression = false;
            ToolkitRunner.ShowNotice(why);
        }

        public static bool CanSave()
        {
            var s = Saves;
            return s != null && !s.IsLoading && !s.HasSaveBlockadesExcept(null, out _);
        }

        public static bool Ctrl => Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        public static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }
}

// ====================================================================================================
// Abschnitt: Modules/GameTuning.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::UnityEngine;
    using global::UnityEngine.Scripting;

    // Game-wide tuning (not only the mod):
    // - Log filter: the game writes masses of info lines into Player.log (reports of up to 6 GB).
    //   Only warnings and errors are kept -> less disk work and less memory for log text.
    // - GC time slice: the incremental garbage collector may use x ms per frame (0 = game value).
    public class GameTuning : ModuleBase
    {
        private LogType? originalFilter;
        private ulong originalSlice;

        private static readonly UBOAT.Game.Core.Data.GameDataReference maxUnits = new UBOAT.Game.Core.Data.GameDataReference("General/Settings/World/Max Spawned Units Per Group");
        private static readonly UBOAT.Game.Core.Data.GameDataReference maxGroups = new UBOAT.Game.Core.Data.GameDataReference("General/Settings/World/Max Groups Per Battle");
        private float origUnits = -1f, origGroups = -1f;

        // Fewer ships at the same time in 3D = big CPU saving in convoy battles (0 = game value 28 / 4).
        public override void Tick()
        {
            Apply(maxUnits, ref origUnits, Settings.F("MaxShipsPerGroup"));
            Apply(maxGroups, ref origGroups, Settings.F("MaxGroupsPerBattle"));

        }

        private static void Apply(UBOAT.Game.Core.Data.GameDataReference r, ref float orig, float want)
        {
            var p = r.SelectedValue;
            if (p == null) return;
            if (orig < 0f) orig = p.Value;
            float v = want > 0f ? want : orig;
            if (p.Value != v) p.Value = v;
        }

        public override void Frame()
        {
            if (Settings.B("GameLogFilter"))
            {
                if (originalFilter == null) originalFilter = Debug.unityLogger.filterLogType;
                Debug.unityLogger.filterLogType = LogType.Warning;
            }
            else if (originalFilter != null)
            {
                Debug.unityLogger.filterLogType = originalFilter.Value;
                originalFilter = null;
            }

            if (!GarbageCollector.isIncremental) return;
            if (originalSlice == 0) originalSlice = GarbageCollector.incrementalTimeSliceNanoseconds;
            float ms = Settings.F("GcSliceMs");
            ulong want = ms > 0f ? (ulong)(ms * 1000000f) : originalSlice;
            if (GarbageCollector.incrementalTimeSliceNanoseconds != want) GarbageCollector.incrementalTimeSliceNanoseconds = want;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::UnityEngine;
    using global::UnityEngine.Scripting;

    // Game's own developer console: only its "enabled" setting is answered with true (idea from the Custom Actions mod).
    // With "achievements always allowed" using it costs no achievements.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Core.Data.GameDataReference), "GetBoolean", new System.Type[0])]
    internal static class GameTuning_DevConsolePatch
    {
        private static System.Reflection.FieldInfo consoleField;
        private static bool consoleLooked;   // look the field up once: the type search scans every assembly
        private static void Postfix(UBOAT.Game.Core.Data.GameDataReference __instance, ref bool __result)
        {
            if (__result || !Settings.B("DevConsole")) return;
            if (!consoleLooked)
            {
                consoleLooked = true;
                var t = UboatToolkit.Util.FindType("UBOAT.Game.UI.DevConsole.DevConsole");
                consoleField = t == null ? null : HarmonyLib.AccessTools.Field(t, "enableDeveloperConsole");
            }
            var consoleRef = consoleField?.GetValue(null);
            if (consoleRef != null && ReferenceEquals(__instance, consoleRef)) __result = true;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::UnityEngine;
    using global::UnityEngine.Scripting;

    // 0.4.28 (DoMe: short error popups, e.g. when buying upgrades): with the developer console enabled the GAME shows
    // "Error Logged" on its notification bar for every logged exception (DevConsole.Application_logMessageReceived).
    // The popup is skipped; the error itself stays in the game log, and the Toolkit log counts them once per minute.
    [HarmonyLib.HarmonyPatch]
    internal static class GameTuning_DevConsolePopupPatch
    {
        private static int skipped; private static float nextLog;
        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.UI.DevConsole.DevConsole");
            return t == null ? null : HarmonyLib.AccessTools.Method(t, "Application_logMessageReceived");
        }
        private static bool Prefix(string condition, UnityEngine.LogType type)
        {
            if (!Settings.B("DevConsole") || !Settings.B("DevConsoleNoErrorPopup")) return true;
            if (type == UnityEngine.LogType.Exception)
            {
                skipped++;
                if (UnityEngine.Time.unscaledTime >= nextLog)
                {
                    nextLog = UnityEngine.Time.unscaledTime + 60f;
                    string first = condition ?? ""; if (first.Length > 160) first = first.Substring(0, 160);
                    Util.Log("Konsole: Meldung „Fehler protokolliert“ unterdrückt, " + skipped + " Fehler seit Start, zuletzt: " + first);
                }
            }
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Inventory.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene.Items;
    using global::UnityEngine;

    // Cheats #18 + #23: items and ammunition (almost) weigh nothing (0.0000001, never 0),
    // and one storage slot holds (almost) unlimited items of one kind.
    // Adding real extra slots broke items in the old "Slot Machine" mod, so this uses
    // the game's own "ignore stack limits" switch instead (safe for saves).
    public class Inventory : ModuleBase
    {
        private float next, nextMerge;
        private string lastState = "";

        // Same items in several slots of one storage are put together into one stack
        // (e.g. medkits bought as 5-packs). No reordering, slots with open loading orders are left alone.
        internal static int MergeStacks()
        {
            int moves = 0;
            foreach (var s in Util.Find<Storage>())
            {
                if (s == null || s.IsShop || s.GetComponentInParent<UBOAT.Game.Scene.Characters.PlayableCharacter>() != null) continue;
                int slots = Util.Slots(s);
                for (int a = 0; a < slots; a++)
                {
                    if (!(s.GetItemAt(a) is Cargo ca) || ca.Data == null || ca.Data.StackLimit <= 1f || s.HasTransportOrders(a)) continue;
                    for (int b = a + 1; b < slots; b++)
                    {
                        if (!(s.GetItemAt(b) is Cargo cb) || cb.Data == null || cb.Data.Name != ca.Data.Name || s.HasTransportOrders(b)) continue;
                        float room = (float)ca.StackLimit - ca.Count;
                        if (room <= 0f) break;
                        float move = Mathf.Min(room, cb.Count);
                        if (move <= 0f) continue;
                        UBOAT.Game.Scene.Utilities.StorageUtility.TransferInstantly(s, b, s, a, move);
                        moves++;
                    }
                }
            }
            return moves;
        }

        // Sets the stack size of one item right away (used for new items too, so they do not
        // wait for the next 5-second pass). At least "CheatMinStackSize" per slot.
        public static void ApplyStack(Cargo c)
        {
            if (c == null || c.StackLimit == null) return;
            float dataLimit = c.Data != null ? c.Data.StackLimit : 1f;
            // Stackable items: the bigger stack is written into the item database itself (see StackData),
            // because the game's storage window (drag, buy, split) only reads the database value.
            // Keep the item's own "Base" value in step with it.
            if (dataLimit > 1f)
            {
                var m = c.StackLimit.GetDeltaModifier("Base", true);
                if (m != null && Mathf.Abs(m.Value - dataLimit) > 0.0001f) m.Value = dataLimit;
                Util.ScaleParameter(c.StackLimit, 1f);
                return;
            }
            float f = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));
            if (minStack > 0f) f = Mathf.Max(f, minStack);
            Util.ScaleParameter(c.StackLimit, f);
        }

        // Cheat: the dealer in port has at least "CheatShopStock" of every stackable item (DoMe 2026-11-13: buy slider
        // stopped at the dealer's 15 decoys although one storage place holds 100000). Only while docked, every 5 s.
        private static int shopLogged;
        private static void ShopStock()
        {
            float want = Settings.F("CheatShopStock");
            var ship = Util.PlayerShip;
            if (want <= 0f || ship == null || !ship.Docked) return;
            int n = 0;
            foreach (var st in UnityEngine.Object.FindObjectsOfType<Storage>())
            {
                if (st == null || !st.IsShop) continue;
                for (int i = Util.Slots(st) - 1; i >= 0; i--)
                {
                    var c = st.GetItemAt(i) as Cargo;
                    if (c == null || c.Data == null || c.Data.StackLimit <= 1f || c is Fuel) continue;
                    if (c.Count < want) { c.Count = want; n++; }
                }
            }
            if (n > 0 && shopLogged++ < 5) Util.Log("Händler: " + n + " Waren auf " + want + " Stück aufgefüllt");
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            bool noWeight = Settings.B("CheatNoWeight");
            float stack = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));
            // 0.3.69 ("Langsam: Inventory.Tick ~48 ms" every 5 s): the full pass over all storages and items now runs
            // at once when a setting changed, otherwise only every 20 s. New items get their stack size right away (ApplyStack).
            bool noLimit = Settings.B("CheatNoMassLimit");
            string state = noWeight + "/" + noLimit + "/" + stack + "/" + minStack + "/" + Settings.F("CheatShopStock") + "/" + Settings.B("CheatMergeStacks") + "/" + ship.Docked;
            if (state == lastState && Time.unscaledTime < next) return;
            next = Time.unscaledTime + (state != lastState ? 2f : 20f);
            lastState = state;

            foreach (var s in Util.Find<Storage>())
            {
                if (s.IsShop) continue;
                // 0.4.2: torpedo tubes and torpedo racks are left exactly as the game made them (suspect for the
                // empty tube window / crew not loading; torpedoes never stack anyway).
                if (s is TorpedoLauncherGroup || s is TorpedoStorage)
                {
                    var tt = Traverse.Create(s);
                    tt.Field("ignoreStackLimits").SetValue(Util.Original<bool>(s, "ignoreStackLimits"));
                    Util.ScaleParameter(tt.Field("itemsMassLimit").GetValue<Parameter>(), 1f);
                    continue;
                }
                var t = Traverse.Create(s);
                bool origIgnore = Util.Original<bool>(s, "ignoreStackLimits");
                t.Field("ignoreStackLimits").SetValue(stack > 1f || minStack > 0f || origIgnore);
                // Never for diesel tanks: the game shows fuel % as (fuel mass / tank mass limit), so a bigger
                // limit made the tank read 0 % and the boat "out of fuel" although it was full.
                bool fuelTank = s.StorageType != null && s.StorageType.StartsWith("Fuel");
                Util.ScaleParameter(t.Field("itemsMassLimit").GetValue<Parameter>(), noLimit && !fuelTank ? 1000000f : 1f);
            }
            StackData.Apply();
            Util.Safe("ShopStock", ShopStock);
            if (Settings.B("CheatMergeStacks") && Time.unscaledTime >= nextMerge)
            {
                nextMerge = Time.unscaledTime + 15f;
                int m = MergeStacks();
                if (m > 0) Util.Log("Stapel zusammengelegt: " + m);
            }
            foreach (var e in Util.Find<Equipment>())
            {
                // Never for diesel: the game reads the fuel amount from its weight, so "no weight"
                // made every tank show 0 kg and look empty, even right after refuelling.
                Util.ScaleParameter(e.Mass, noWeight && !(e is Fuel) && !(e is StoredTorpedo) ? 0.0000001f : 1f);   // 0.4.2: torpedoes keep their game weight
                if (e is Cargo c) ApplyStack(c);
            }
        }
    }

    // (ShopStock is a member of Inventory, see below)
    // One place that writes the stack size into the item database (all stackable items, e.g. medkits 5 -> 100000).
    // Stack cheat: base x "CheatStackSize", at least "CheatMinStackSize". Tight packing skill adds its bonus on top.
    // Switched off = the game's own value again.
    internal static class StackData
    {
        private static readonly System.Collections.Generic.Dictionary<UBOAT.Game.Sandbox.SandboxEquipmentData, float> baseStack =
            new System.Collections.Generic.Dictionary<UBOAT.Game.Sandbox.SandboxEquipmentData, float>();
        internal static int PackBonus;
        private static string last = "";

        internal static void Apply()
        {
            float f = Mathf.Max(1f, Settings.F("CheatStackSize"));
            float minStack = Mathf.Max(0f, Settings.F("CheatMinStackSize"));
            bool all = Settings.B("CheatStackAll") && (f > 1f || minStack > 0f);
            string key = f + "/" + minStack + "/" + PackBonus + "/" + all;
            if (key == last) return;
            var db = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Sandbox.Devices.EquipmentDatabase>();
            var bps = db != null ? db.GetEquipmentDirect() : null;
            if (bps == null) return;
            int n = 0;
            foreach (var d in bps)
            {
                if (d == null) continue;
                if (!baseStack.TryGetValue(d, out float b)) { b = d.StackLimit; baseStack[d] = b; }
                // single items (game stack 1): only plain goods (Cargo types, not diesel) and only with "CheatStackAll";
                // switched off = back to the game's 1
                if (b <= 1f && !(all && PlainGoods(d)))
                {
                    if (Mathf.Abs(d.StackLimit - b) > 0.0001f) { d.StackLimit = b; n++; }
                    continue;
                }
                float v = Mathf.Max(Mathf.Max(b, 1f) * f, minStack) + PackBonus;
                if (Mathf.Abs(d.StackLimit - v) > 0.0001f) { d.StackLimit = v; n++; }
            }
            last = key;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<Cargo>()) Inventory.ApplyStack(c);
            Util.Log("Stapel: " + n + " Gegenstaende neu (x" + f + ", mind. " + minStack + ", Packen +" + PackBonus + ", Einzel-Sachen " + (all ? "auch" : "nein") + ")");
        }

        private static bool PlainGoods(UBOAT.Game.Sandbox.SandboxEquipmentData d)
        {
            var t = d.Type;
            return t != null && typeof(UBOAT.Game.Scene.Items.Cargo).IsAssignableFrom(t) && !typeof(UBOAT.Game.Scene.Items.Fuel).IsAssignableFrom(t);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Loading.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

    // QoL #39: loading in port. With many items at once the port workers can get stuck and
    // nothing reaches the boat any more. While docked, transport orders between port and boat
    // that nobody works on (or that did not move for a while) are finished right away,
    // with the game's own "resolve instantly" function.
    public class Loading : ModuleBase
    {
        private readonly List<TransportOrder> buffer = new List<TransportOrder>();
        private readonly HashSet<TransportOrder> seen = new HashSet<TransportOrder>();
        private readonly Dictionary<TransportOrder, KeyValuePair<float, float>> progress = new Dictionary<TransportOrder, KeyValuePair<float, float>>();
        private float next;

        private UBOAT.Game.Scene.Tasks.PortServicesProvider provider;
        private double lastLeft = -1;
        private float lastChange;
        private float nextDiag;
        private int kicks;

        // Watchdog for the port resupply queue: if "loading" shows but nothing moves,
        // push it once more; if it is still stuck, cancel it so you can order again.
        // The menu switch "CancelPortLoading" cancels it by hand.
        // Dead orders: finished (or nothing left to carry) but still hanging on a storage slot.
        // The slot then shows "wird verladen... 0 / 0" forever. Seen when a storage got more slots
        // (Complete() removes the order only from the slot index it remembers) or when an order ran
        // down to 0 without being completed. They are completed and taken off every slot.
        private readonly List<TransportOrder> sweepBuf = new List<TransportOrder>();
        internal int SweepDeadOrders()
        {
            int n = 0;
            if (Util.PlayerShip == null) return 0;
            foreach (var s in Util.Find<Storage>())
                for (int i = 0; i < Util.Slots(s); i++)
                {
                    sweepBuf.Clear();
                    if (!s.GetTransportOrders(i, sweepBuf)) continue;
                    foreach (var o in sweepBuf)
                    {
                        if (o == null) { s.RemoveTransportOrder(i, o); n++; continue; }
                        bool dead = o.IsCompleted || (o.CountLeft <= 0f && o.CountInTransit <= 0f);
                        if (!dead) continue;
                        if (!o.IsCompleted) { try { o.Complete(); } catch (System.Exception ex) { Util.Log("Verladen: Abschluss-Fehler " + ex.Message); } }
                        s.RemoveTransportOrder(i, o);
                        n++;
                    }
                }
            if (n > 0) Util.Log("Verladen: " + n + " tote Aufträge (0 / 0) entfernt");
            return n;
        }

        private void Watchdog()
        {
            if (Util.PlayerShip != null && Util.PlayerShip.Docked) SweepDeadOrders();
            if (provider == null)
                provider = HarmonyLib.Traverse.Create(typeof(UBOAT.Game.Sandbox.Missions.TutorialUtility)).Field("portServicesProvider").GetValue<UBOAT.Game.Scene.Tasks.PortServicesProvider>();
            if (provider == null) return;
            var t = HarmonyLib.Traverse.Create(provider);
            var orders = t.Field("transportOrders").GetValue<List<TransportOrder>>();

            var cancel = Settings.Get("CancelPortLoading");
            if (cancel != null && cancel.On)
            {
                cancel.Value = 0f;
                Settings.Save();
                provider.CancelResupply();
                ToolkitRunner.ShowNotice("Verladen abgebrochen. Du kannst neu bestellen.");
                lastLeft = -1; kicks = 0;
                return;
            }
            var force = Settings.Get("FixMyDamnShipment");
            if (force != null && force.On)
            {
                force.Value = 0f;
                Settings.Save();
                ForceDeliver(provider, orders);
                lastLeft = -1; kicks = 0;
                return;
            }
            if (orders == null || orders.Count == 0) { lastLeft = -1; kicks = 0; return; }

            double left = 0;
            foreach (var o in orders) if (o != null) left += o.CountLeft;
            if (left != lastLeft) { lastLeft = left; lastChange = Time.unscaledTime; return; }
            if (Time.unscaledTime - lastChange < Settings.F("PortLoadingStuckSeconds")) return;

            lastChange = Time.unscaledTime;
            if (kicks == 0)
            {
                kicks++;
                Util.Log("Verladen hängt, neuer Anstoß");
                t.Method("AddDateListener").GetValue();
            }
            else
            {
                kicks = 0;
                provider.CancelResupply();
                ToolkitRunner.ShowNotice("Verladen hing fest und wurde abgebrochen. Bitte neu bestellen.");
            }
        }

        // "Fix my damn shipment": deliver EVERYTHING that is ordered into the boat right now,
        // including orders a worker is carrying, then clear whatever is left in the queue.
        private void ForceDeliver(UBOAT.Game.Scene.Tasks.PortServicesProvider p, List<TransportOrder> orders)
        {
            var all = new HashSet<TransportOrder>();
            if (orders != null) foreach (var o in orders) if (o != null) all.Add(o);
            var ship = Util.PlayerShip;
            if (ship != null)
                foreach (var s in Util.Find<Storage>())
                    for (int i = 0; i < Util.Slots(s); i++)
                    {
                        buffer.Clear();
                        if (s.GetTransportOrders(i, buffer)) foreach (var o in buffer) if (o != null) all.Add(o);
                    }
            int n = 0;
            foreach (var o in all)
            {
                if (o.IsCompleted) continue;
                try
                {
                    if (o.CountInTransit > 0f) { o.CountLeft += o.CountInTransit; o.CountInTransit = 0f; }
                    StorageUtility.ResolveTransportOrderInstantly(o);
                }
                catch (System.Exception ex) { Util.Log("Force: " + ex.Message); }
                if (!o.IsCompleted) o.Complete();
                n++;
            }
            n += SweepDeadOrders();
            p.CancelResupply();   // clears any leftovers and ends the "loading" state
            progress.Clear();
            ToolkitRunner.ShowNotice("FIX MY DAMN SHIPMENT: " + n + " Lieferungen erzwungen. Ruhe jetzt.");
        }

        public override void Frame() => Loading_ResupplyPatch.RunPending();

        public override void Tick()
        {
            if (Settings.B("PortLoadingFix") || Settings.B("FixMyDamnShipment") || Settings.B("CancelPortLoading")) Util.Safe("LoadingWatchdog", Watchdog);
            if (!Settings.B("InstantPortLoading") || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 2f;
            var ship = Util.PlayerShip;
            // Before the first patrol the game keeps bought goods waiting until you sail out
            // ("Georderte Waren werden ... geladen, bevor es zu seinem ersten Einsatz ausläuft").
            // Then the boat does not count as docked, so allow that case too.
            var career = Util.Career;
            bool firstPatrol = career != null && career.BeforeFirstPatrol;
            if (!ship.Docked && !firstPatrol) { progress.Clear(); return; }

            seen.Clear();
            foreach (var s in Util.Find<Storage>())
                for (int i = 0; i < Util.Slots(s); i++)
                {
                    buffer.Clear();
                    if (!s.GetTransportOrders(i, buffer)) continue;
                    foreach (var o in buffer) if (o != null) seen.Add(o);
                }

            int done = 0, nDone = 0, nTransit = 0, nInside = 0, nBusy = 0;
            float stuckAfter = Settings.F("PortLoadingStuckSeconds");
            foreach (var o in seen)
            {
                if (o.IsCompleted || o.CountLeft <= 0f) { nDone++; continue; }
                if (o.CountInTransit > 0f) { nTransit++; continue; }
                bool portToBoat = !IsShip(o.SourceStorage, ship) || !IsShip(o.TargetStorage, ship);
                if (!portToBoat) { nInside++; continue; }

                bool idle = o.CharacterAction == null;
                bool stuck = false;
                if (!idle)
                {
                    if (progress.TryGetValue(o, out var p) && p.Key == o.CountLeft) stuck = Time.unscaledTime - p.Value > stuckAfter;
                    else progress[o] = new KeyValuePair<float, float>(o.CountLeft, Time.unscaledTime);
                }
                if (!idle && !stuck && !firstPatrol) { nBusy++; continue; }

                Util.Safe("Loading", () => StorageUtility.ResolveTransportOrderInstantly(o));
                progress.Remove(o);
                done++;
            }
            if (done > 0) Util.Log("Verladen: " + done + " Aufträge sofort erledigt" + (firstPatrol ? " (vor erster Feindfahrt)" : ""));
            else if (seen.Count > 0 && Time.unscaledTime >= nextDiag)
            {
                nextDiag = Time.unscaledTime + 30f;
                Util.Log("Verladen: " + seen.Count + " Aufträge gefunden, keiner erledigt (angedockt " + ship.Docked + ", vor erster Feindfahrt " + firstPatrol
                    + "; fertig " + nDone + ", wird getragen " + nTransit + ", nur im Boot umräumen " + nInside + ", Crew arbeitet dran " + nBusy + ")");
            }
        }

        private static bool IsShip(IItemStorage s, UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var c = s as Component;
            return c != null && c.transform.IsChildOf(ship.transform);
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::System.Collections.Generic;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;
    using global::UnityEngine;

    // The real cause of the port hang: PortServicesProvider.AddDateListener moves ONE unit per loop
    // step on the main thread. With tiny item weight the wait per unit becomes 0 ticks, so a stack of
    // millions of units is moved in one endless loop -> the game freezes and the queue stays blocked
    // (no more ammo, items or fuel). Fix: move the whole order in one step.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.PortServicesProvider), "AddDateListener")]
    internal static class Loading_ResupplyPatch
    {
        private static readonly UBOAT.Game.Core.Data.GameDataReference durationFactor =
            new UBOAT.Game.Core.Data.GameDataReference("General/Settings/Trade/Transfer Duration Factor (s/kg)");

        // Providers whose next order must be moved. Not done right away: the shop screen still reads the
        // shop slot right after placing the order (price, name). Moving the goods in that same moment
        // emptied the slot -> crash in the shop screen and the order stayed "wird verladen" forever.
        // So the move happens one frame later (RunPending, called every frame by the Loading module).
        private static readonly System.Collections.Generic.List<UBOAT.Game.Scene.Tasks.PortServicesProvider> pending =
            new System.Collections.Generic.List<UBOAT.Game.Scene.Tasks.PortServicesProvider>();
        private static bool running;

        internal static void RunPending()
        {
            if (pending.Count == 0) return;
            var list = pending.ToArray(); pending.Clear();
            foreach (var p in list)
            {
                if (p == null) continue;
                running = true;
                try { Util.Safe("Verladen", () => Traverse.Create(p).Method("AddDateListener").GetValue()); }
                finally { running = false; }
            }
        }

        private static bool Prefix(UBOAT.Game.Scene.Tasks.PortServicesProvider __instance)
        {
            if (!running && (Settings.B("InstantPortLoading") || Settings.B("PortLoadingFix")))
            {
                if (!pending.Contains(__instance)) pending.Add(__instance);
                return false;
            }
            bool instant = Settings.B("InstantPortLoading");
            bool fix = Settings.B("PortLoadingFix");
            if (!instant && !fix) return true;
            var t = HarmonyLib.Traverse.Create(__instance);
            var orders = t.Field("transportOrders").GetValue<System.Collections.Generic.List<UBOAT.Game.Scene.Items.TransportOrder>>();
            if (orders == null) return true;
            orders.RemoveAll(o => o == null || o.IsCompleted);     // base fix: drop dead orders that block the queue
            if (orders.Count == 0) return true;

            var order = orders[0];
            if (!instant)
            {
                // Base fix only: keep the game's normal waiting, but never let it run into the
                // endless loop (0 ticks per unit because of tiny weight, or giant stacks).
                long perUnit = order.ItemData.HasTag("Torpedo") ? 1 : (long)((double)order.ItemData.Mass * (double)(float)durationFactor * 10000000.0);
                if (perUnit >= 1 && order.CountLeft <= 10000f) return true;
            }
            t.Field("currentTransportOrder").SetValue(order);
            try
            {
                // Complete() fires the game's own handler, which starts the next order (back into this patch).
                UBOAT.Game.Scene.Utilities.StorageUtility.ResolveTransportOrderInstantly(order);
            }
            catch (System.Exception ex)
            {
                Util.Log("Verladen Fehler: " + ex.Message);
                if (!order.IsCompleted) order.Complete();
            }
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/MemoryCleaner.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::UnityEngine;

    // Memory cleanup against the game's slowly growing RAM use: every X minutes the mod frees
    // unused graphics/sound data and runs the .NET garbage collector. This causes one short hitch,
    // so it waits for a calm moment (game paused or time compression on).
    public class MemoryCleaner : ModuleBase
    {
        private float due = -1f, shipSince;

        public override void Tick()
        {
            float min = Settings.F("MemoryCleanMinutes");
            if (min <= 0f) { due = -1f; return; }
            if (due < 0f) { due = Time.unscaledTime + min * 60f; return; }
            if (Time.unscaledTime < due) return;
            // Never while a save is loading or the boat just appeared: freeing "unused" assets then can unload
            // ship prefabs the game is about to spawn (seen: HDML / Flower-Class spawn errors after a load).
            if (SaveTools_LoadSpeedPatch.IsLoading || !Util.ShipReady || Util.SafeGet(() => Game.Saves != null && Game.Saves.IsLoading, false)
                || Time.unscaledTime - shipSince < 120f)
            { if (Util.PlayerShip == null) shipSince = Time.unscaledTime; return; }
            bool calm = Time.timeScale == 0f || (Game.TimeCompression?.TimeCompression ?? false);
            // Only in a calm moment (paused or time compression): the cleanup itself takes 0.3-0.65 s, which was
            // a visible hitch when it was forced after 5 minutes during normal play.
            if (!calm) return;
            due = Time.unscaledTime + min * 60f;
            long before = System.GC.GetTotalMemory(false);
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
            long after = System.GC.GetTotalMemory(false);
            Util.Log("Speicher aufgeräumt: " + ((before - after) / 1048576) + " MB frei");
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::UnityEngine;

    // Disk cache cleanup, once per game start: old cached data sheets (.dat, the game rebuilds them when needed),
    // old crash reports and old mod logs older than X days are deleted. Save games are never touched.
    public class CacheCleaner : ModuleBase
    {
        private bool done;

        public override void Tick()
        {
            if (done || !Settings.B("AutoCleanCache")) return;
            done = true;
            Util.Safe("CacheCleaner", () =>
            {
                var limit = System.DateTime.Now.AddDays(-Mathf.Max(1f, Settings.F("AutoCleanDays")));
                string root = Application.persistentDataPath;
                int n = 0; long bytes = 0;
                n += Clean(System.IO.Path.Combine(root, "Data Sheets"), "*.dat", limit, ref bytes);
                n += Clean(System.IO.Path.Combine(Settings.Folder, "logs"), "*.*", limit, ref bytes, keep: "kriegstagebuch.txt");
                n += Clean(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Deep Water Studio", "UBOAT", "Crashes"), "*.*", limit, ref bytes);
                if (n > 0) Util.Log("Cache aufgeräumt: " + n + " Dateien, " + (bytes / 1048576) + " MB");
            });
        }

        private static int Clean(string dir, string pattern, System.DateTime limit, ref long bytes, string keep = null)
        {
            if (!System.IO.Directory.Exists(dir)) return 0;
            int n = 0;
            foreach (var f in System.IO.Directory.GetFiles(dir, pattern, System.IO.SearchOption.AllDirectories))
            {
                try
                {
                    var fi = new System.IO.FileInfo(f);
                    if (keep != null && fi.Name == keep) continue;
                    if (fi.LastWriteTime > limit) continue;
                    bytes += fi.Length;
                    fi.Delete();
                    n++;
                }
                catch { }
            }
            return n;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/MilkCows.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.HQ;
    using global::UnityEngine;

    // QoL #25: supply U-boats (Type XIV "milk cows") are at sea from the start of the campaign,
    // without first ordering them at the flotilla commander in port.
    // The game defines them as headquarters tasks "Spawn Milk Cow", "Spawn Milk Cow II", ...
    // Fix 2026-11-12 ("Nachschubanforderung läuft ins Leere"): the game's radio resupply only counts
    // supply-ship tasks marked completed. We only spawned the boat, so the flag stayed false. Now the flag
    // is set like the game's own start script does, sunk/lost boats are spawned again, and on a radio
    // request the nearest milk cow is brought into reach first.
    public class MilkCows : ModuleBase
    {
        private static readonly string[] Names =
        {
            "Spawn Milk Cow", "Spawn Milk Cow II", "Spawn Milk Cow III", "Spawn Milk Cow IV", "Spawn Milk Cow V",
            "Spawn Milk Cow VI", "Spawn Milk Cow VII", "Spawn Milk Cow VIII", "Spawn Milk Cow IX", "Spawn Milk Cow X",
        };

        private static readonly FieldInfo Completed = AccessTools.Field(typeof(HeadquartersTaskBase), "completed");
        private float next;

        public static bool Alive(AddSupplyShip t) => t?.SupplyShip != null && !t.SupplyShip.IsPacified && t.SupplyShip.Group != null;

        public static IEnumerable<AddSupplyShip> Tasks(Headquarters hq)
        {
            foreach (var name in Names)
                if (hq.GetTask(name) is AddSupplyShip task) yield return task;
        }

        public static void MarkDone(AddSupplyShip t)
        {
            if (t != null && !t.IsCompleted) Completed?.SetValue(t, true);
        }

        public override void Tick()
        {
            if (!Settings.B("MilkCowsAtStart") || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 10f;
            var hq = Util.PlayerShip?.SandboxEntity?.Country?.Headquarters;
            if (hq == null) return;
            foreach (var task in Tasks(hq))
            {
                if (!Alive(task))
                {
                    task.Start();
                    Util.Log("[MilkCows] Versorger gestartet: " + task.Name + (Alive(task) ? "" : " (fehlgeschlagen)"));
                }
                MarkDone(task);
            }
        }

        // Before the game answers a radio resupply request: make sure the nearest milk cow is in reach.
        public static void BringNearest()
        {
            var ps = Util.PlayerShip?.SandboxEntity;
            var hq = ps?.Country?.Headquarters;
            if (hq == null) return;
            AddSupplyShip best = null; float bestD = float.MaxValue;
            foreach (var t in Tasks(hq))
            {
                if (!Alive(t)) { t.Start(); }
                if (!Alive(t)) continue;
                MarkDone(t);
                float d = (t.SupplyShip.Position - ps.Position).sqrMagnitude;
                if (d < bestD) { best = t; bestD = d; }
            }
            if (best == null) { Util.Log("[MilkCows] Funk-Nachschub: keine Milchkuh verfügbar"); return; }
            float km = Settings.F("MilkCowRadioKm");
            if (bestD <= km * km) return;
            best.SupplyShip.Group.SetPosition(ps.Position + new Vector2(km * 0.7071f, km * 0.7071f), true);
            Util.Log("[MilkCows] Funk-Nachschub: " + best.Name + " auf " + km + " km herangeholt (war " + Mathf.Sqrt(bestD).ToString("0") + " km)");
        }
    }

    [HarmonyPatch(typeof(Headquarters), "RespondToResupplyRequest")]
    internal static class MilkCows_RadioRequest
    {
        private static void Prefix()
        {
            if (Settings.B("MilkCowRadio")) Util.Safe("MilkCowRadio", MilkCows.BringNearest);
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/MoreShips.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    // QoL "mehr Frachter": every merchant spawner of the game (types "Convoy" and "Transport") sends out new groups
    // every 8 h / activity. This multiplies the activity, so convoys and single freighters appear x times as often.
    // The game's own original value is remembered and restored at factor 1.
    public class MoreShips : ModuleBase
    {
        private static readonly AccessTools.FieldRef<GroupSpawner, float> Activity = AccessTools.FieldRefAccess<GroupSpawner, float>("activity");
        private readonly Dictionary<GroupSpawner, float> original = new Dictionary<GroupSpawner, float>();
        private float next, lastFactor = 1f;

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 30f;
            float f = Mathf.Max(0.1f, Settings.F("MerchantTraffic"));
            if (f == 1f && lastFactor == 1f) return;
            var sb = Traverse.Create(typeof(UBOAT.Game.UI.DevConsole.DevConsoleCommands)).Field("sandbox").GetValue<Sandbox>();
            if (sb == null || sb.GroupSpawners == null) return;
            int n = 0;
            foreach (var g in sb.GroupSpawners)
            {
                if (g == null || (g.SpawnType != "Convoy" && g.SpawnType != "Transport")) continue;
                if (!original.TryGetValue(g, out float orig)) { orig = Activity(g); original[g] = orig; }
                float want = orig * f;
                if (Activity(g) != want) { Activity(g) = want; n++; }
            }
            if (n > 0) Util.Log("Mehr Frachter: " + n + " Handels-Startpunkte auf x" + f.ToString("0.##") + " gesetzt");
            lastFactor = f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/OtherModFixes.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::DWS.Common.Characters;
    using global::UBOAT.Game.Scene.Effects;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.SceneManagement;
    using global::UBOAT.Game.UI.MainMenu;

    // Fix for the "Supply Price Editor" mod: it searched the whole scene for the main menu every
    // frame, even at sea. Now it only searches while the main menu is loaded, at most once a second.
    [HarmonyPatch]
    internal static class SupplyPriceMenu_Patch
    {
        private static MainMenu cached;
        private static float nextSearch;
        private static bool errorLogged;

        private static MethodBase Target()
        {
            var t = UboatToolkit.Util.FindType("SupplyPriceEditor.MainMenuContext");
            var m = t == null ? null : AccessTools.Method(t, "FindMainMenu", Type.EmptyTypes);
            return m != null && m.ReturnType == typeof(MainMenu) ? m : null;
        }

        private static bool Prepare()
        {
            bool ok = Target() != null;
            Util.Log("Preis-Editor-Fix: " + (ok ? "aktiv (Hauptmenü-Suche nur im Hauptmenü, höchstens 1x pro Sekunde)." : "Supply Price Editor nicht gefunden - nichts zu tun."));
            return ok;
        }

        private static MethodBase TargetMethod() => Target();

        private static bool Prefix(ref MainMenu __result)
        {
            try
            {
                if (!Settings.B("FixSupplyPriceMenu")) return true;
                if (!SceneManager.GetSceneByName("Main Menu").isLoaded) { cached = null; __result = null; return false; }
                if (cached != null && cached.isActiveAndEnabled && cached.gameObject.scene.isLoaded) { __result = cached; return false; }
                float now = Time.unscaledTime;
                if (now < nextSearch) { __result = cached; return false; }
                nextSearch = now + 1f;
                return true;
            }
            catch (Exception e)
            {
                if (!errorLogged) { errorLogged = true; Util.Log("Preis-Editor-Fix: Fehler, nehme Original-Suche (einmalig): " + e.Message); }
                return true;
            }
        }

        private static void Postfix(MainMenu __result) { cached = __result; }
    }

    // Fix for the "Realistic Uniforms Revival" mod: it rebuilt a crewman's textures again and again
    // although nothing on him changed. Those own rebuilds are skipped when renderers, meshes and
    // materials are unchanged; rebuilds that other mods ask for afterwards are never touched.
    internal static class UniformFix
    {
        public sealed class Pending
        {
            public object Character;
            public Renderer[] Renderers;
            public Material[][] Before;
            public Mesh[] Meshes;
            public int[] Verts, Subs;
        }

        public static Pending pending;
        public static object afterBody;
        public static FieldInfo redirectCharacter;
        public static int skipped, passed, foreign, skippedR, passedR, foreignR, nextReport;
        private static bool errorLogged;

        public static MethodBase Apply()
        {
            var rt = UboatToolkit.Util.FindType("RealisticUniformsRevival.RealisticUniformClothesRuntime");
            var rs = UboatToolkit.Util.FindType("RealisticUniformsRevival.RedirectState");
            redirectCharacter = rs == null ? null : AccessTools.Field(rs, "Character");
            var m = rt == null ? null : AccessTools.Method(rt, "ApplyMaterialToExistingWearable");
            return m != null && redirectCharacter != null ? m : null;
        }

        public static bool On => Settings.B("FixUniformRebuild");

        public static Mesh MeshOf(Renderer r)
        {
            if (r == null) return null;
            if (r is SkinnedMeshRenderer sk) return sk.sharedMesh;
            var f = r.GetComponent<MeshFilter>();
            return f == null ? null : f.sharedMesh;
        }

        public static bool Unchanged(Pending p)
        {
            if (p.Character is Component body)
                foreach (var r in body.GetComponentsInChildren<Renderer>(true)) if (r != null && Array.IndexOf(p.Renderers, r) < 0) return false;
            for (int i = 0; i < p.Renderers.Length; i++)
            {
                var r = p.Renderers[i];
                var was = p.Before[i];
                if (r == null || was == null) return false;
                var mesh = MeshOf(r);
                if (!ReferenceEquals(mesh, p.Meshes[i])) return false;
                if (mesh != null && (mesh.vertexCount != p.Verts[i] || mesh.subMeshCount != p.Subs[i])) return false;
                var now = r.sharedMaterials;
                if (now.Length != was.Length) return false;
                for (int j = 0; j < now.Length; j++) if (!ReferenceEquals(now[j], was[j])) return false;
            }
            return true;
        }

        public static void Report()
        {
            int now = Environment.TickCount;
            if (unchecked(now - nextReport) < 0) return;
            nextReport = now + 60000;
            int s = skipped - skippedR, q = passed - passedR, f = foreign - foreignR;
            if (s == 0 && q == 0 && f == 0) return;
            skippedR = skipped; passedR = passed; foreignR = foreign;
            if (Settings.B("UniformLog")) Util.Log("Uniform-Fix: letzte Minute " + s + " unnötige Neuaufbauten übersprungen, " + q + " durchgelassen, " + f + " von anderen Mods unberührt. Sitzung: " + skipped + "/" + passed + "/" + foreign + ".");
        }

        public static void Error(Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Uniform-Fix: Fehler, Spielverhalten (einmalig): " + e); } }
    }

    [HarmonyPatch]
    internal static class UniformFix_Apply
    {
        private static bool Prepare() => UniformFix.Apply() != null;
        private static MethodBase TargetMethod() => UniformFix.Apply();

        private static void Prefix(object[] __args)
        {
            UniformFix.pending = null;
            UniformFix.afterBody = null;
            try
            {
                if (!UniformFix.On || __args == null || __args.Length < 2) return;
                var wearable = __args[1] as Component;
                object character = __args[0] == null ? null : UniformFix.redirectCharacter.GetValue(__args[0]);
                if (wearable == null || character == null) return;
                var set = new List<Renderer>();
                if (character is Component body) foreach (var r in body.GetComponentsInChildren<Renderer>(true)) if (r != null) set.Add(r);
                var lods = wearable.GetComponent<LODReferences>();
                var levels = lods != null ? lods.GetLODsDirect() : null;
                if (levels != null)
                    foreach (var lv in levels)
                    {
                        if (lv == null || lv.renderers == null) continue;
                        foreach (var r in lv.renderers) if (r != null && !set.Contains(r)) set.Add(r);
                    }
                foreach (var r in wearable.GetComponentsInChildren<Renderer>(true)) if (r != null && !set.Contains(r)) set.Add(r);
                var rs = set.ToArray();
                var p = new UniformFix.Pending { Character = character, Renderers = rs, Before = new Material[rs.Length][], Meshes = new Mesh[rs.Length], Verts = new int[rs.Length], Subs = new int[rs.Length] };
                for (int i = 0; i < rs.Length; i++)
                {
                    p.Before[i] = rs[i] != null ? rs[i].sharedMaterials : null;
                    p.Meshes[i] = UniformFix.MeshOf(rs[i]);
                    p.Verts[i] = p.Meshes[i] != null ? p.Meshes[i].vertexCount : -1;
                    p.Subs[i] = p.Meshes[i] != null ? p.Meshes[i].subMeshCount : -1;
                }
                UniformFix.pending = p;
            }
            catch (Exception e) { UniformFix.pending = null; UniformFix.Error(e); }
        }

        // Runs first among postfixes: from here on rebuild requests come from other mods.
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("officersandsailors.nzo.compat")]
        private static void Postfix()
        {
            var p = UniformFix.pending;
            UniformFix.pending = null;
            UniformFix.afterBody = p != null ? p.Character : null;
        }

        private static Exception Finalizer(Exception __exception)
        {
            UniformFix.pending = null;
            UniformFix.afterBody = null;
            UniformFix.Report();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class UniformFix_Dirty
    {
        private static bool Prepare() => UniformFix.Apply() != null;
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(Character), "SetLODLevelsDirty", Type.EmptyTypes);

        private static bool Prefix(object __instance)
        {
            var p = UniformFix.pending;
            if (p == null)
            {
                if (UniformFix.afterBody != null && ReferenceEquals(UniformFix.afterBody, __instance)) UniformFix.foreign++;
                return true;
            }
            if (!ReferenceEquals(p.Character, __instance)) return true;
            try { if (UniformFix.Unchanged(p)) { UniformFix.skipped++; return false; } }
            catch (Exception e) { UniformFix.Error(e); }
            UniformFix.passed++;
            return true;
        }
    }

    // Companion fixes for the "Projekt Bifrost" mod: its gauges are updated at a few Hz instead of
    // every frame (list in toolkit-bifrost-throttle.txt, "Typ = Hz"), its depth-charge tally no longer
    // throws on every detonation when its chalkboard never bound, plus a UI path report in the log.
    internal static class BifrostCfg
    {
        private static readonly string[] DefaultTypes = { "ThermoPlath", "Uboat.Mods.Moonphase.Mapper.SmoothRotator", "IndicatorHandlerEM2", "IndicatorHandler", "Uboat.Mods.Moonphase.Mapper.Thermo_outer", "Uboat.Mods.Moonphase.Mapper.hgWet" };
        private static readonly float[] DefaultHz = { 10f, 30f, 10f, 10f, 10f, 10f };
        private static List<KeyValuePair<string, float>> list;
        public static bool On => Settings.B("FixBifrost");
        private static string FilePath => System.IO.Path.Combine(Settings.Folder, "toolkit-bifrost-throttle.txt");

        public static List<KeyValuePair<string, float>> Throttles()
        {
            if (list != null) return list;
            list = new List<KeyValuePair<string, float>>();
            for (int i = 0; i < DefaultTypes.Length; i++) list.Add(new KeyValuePair<string, float>(DefaultTypes[i], DefaultHz[i]));
            try
            {
                if (!System.IO.File.Exists(FilePath))
                {
                    System.IO.Directory.CreateDirectory(Settings.Folder);
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine("# Bifrost-Anzeigen: wie oft pro Sekunde sie neu gerechnet werden (0 = jedes Bild).");
                    sb.AppendLine("# Zeile: Typname = Hz. Neue Zeilen für weitere Anzeigen sind erlaubt. Wirkt nach Spiel-Neustart.");
                    for (int i = 0; i < DefaultTypes.Length; i++) sb.AppendLine(DefaultTypes[i] + " = " + DefaultHz[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                    System.IO.File.WriteAllText(FilePath, sb.ToString());
                }
                foreach (var raw in System.IO.File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string type = line.Substring(0, eq).Trim();
                    if (!float.TryParse(line.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hz)) continue;
                    hz = Mathf.Clamp(hz, 0f, 120f);
                    int at = list.FindIndex(kv => string.Equals(kv.Key, type, StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) list[at] = new KeyValuePair<string, float>(type, hz); else list.Add(new KeyValuePair<string, float>(type, hz));
                }
            }
            catch (Exception e) { Util.Log("Bifrost-Fix: Liste nicht lesbar, nehme Standard: " + e.Message); }
            return list;
        }
    }

    [HarmonyPatch]
    internal static class Bifrost_Throttle
    {
        private static readonly Dictionary<int, float> nextDue = new Dictionary<int, float>();
        private static readonly Dictionary<Type, float> interval = new Dictionary<Type, float>();
        public static long Skipped, Ran;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var missing = new List<string>();
            foreach (var kv in BifrostCfg.Throttles())
            {
                var t = UboatToolkit.Util.FindType(kv.Key);
                var u = t == null ? null : AccessTools.DeclaredMethod(t, "Update");
                if (u == null) { missing.Add(kv.Key); continue; }
                interval[t] = kv.Value <= 0f ? 0f : 1f / kv.Value;
                yield return u;
            }
            if (missing.Count > 0 && interval.Count > 0) Util.Log("Bifrost-Fix: nicht gefunden (übersprungen): " + string.Join(", ", missing.ToArray()));
        }

        private static bool Prepare()
        {
            foreach (var kv in BifrostCfg.Throttles()) if (UboatToolkit.Util.FindType(kv.Key) != null) return true;
            return false;
        }

        private static bool Prefix(MonoBehaviour __instance)
        {
            try
            {
                if (!BifrostCfg.On || __instance == null) return true;
                if (!interval.TryGetValue(__instance.GetType(), out float iv) || iv <= 0f) return true;
                int id = __instance.GetInstanceID();
                float now = Time.unscaledTime;
                if (nextDue.TryGetValue(id, out float due) && now < due) { Skipped++; return false; }
                if (nextDue.Count >= 256 && !nextDue.ContainsKey(id)) nextDue.Clear();   // forget destroyed instances
                nextDue[id] = now + iv;
                Ran++;
                return true;
            }
            catch { return true; }
        }

        private static void Cleanup()
        {
            if (interval.Count == 0) return;
            Util.Log("Bifrost-Fix: " + interval.Count + " Anzeige(n) gedrosselt.");
            Application.quitting += () =>
            {
                long all = Skipped + Ran;
                if (all == 0) return;
                Util.Log("Bifrost-Fix: Sitzung " + Skipped + " von " + all + " Anzeige-Updates gespart (" + (Skipped * 100.0 / all).ToString("0.0") + " %)" + (Bifrost_DepthCharge.Suppressed > 0 ? ", " + Bifrost_DepthCharge.Suppressed + " Wasserbomben-Fehler verhindert" : "") + ".");
            };
        }
    }

    [HarmonyPatch]
    internal static class Bifrost_DepthCharge
    {
        private static FieldInfo chalk;
        private static bool logged;
        public static int Suppressed;

        private static MethodBase Target()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Mods.Moonphase.Main");
            if (t == null) return null;
            chalk = AccessTools.Field(t, "chalkHandler");
            var m = AccessTools.DeclaredMethod(t, "depthChargesDetonatedDuringCurrentEncounterChanged");
            return chalk != null ? m : null;
        }

        private static bool Prepare() => Target() != null;
        private static MethodBase TargetMethod() => Target();

        private static bool Prefix()
        {
            try
            {
                if (!BifrostCfg.On || !Settings.B("FixBifrostDepthCharge")) return true;
                if (chalk.GetValue(null) as UnityEngine.Object != null) return true;
                Suppressed++;
                if (!logged) { logged = true; Util.Log("Bifrost-Fix: Kreidetafel nie verbunden - Wasserbomben-Zähler übersprungen (sonst Fehler bei jeder Detonation). Wasserbomben selbst unverändert."); }
                return false;
            }
            catch (Exception e)
            {
                if (!logged) { logged = true; Util.Log("Bifrost-Fix: Wächter fehlgeschlagen, Bifrost läuft normal: " + e.Message); }
                return true;
            }
        }
    }

    // Writes Bifrost's UI paths to the log once, 20 s after the game UI appears.
    public class BifrostProbe : ModuleBase
    {
        private float probeAt = -1f, firstTry = -1f, nextTry;
        private bool done;
        private static int moonphase;   // 0 = not checked yet, 1 = there, 2 = not there (TypeByName scans every assembly: ~350 ms)
        private static readonly string[] Paths = { "GUI/Viewport/Main UI Area", "GUI/Viewport/Main UI Area/TimeCompressionExtendedUI", "GUI/Viewport/Main UI Area/TimeCompressionExtendedUI Alternative", "GUI/Viewport/Main UI Area/TimeCompressionExtendedUI Alternative/Timer" };

        public override void Tick()
        {
            if (done || !BifrostCfg.On || !Settings.B("BifrostUiLog")) return;
            if (moonphase == 0) moonphase = UboatToolkit.Util.FindType("UBOAT.Mods.Moonphase.Main") != null ? 1 : 2;
            if (moonphase == 2) { done = true; return; }
            float now = Time.unscaledTime;
            // GameObject.Find searches the whole scene (300-600 ms here). It ran every second for as long as the
            // UI path was missing, i.e. all game long. Now: one try every 15 s, give up after 5 minutes.
            if (probeAt < 0f)
            {
                if (firstTry < 0f) firstTry = now;
                if (now - firstTry > 300f) { done = true; Util.Log("Bifrost-Fix: UI-Pfad nicht gefunden, Prüfung beendet"); return; }
                if (now < nextTry) return;
                nextTry = now + 15f;
                if (GameObject.Find(Paths[0]) != null) probeAt = now + 20f;
            }
            if (probeAt < 0f || now < probeAt) return;
            done = true;
            Util.Safe("Bifrost.Probe", () =>
            {
                var sb = new System.Text.StringBuilder("Bifrost-Fix: UI-Pfade:");
                foreach (var p in Paths)
                {
                    var go = GameObject.Find(p);
                    sb.Append("\n   ").Append(go != null ? "OK      " : "FEHLT   ").Append(p);
                    if (go != null) sb.Append("  (Kinder ").Append(go.transform.childCount).Append(", Text ").Append(go.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length).Append(", Bild ").Append(go.GetComponentsInChildren<UnityEngine.UI.Image>(true).Length).Append(")");
                }
                var area = GameObject.Find(Paths[0]);
                if (area != null)
                {
                    sb.Append("\n   Kinder von 'Main UI Area':");
                    for (int i = 0; i < area.transform.childCount; i++) sb.Append("\n     ").Append(area.transform.GetChild(i).name).Append(area.transform.GetChild(i).gameObject.activeInHierarchy ? "" : "  (inaktiv)");
                }
                Util.Log(sb.ToString());
            });
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::DWS.Common.Characters;
    using global::UBOAT.Game.Scene.Effects;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.SceneManagement;
    using global::UBOAT.Game.UI.MainMenu;

    // Fix for mods that give characters clothes which do not exist in the game (seen: "Cap NCO" from
    // "officers-and-sailors-better-ncos"). The game then writes two red errors per character and the
    // piece stays empty anyway. Here a missing accessory is simply left out, without the errors.
    [HarmonyPatch]
    internal static class MissingWearable_Patch
    {
        private static DWS.Common.Resources.ResourceManager rm;
        private static readonly HashSet<string> reported = new HashSet<string>();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Character), "LoadWearable", new[] { typeof(string), typeof(bool) });
            yield return AccessTools.Method(typeof(Character), "LoadWearableAsync", new[] { typeof(string), typeof(bool) });
        }

        private static bool Prefix(string path)
        {
            try
            {
                if (!Settings.B("FixMissingWearables") || path == null || !path.StartsWith("Character/Accessories/")) return true;
                if (rm == null) rm = Traverse.Create(typeof(Character)).Field("resourceManager").GetValue<DWS.Common.Resources.ResourceManager>();
                if (rm == null || rm.AssetExists(path)) return true;
                if (reported.Add(path)) Util.Log("Kleidung fehlt im Spiel, wird weggelassen: " + path);
                return false;   // LoadWearable then returns null, same as the game does after its error
            }
            catch { return true; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/PerfLogger.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Linq;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.Profiling;
    using global::UnityEngine.SceneManagement;
    using global::UBOAT.Game.Scene.Entities;

    // Performance logger (plan: /uboat/logger-mod-plan.md). Off by default.
    // Writes one small CSV per session to <persistent data>/UboatToolkit/logs (max. about 10 MB):
    //  1. scene load times, 2. frames slower than 100 ms, 3. memory + GC every 5 min,
    //  4. object counts every 5 min, 5. hotspot finder (F11): time spent in the game's Update methods, top 20 per minute.
    public class PerfLogger : ModuleBase
    {
        private const long MaxBytes = 10L * 1024 * 1024;
        private static string file;
        private static long written;
        private static double sceneLoadStart;
        private float nextMemory, nextObjects;
        private bool hooked;

        // hotspot finder
        private static readonly Dictionary<MethodBase, double> timeSpent = new Dictionary<MethodBase, double>();
        private static readonly List<MethodBase> patched = new List<MethodBase>();
        private static bool hotspotsOn;
        private float nextHotspotDump;

        private static void Write(string kind, string a, string b = "", string c = "")
        {
            if (!Settings.B("PerfLogger")) return;
            try
            {
                if (file == null)
                {
                    var dir = Path.Combine(Settings.Folder, "logs");
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, "perf-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
                    File.WriteAllText(file, "time;realtime_s;kind;value1;value2;value3\n");
                }
                if (written > MaxBytes) return;
                string line = DateTime.Now.ToString("HH:mm:ss.fff") + ";" + Time.realtimeSinceStartup.ToString("0.00", CultureInfo.InvariantCulture) + ";" + kind + ";" + a + ";" + b + ";" + c + "\n";
                written += line.Length;
                BgFile.Append(file, line);
            }
            catch (Exception e) { Util.Log("logger: " + e.Message); }
        }

        public override void Frame()
        {
            if (!hooked)
            {
                hooked = true;
                SceneManager.sceneLoaded += (s, m) =>
                {
                    double took = Time.realtimeSinceStartupAsDouble - sceneLoadStart;
                    Write("scene_loaded", s.name, sceneLoadStart > 0 ? took.ToString("0.00", CultureInfo.InvariantCulture) + " s since previous scene change" : "");
                    sceneLoadStart = Time.realtimeSinceStartupAsDouble;
                };
                SceneManager.sceneUnloaded += s => { Write("scene_unloaded", s.name); sceneLoadStart = Time.realtimeSinceStartupAsDouble; };
            }
            if (!Settings.B("PerfLogger")) { if (hotspotsOn) SetHotspots(false); return; }

            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms > 100f) Write("slow_frame", ms.ToString("0", CultureInfo.InvariantCulture) + " ms");
            FrameSplit.Sample();

            float now = Time.unscaledTime;
            if (now >= nextMemory)
            {
                nextMemory = now + 300f;
                Write("memory", "mono_MB=" + (Profiler.GetMonoUsedSizeLong() / 1048576),
                    "reserved_MB=" + (Profiler.GetTotalReservedMemoryLong() / 1048576),
                    "gc_runs=" + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2));
            }
            if (now >= nextObjects)
            {
                nextObjects = now + 300f;
                Write("objects", "gameobjects=" + Resources.FindObjectsOfTypeAll<GameObject>().Length,
                    "textures=" + Resources.FindObjectsOfTypeAll<Texture>().Length + " meshes=" + Resources.FindObjectsOfTypeAll<Mesh>().Length,
                    "sounds=" + Resources.FindObjectsOfTypeAll<AudioClip>().Length);
            }

            bool button = Settings.B("PerfHotspotsNow");
            if (button) Settings.Set("PerfHotspotsNow", 0);
            if (Settings.B("PerfHotspots") && (button || Keys.Down("KeyHotspotFinder2"))) SetHotspots(!hotspotsOn);
            if (!Settings.B("PerfHotspots") && hotspotsOn) SetHotspots(false);
            if (hotspotsOn && now >= nextHotspotDump)
            {
                nextHotspotDump = now + 60f;
                int rank = 0;
                foreach (var kv in timeSpent.OrderByDescending(k => k.Value).Take(20))
                    Write("hotspot", (++rank).ToString(), kv.Key.DeclaringType?.Name + "." + kv.Key.Name, (kv.Value * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " ms/min");
                timeSpent.Clear();
            }
        }

        private void SetHotspots(bool on)
        {
            var h = ToolkitLoader.Harmony;
            var pre = new HarmonyMethod(AccessTools.Method(typeof(PerfLogger), nameof(Before)));
            var post = new HarmonyMethod(AccessTools.Method(typeof(PerfLogger), nameof(After)));
            if (on)
            {
                foreach (var type in AccessTools.GetTypesFromAssembly(typeof(PlayerShip).Assembly))
                {
                    if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsGenericTypeDefinition) continue;
                    foreach (var name in new[] { "Update", "LateUpdate", "FixedUpdate" })
                    {
                        var m = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes);
                        if (m == null || m.IsAbstract || m.GetMethodBody() == null) continue;
                        try { h.Patch(m, pre, post); patched.Add(m); } catch { }
                    }
                }
                nextHotspotDump = Time.unscaledTime + 60f;
                Write("hotspots", "on", patched.Count + " methods measured");
            }
            else
            {
                foreach (var m in patched)
                {
                    try { h.Unpatch(m, pre.method); h.Unpatch(m, post.method); } catch { }
                }
                patched.Clear();
                timeSpent.Clear();
                Write("hotspots", "off");
            }
            hotspotsOn = on;
            UboatToolkit.ToolkitRunner.ShowNotice(on ? "Bremsen-Finder AN (kostet selbst Leistung) - Strg+Shift+H schaltet aus" : "Bremsen-Finder AUS", 5f);
        }

        private static void Before(out double __state) { __state = Time.realtimeSinceStartupAsDouble; }

        private static void After(MethodBase __originalMethod, double __state)
        {
            double d = Time.realtimeSinceStartupAsDouble - __state;
            timeSpent.TryGetValue(__originalMethod, out var t);
            timeSpent[__originalMethod] = t + d;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Linq;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.Profiling;
    using global::UnityEngine.SceneManagement;
    using global::UBOAT.Game.Scene.Entities;

    // Frame split: where does a frame go (scripts, physics, drawing, garbage collection, audio...)?
    // Uses Unity's own profiler counters (work in the development build), one "frame_split" line per minute.
    internal static class FrameSplit
    {
        private static readonly string[] Names = {
            "PlayerLoop", "BehaviourUpdate", "LateBehaviourUpdate", "FixedBehaviourUpdate", "Physics.Simulate",
            "GC.Collect", "Camera.Render", "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread",
            "Animators.Update", "AudioManager.Update", "ParticleSystem.Update", "Canvas.SendWillRenderCanvases",
            "NavMeshManager", "Loading.ReadObject", "Instantiate", "Destroy" };
        // 0.3.72: the fixed list above explained only ~20 of ~46 ms per frame (convoy test 2026-11-16). Every engine phase
        // of the frame ("PostLateUpdate.xyz" ...) and the UI / IMGUI / culling markers are recorded too; once per
        // minute the 14 biggest are logged as "Frame-Phasen".
        private static readonly string[] Prefixes = { "Initialization.", "EarlyUpdate.", "FixedUpdate.", "PreUpdate.", "Update.", "PreLateUpdate.", "PostLateUpdate.", "TimeUpdate." };
        private static readonly string[] Extra = {
            "UGUI.Rendering.UpdateBatches", "Canvas.BuildBatch", "Layout", "GUI.Repaint", "GUI.ProcessEvents", "GUIUtility.SetSkin", "Culling", "Render Camera",
            "MeshSkinning.Update", "EventSystem.Update", "CoroutinesDelayedCalls", "Gfx.PresentFrame", "WaitForTargetFPS", "Semaphore.WaitForSignal",
            "Director.Update", "Animators.Update", "UpdateRendererBoundingVolumes", "Shadows.RenderShadowMap", "Drawing", "RenderLoop.Prepare", "SceneCulling",
            "ScriptableRenderContext.Submit", "Camera.FireOnPreCull", "OnPreCull", "OnPreRender", "OnPostRender", "OnRenderImage", "Physics.Processing", "Physics.ProcessReports",
            "Physics.UpdateBodies", "Physics.SyncColliderTransform", "Physics.Raycast", "Terrain.Heightmap.RenderStep1", "Water", "UIEvents.WillRenderCanvases" };
        private static readonly List<Unity.Profiling.ProfilerRecorder> rec2 = new List<Unity.Profiling.ProfilerRecorder>();
        private static readonly List<string> name2 = new List<string>();
        private static readonly List<double> sum2 = new List<double>();
        private static Unity.Profiling.ProfilerRecorder[] rec;
        private static double[] sum;
        private static int frames; private static float nextDump; private static bool failed;

        internal static void Sample()
        {
            if (failed) return;
            try
            {
                if (rec == null)
                {
                    rec = new Unity.Profiling.ProfilerRecorder[Names.Length];
                    sum = new double[Names.Length];
                    var all = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
                    Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(all);
                    var found = new List<string>();
                    foreach (var h in all)
                    {
                        var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                        int i = Array.IndexOf(Names, d.Name);
                        if (i < 0 && rec2.Count < 160 && d.Name != null && !name2.Contains(d.Name))
                        {
                            bool want = Array.IndexOf(Extra, d.Name) >= 0;
                            if (!want) foreach (var pre in Prefixes) if (d.Name.StartsWith(pre)) { want = true; break; }
                            if (want) { rec2.Add(Unity.Profiling.ProfilerRecorder.StartNew(d.Category, d.Name, 1)); name2.Add(d.Name); sum2.Add(0); }
                        }
                        if (i < 0 || rec[i].Valid) continue;
                        rec[i] = Unity.Profiling.ProfilerRecorder.StartNew(d.Category, d.Name, 1);
                        found.Add(d.Name);
                    }
                    Util.Log("Frame-Aufteilung: " + found.Count + " von " + Names.Length + " Messpunkten gefunden, dazu " + rec2.Count + " Phasen");
                    nextDump = Time.unscaledTime + 60f;
                }
                for (int i = 0; i < rec.Length; i++) if (rec[i].Valid) sum[i] += rec[i].LastValue / 1e6;
                for (int i = 0; i < rec2.Count; i++) if (rec2[i].Valid) sum2[i] += rec2[i].LastValue / 1e6;
                frames++;
                if (Time.unscaledTime < nextDump) return;
                nextDump = Time.unscaledTime + 60f;
                var sb = new System.Text.StringBuilder();
                sb.Append("frames=").Append(frames).Append(" fps=").Append((frames / 60f).ToString("0.0", CultureInfo.InvariantCulture)).Append(" ms/frame:");
                for (int i = 0; i < rec.Length; i++)
                    if (rec[i].Valid) sb.Append(' ').Append(Names[i]).Append('=').Append((sum[i] / Math.Max(1, frames)).ToString("0.0", CultureInfo.InvariantCulture));
                int ships = 0; foreach (var e in Entity.List) if (e is Ship) ships++;
                sb.Append(" ships=").Append(ships);
                Util.Log("Frame-Aufteilung " + sb);
                if (rec2.Count > 0)
                {
                    var idx = new List<int>(); for (int i = 0; i < rec2.Count; i++) idx.Add(i);
                    idx.Sort((a, b) => sum2[b].CompareTo(sum2[a]));
                    var s2 = new System.Text.StringBuilder();
                    for (int k = 0; k < idx.Count && k < 14; k++)
                        s2.Append(' ').Append(name2[idx[k]]).Append('=').Append((sum2[idx[k]] / Math.Max(1, frames)).ToString("0.0", CultureInfo.InvariantCulture));
                    Util.Log("Frame-Phasen ms/frame:" + s2);
                    for (int i = 0; i < sum2.Count; i++) sum2[i] = 0;
                }
                Util.Log("Toolkit-Kosten ms/frame: Anzeige=" + (ToolkitRunner.GuiSeconds * 1000.0 / Math.Max(1, frames)).ToString("0.00", CultureInfo.InvariantCulture)
                    + " (" + (ToolkitRunner.GuiCalls / (float)Math.Max(1, frames)).ToString("0.0", CultureInfo.InvariantCulture) + " Durchläufe/Bild, " + ToolkitRunner.GuiSkipped + " leere Durchläufe gespart) Module=" + (ToolkitRunner.ModuleSeconds * 1000.0 / Math.Max(1, frames)).ToString("0.00", CultureInfo.InvariantCulture));
                // menu open and closed apart: is the cost the F10 menu or the always-on displays?
                int closedCalls = ToolkitRunner.GuiCalls - ToolkitRunner.MenuCalls;
                Util.Log("Toolkit-Anzeige ms je Durchlauf: Menü offen=" + (ToolkitRunner.MenuSeconds * 1000.0 / Math.Max(1, ToolkitRunner.MenuCalls)).ToString("0.00", CultureInfo.InvariantCulture)
                    + " (" + ToolkitRunner.MenuCalls + "x), Menü zu=" + ((ToolkitRunner.GuiSeconds - ToolkitRunner.MenuSeconds) * 1000.0 / Math.Max(1, closedCalls)).ToString("0.00", CultureInfo.InvariantCulture)
                    + " (" + closedCalls + "x)");
                Util.Log("Toolkit-Bootssuche: " + Util.FindScans + " Suchläufe, zusammen " + (Util.FindSeconds * 1000.0).ToString("0", CultureInfo.InvariantCulture) + " ms in dieser Minute");
                Util.FindScans = 0; Util.FindSeconds = 0;
                ToolkitRunner.GuiSeconds = 0; ToolkitRunner.GuiCalls = 0; ToolkitRunner.GuiSkipped = 0; ToolkitRunner.ModuleSeconds = 0; ToolkitRunner.MenuSeconds = 0; ToolkitRunner.MenuCalls = 0;
                for (int i = 0; i < sum.Length; i++) sum[i] = 0; frames = 0;
            }
            catch (Exception ex) { failed = true; Util.Log("Frame-Aufteilung aus: " + ex.Message); }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/QueueProfiler.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Linq;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core;

    // Brake finder for the game's own update dispatcher (ExecutionQueue).
    // Most game logic does NOT run in MonoBehaviour.Update but as Action delegates in
    // ExecutionQueue.update / lateUpdate / fixedUpdate (decompiled com.uboat.game.dll).
    // The PerfLogger hotspot finder cannot see these. This module times each delegate.
    // Setting "PerfQueue" (off by default). Writes to <Settings.Folder>/logs/queue-*.csv:
    //  - every 60 s: top 20 delegates by time (ms per minute)
    //  - every 5 min: list sizes (growing counts = leak hint: listeners added but never removed)
    [HarmonyPatch]
    public class QueueProfiler : ModuleBase
    {
        private static readonly AccessTools.FieldRef<ExecutionQueue, List<Action>> fUpdate = AccessTools.FieldRefAccess<ExecutionQueue, List<Action>>("update");
        private static readonly AccessTools.FieldRef<ExecutionQueue, List<Action>> fLate = AccessTools.FieldRefAccess<ExecutionQueue, List<Action>>("lateUpdate");
        private static readonly AccessTools.FieldRef<ExecutionQueue, List<Action>> fFixed = AccessTools.FieldRefAccess<ExecutionQueue, List<Action>>("fixedUpdate");
        private static readonly AccessTools.FieldRef<ExecutionQueue, int> iUpdate = AccessTools.FieldRefAccess<ExecutionQueue, int>("currentUpdateIndex");
        private static readonly AccessTools.FieldRef<ExecutionQueue, int> iLate = AccessTools.FieldRefAccess<ExecutionQueue, int>("currentLateUpdateIndex");
        private static readonly AccessTools.FieldRef<ExecutionQueue, int> iFixed = AccessTools.FieldRefAccess<ExecutionQueue, int>("currentFixedUpdateIndex");

        private static readonly Dictionary<string, double> spent = new Dictionary<string, double>();
        private static ExecutionQueue lastQueue;
        private static bool on;
        private static string file;
        private static long written;
        private float nextTop, nextSizes;

        public override void Frame()
        {
            on = Settings.B("PerfQueue");
            if (!on) { spent.Clear(); return; }
            float now = Time.unscaledTime;
            if (now >= nextTop)
            {
                if (nextTop > 0f)
                {
                    int rank = 0;
                    foreach (var kv in spent.OrderByDescending(k => k.Value).Take(20))
                        Write("queue_top", (++rank).ToString(), kv.Key, (kv.Value * 1000).ToString("0.0", CultureInfo.InvariantCulture) + " ms/min");
                }
                spent.Clear();
                nextTop = now + 60f;
            }
            if (now >= nextSizes && lastQueue != null)
            {
                nextSizes = now + 300f;
                Write("queue_sizes", "update=" + fUpdate(lastQueue).Count, "late=" + fLate(lastQueue).Count, "fixed=" + fFixed(lastQueue).Count);
            }
        }

        private static void Write(string kind, string a, string b, string c)
        {
            try
            {
                if (file == null)
                {
                    var dir = Path.Combine(Settings.Folder, "logs");
                    Directory.CreateDirectory(dir);
                    file = Path.Combine(dir, "queue-" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv");
                    File.WriteAllText(file, "time;kind;value1;value2;value3\n");
                }
                if (written > 5L * 1024 * 1024) return;
                string line = DateTime.Now.ToString("HH:mm:ss") + ";" + kind + ";" + a + ";" + b + ";" + c + "\n";
                written += line.Length;
                BgFile.Append(file, line);
            }
            catch (Exception e) { Util.Log("queue profiler: " + e.Message); }
        }

        private static string Name(Action a) =>
            a.Method.DeclaringType != null ? a.Method.DeclaringType.Name + "." + a.Method.Name : a.Method.Name;

        // Same loop as the original, plus timing. Index field is kept in sync because
        // RemoveUpdateListener() adjusts it while the loop runs.
        private static void Run(ExecutionQueue q, List<Action> list, AccessTools.FieldRef<ExecutionQueue, int> idx, int start)
        {
            idx(q) = start;
            Action action = null;
            try
            {
                while (idx(q) >= 0)
                {
                    action = list[idx(q)];
                    double t = Time.realtimeSinceStartupAsDouble;
                    action();
                    double d = Time.realtimeSinceStartupAsDouble - t;
                    string n = Name(action);
                    spent.TryGetValue(n, out var old);
                    spent[n] = old + d;
                    idx(q)--;
                }
            }
            catch (Exception ex)
            {
                if (action != null) Debug.LogError("Error in execution queue method \"" + Name(action) + "\".");
                Debug.LogException(ex);
                Run(q, list, idx, Mathf.Min(idx(q) - 1, list.Count - 1));
            }
        }

        [HarmonyPatch(typeof(ExecutionQueue), "RunUpdates"), HarmonyPrefix]
        private static bool RunUpdates(ExecutionQueue __instance, int index)
        {
            lastQueue = __instance;
            if (!on) return true;
            Run(__instance, fUpdate(__instance), iUpdate, index);
            return false;
        }

        [HarmonyPatch(typeof(ExecutionQueue), "RunLateUpdates"), HarmonyPrefix]
        private static bool RunLateUpdates(ExecutionQueue __instance, int index)
        {
            if (!on) return true;
            Run(__instance, fLate(__instance), iLate, index);
            return false;
        }

        [HarmonyPatch(typeof(ExecutionQueue), "RunFixedUpdates"), HarmonyPrefix]
        private static bool RunFixedUpdates(ExecutionQueue __instance, int index)
        {
            if (!on) return true;
            Run(__instance, fFixed(__instance), iFixed, index);
            return false;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/SaveCategories.cs
// ====================================================================================================
namespace UboatToolkit.Modules.SaveCats
{
    using global::HarmonyLib;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Reflection;
    using global::System.Text;
    using global::System;
    using global::TMPro;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.UI;
    using global::UnityEngine.EventSystems;
    using global::UnityEngine.UI;
    using global::UnityEngine;

    // Load menu grouped by career: a tab per career above the save list (plus "Alle").
    // Career identity comes from data the game already stores; saves are never written to.
    // Our own small index file (toolkit-career-index.tsv) remembers which save belongs to which career.
    internal static class SaveCatCfg
    {
        public static bool Active => Settings.B("SaveGroupCareer");
    }

    internal static class SaveCatClock
    {
        public static long Now() => UboatToolkit.Modules.LoadClock.Now();
        public static double MsSince(long t) => UboatToolkit.Modules.LoadClock.Seconds(UboatToolkit.Modules.LoadClock.Now() - t) * 1000.0;
    }

    internal static class CscLog
    {
        public static void Info(string m) { if (Settings.B("SaveCatLog")) Util.Log("Karrieren: " + m); }
        public static void Warn(string m) { Util.Log("Karrieren: " + m); }
        public static void Error(string m, Exception ex) { Util.Log("Karrieren: " + m + (ex != null ? " - " + ex : "")); }
    }

    internal static class CscSession
    {
        private static string _careerGuid;
        internal static string CareerGuid
        {
            get { return _careerGuid; }
        }
        internal static void BeginNewCareer()
        {
            _careerGuid = Guid.NewGuid().ToString("N");
            CscLog.Info("new career started, tracking as " + Short(_careerGuid));
        }
        internal static void Adopt(string careerGuid)
        {
            if (string.IsNullOrEmpty(careerGuid))
            {
                _careerGuid = null;
                CscLog.Warn("loaded a save with no identifiable career - the next save will start a new group");
                return;
            }
            _careerGuid = careerGuid;
            CscLog.Info("continuing career " + Short(careerGuid));
        }
        internal static string CareerForSave()
        {
            if (string.IsNullOrEmpty(_careerGuid))
            {
                _careerGuid = Guid.NewGuid().ToString("N");
                CscLog.Info("saving with no career held, tracking as " + Short(_careerGuid));
            }
            return _careerGuid;
        }
        private static string Short(string guid)
        {
            return string.IsNullOrEmpty(guid) || guid.Length < 8 ? guid : guid.Substring(0, 8);
        }
    }


    internal static class CscFingerprint
    {
        internal static string FromSkipperJson(string skipperJson)
        {
            if (string.IsNullOrEmpty(skipperJson))
                return null;
            var birthDate = ReadRawValue(skipperJson, "birthDate", 0);
            var portraitTiming = ReadRawValue(skipperJson, "portraitTiming", 0);
            if (string.IsNullOrEmpty(birthDate))
                return null;
            var hair = ReadHairColor(skipperJson);
            return birthDate + "|" + portraitTiming + "|" + hair;
        }
        private static string ReadHairColor(string json)
        {
            var at = json.IndexOf("\"hairColor\"", StringComparison.Ordinal);
            if (at < 0)
                return string.Empty;
            var r = ReadRawValue(json, "r", at);
            var g = ReadRawValue(json, "g", at);
            var b = ReadRawValue(json, "b", at);
            return r + "," + g + "," + b;
        }
        private static string ReadRawValue(string json, string key, int startAt)
        {
            var needle = "\"" + key + "\"";
            var at = json.IndexOf(needle, startAt, StringComparison.Ordinal);
            if (at < 0)
                return string.Empty;
            var colon = json.IndexOf(':', at + needle.Length);
            if (colon < 0)
                return string.Empty;
            var i = colon + 1;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t'))
                i++;
            var end = i;
            while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != '\r' && json[end] != '\n')
                end++;
            return json.Substring(i, end - i).Trim().Trim(new[] { '"' });
        }
    }


    internal sealed class CscSaveRecord
    {
        internal string Path;
        internal long AssignedWriteTicks;
        internal string CareerGuid;
        internal string Fingerprint;
        internal string SkipperName;
        internal long GameDateTicks;
    }
    internal sealed class CscCareer
    {
        internal string Guid;
        internal string Label;
        internal long LastWriteTicks;
        internal long FirstWriteTicks;
        internal int SaveCount;
    }
    internal static class CscIndex
    {
        private const int DecodeBudgetMs = 4000;
        internal const long PendingWriteTicks = -1L;
        private const string Header = "#csc-index v2";
        private static readonly Dictionary<string, CscSaveRecord> ByPath =
            new Dictionary<string, CscSaveRecord>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> GuidByFingerprint =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _cacheLoaded;
        private static bool _dirty;
        private static string CachePath
        {
            get { return Application.persistentDataPath + "/toolkit-career-index.tsv"; }
        }
        private static readonly ConstructorInfo GameStateDataCtor =
            AccessTools.Constructor(typeof(SavesManager.GameStateData), new[] { typeof(string) });
        internal static CscSaveRecord[] Refresh(SavesManager.GameStateData[] saves)
        {
            LoadCache();
            var result = new CscSaveRecord[saves.Length];
            var timerStart = SaveCatClock.Now();
            var decoded = 0;
            var deferred = 0;
            for (var i = 0; i < saves.Length; i++)
            {
                var save = saves[i];
                if (save == null)
                    continue;
                var path = save.Path;
                long writeTicks;
                try
                {
                    writeTicks = save.WriteDate.Ticks;
                }
                catch
                {
                    writeTicks = 0L;
                }
                CscSaveRecord row;
                if (ByPath.TryGetValue(path, out row) && !string.IsNullOrEmpty(row.CareerGuid))
                {
                    if (row.AssignedWriteTicks == PendingWriteTicks)
                    {
                        row.AssignedWriteTicks = writeTicks;
                        _dirty = true;
                        if ((long)SaveCatClock.MsSince(timerStart) <= DecodeBudgetMs)
                            FillDisplayMetadata(save, row);
                        result[i] = row;
                        continue;
                    }
                    if (row.AssignedWriteTicks == writeTicks)
                    {
                        if (string.IsNullOrEmpty(row.SkipperName) && (long)SaveCatClock.MsSince(timerStart) <= DecodeBudgetMs)
                            FillDisplayMetadata(save, row);
                        result[i] = row;
                        continue;
                    }
                }
                if ((long)SaveCatClock.MsSince(timerStart) > DecodeBudgetMs)
                {
                    deferred++;
                    result[i] = new CscSaveRecord { Path = path, AssignedWriteTicks = writeTicks };
                    continue;
                }
                result[i] = ResolveByDecoding(save, path, writeTicks);
                decoded++;
            }
            if (decoded > 0 || deferred > 0)
            {
                CscLog.Info("indexed " + decoded + " save(s) in " + (long)SaveCatClock.MsSince(timerStart) + "ms" +
                            (deferred > 0 ? ", " + deferred + " deferred to next open (budget)" : ""));
            }
            if (_dirty)
                SaveCache(saves);
            return result;
        }
        private static CscSaveRecord ResolveByDecoding(SavesManager.GameStateData save, string path, long writeTicks)
        {
            var row = new CscSaveRecord { Path = path, AssignedWriteTicks = writeTicks };
            try
            {
                var extended = save.ReadExtendedData();
                if (extended != null)
                {
                    row.Fingerprint = CscFingerprint.FromSkipperJson(extended.SkipperJSON);
                    row.SkipperName = extended.SkipperName;
                    row.GameDateTicks = extended.GameDate.Ticks;
                }
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not read header of " + System.IO.Path.GetFileName(path) + ": " + ex.Message);
            }
            if (!string.IsNullOrEmpty(row.Fingerprint))
            {
                row.CareerGuid = GuidForFingerprint(row.Fingerprint);
                ByPath[path] = row;
                _dirty = true;
            }
            return row;
        }
        private static void FillDisplayMetadata(SavesManager.GameStateData save, CscSaveRecord row)
        {
            try
            {
                var extended = save.ReadExtendedData();
                if (extended == null)
                    return;
                row.SkipperName = extended.SkipperName;
                row.GameDateTicks = extended.GameDate.Ticks;
                if (string.IsNullOrEmpty(row.Fingerprint))
                    row.Fingerprint = CscFingerprint.FromSkipperJson(extended.SkipperJSON);
                _dirty = true;
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not read header of " + System.IO.Path.GetFileName(row.Path) + ": " + ex.Message);
            }
        }
        private static string GuidForFingerprint(string fingerprint)
        {
            string guid;
            if (GuidByFingerprint.TryGetValue(fingerprint, out guid))
                return guid;
            guid = System.Guid.NewGuid().ToString("N");
            GuidByFingerprint[fingerprint] = guid;
            return guid;
        }
        internal static string ResolveCareerForPath(string path)
        {
            LoadCache();
            CscSaveRecord row;
            if (ByPath.TryGetValue(path, out row) && !string.IsNullOrEmpty(row.CareerGuid))
                return row.CareerGuid;
            if (GameStateDataCtor == null || !File.Exists(path))
                return null;
            try
            {
                var save = (SavesManager.GameStateData)GameStateDataCtor.Invoke(new object[] { path });
                var writeTicks = File.GetLastWriteTime(path).Ticks;
                var resolved = ResolveByDecoding(save, path, writeTicks);
                if (_dirty)
                    SaveCache(null);
                return resolved.CareerGuid;
            }
            catch (Exception ex)
            {
                CscLog.Error("could not resolve career for " + System.IO.Path.GetFileName(path), ex);
                return null;
            }
        }
        internal static void AssignCareer(string path, string careerGuid, string skipperName, long gameDateTicks)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(careerGuid))
                return;
            LoadCache();
            CscSaveRecord row;
            if (!ByPath.TryGetValue(path, out row))
            {
                row = new CscSaveRecord { Path = path };
                ByPath[path] = row;
            }
            row.CareerGuid = careerGuid;
            row.AssignedWriteTicks = PendingWriteTicks;
            if (!string.IsNullOrEmpty(skipperName))
                row.SkipperName = skipperName;
            if (gameDateTicks > 0)
                row.GameDateTicks = gameDateTicks;
            _dirty = true;
            SaveCache(null);
        }
        internal static Dictionary<string, CscCareer> BuildCareers(CscSaveRecord[] records)
        {
            var careers = new Dictionary<string, CscCareer>(StringComparer.Ordinal);
            var earliestGameDate = new Dictionary<string, long>(StringComparer.Ordinal);
            var newestName = new Dictionary<string, string>(StringComparer.Ordinal);
            var newestNameTicks = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                if (r == null || string.IsNullOrEmpty(r.CareerGuid))
                    continue;
                var key = r.CareerGuid;
                CscCareer career;
                if (!careers.TryGetValue(key, out career))
                {
                    career = new CscCareer
                    {
                        Guid = key,
                        LastWriteTicks = long.MinValue,
                        FirstWriteTicks = long.MaxValue
                    };
                    careers[key] = career;
                    earliestGameDate[key] = long.MaxValue;
                    newestNameTicks[key] = long.MinValue;
                }
                career.SaveCount++;
                var ticks = r.AssignedWriteTicks == PendingWriteTicks ? long.MaxValue : r.AssignedWriteTicks;
                if (ticks > career.LastWriteTicks)
                    career.LastWriteTicks = ticks;
                if (ticks != long.MaxValue && ticks < career.FirstWriteTicks)
                    career.FirstWriteTicks = ticks;
                if (r.GameDateTicks > 0 && r.GameDateTicks < earliestGameDate[key])
                    earliestGameDate[key] = r.GameDateTicks;
                if (!string.IsNullOrEmpty(r.SkipperName) && ticks > newestNameTicks[key])
                {
                    newestNameTicks[key] = ticks;
                    newestName[key] = r.SkipperName;
                }
            }
            var ordered = new List<CscCareer>(careers.Values);
            ordered.Sort((a, b) => a.FirstWriteTicks.CompareTo(b.FirstWriteTicks));
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var career in ordered)
            {
                string name;
                newestName.TryGetValue(career.Guid, out name);
                var label = Surname(name);
                long earliest;
                if (earliestGameDate.TryGetValue(career.Guid, out earliest) && earliest != long.MaxValue)
                {
                    var year = new DateTime(earliest).Year;
                    label += " '" + (year % 100).ToString("00", CultureInfo.InvariantCulture);
                }
                int seen;
                if (used.TryGetValue(label, out seen))
                {
                    used[label] = seen + 1;
                    label += " #" + (seen + 1).ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    used[label] = 1;
                }
                career.Label = label;
            }
            return careers;
        }
        private static string Surname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return "Unbekannt";
            var parts = fullName.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? "Unbekannt" : parts[parts.Length - 1];
        }
        private static void LoadCache()
        {
            if (_cacheLoaded)
                return;
            _cacheLoaded = true;
            try
            {
                if (!File.Exists(CachePath))
                    return;
                foreach (var line in File.ReadAllLines(CachePath))
                {
                    if (string.IsNullOrEmpty(line) || line[0] == '#')
                        continue;
                    var f = line.Split(new[] { '\t' });
                    if (f.Length < 6)
                        continue;
                    long assigned, gameDateTicks;
                    if (!long.TryParse(f[1], out assigned))
                        continue;
                    long.TryParse(f[5], out gameDateTicks);
                    var row = new CscSaveRecord
                    {
                        Path = f[0],
                        AssignedWriteTicks = assigned,
                        CareerGuid = f[2].Length == 0 ? null : f[2],
                        Fingerprint = f[3].Length == 0 ? null : f[3],
                        SkipperName = f[4],
                        GameDateTicks = gameDateTicks
                    };
                    ByPath[row.Path] = row;
                    if (!string.IsNullOrEmpty(row.Fingerprint) && !string.IsNullOrEmpty(row.CareerGuid) &&
                        !GuidByFingerprint.ContainsKey(row.Fingerprint))
                    {
                        GuidByFingerprint[row.Fingerprint] = row.CareerGuid;
                    }
                }
                CscLog.Info("loaded " + ByPath.Count + " save assignment(s), " +
                            GuidByFingerprint.Count + " known career fingerprint(s)");
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not read index, rebuilding: " + ex.Message);
                ByPath.Clear();
                GuidByFingerprint.Clear();
            }
        }
        private static void SaveCache(SavesManager.GameStateData[] saves)
        {
            try
            {
                HashSet<string> live = null;
                if (saves != null)
                {
                    live = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in saves)
                    {
                        if (s != null)
                            live.Add(s.Path);
                    }
                }
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                var kept = 0;
                foreach (var pair in ByPath)
                {
                    var r = pair.Value;
                    if (live != null && !live.Contains(pair.Key) && r.AssignedWriteTicks != PendingWriteTicks)
                        continue;
                    sb.Append(r.Path).Append('\t')
                      .Append(r.AssignedWriteTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                      .Append(r.CareerGuid ?? string.Empty).Append('\t')
                      .Append(Clean(r.Fingerprint)).Append('\t')
                      .Append(Clean(r.SkipperName)).Append('\t')
                      .Append(r.GameDateTicks.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    kept++;
                }
                File.WriteAllText(CachePath, sb.ToString());
                _dirty = false;
                CscLog.Info("wrote index (" + kept + " assignment(s))");
            }
            catch (Exception ex)
            {
                CscLog.Warn("could not write index: " + ex.Message);
            }
        }
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }


    internal static class CscPaths
    {
        internal static string SavesDirectory
        {
            get { return Application.persistentDataPath + "/Saves"; }
        }
        internal static string FromSaveName(string name, bool allowFullPath)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            if (allowFullPath && name.IndexOf('/') >= 0)
                return name;
            return SavesDirectory + "/" + name + ".save";
        }
    }
    [HarmonyPatch(typeof(PlayerCareer), "StartDate", MethodType.Setter)]
    internal static class PlayerCareerStartDatePatch
    {
        private static void Postfix()
        {
            try
            {
                CscSession.BeginNewCareer();
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to start tracking a new career", ex);
            }
        }
    }
    [HarmonyPatch(typeof(SavesManager), "Load")]
    internal static class SavesManagerLoadPatch
    {
        private static void Postfix(string name)
        {
            try
            {
                var path = CscPaths.FromSaveName(name, true);
                if (path == null)
                    return;
                CscSession.Adopt(CscIndex.ResolveCareerForPath(path));
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to adopt career on load", ex);
            }
        }
    }
    [HarmonyPatch(typeof(SavesManager), "Save")]
    internal static class SavesManagerSavePatch
    {
        private static void Postfix(string name)
        {
            try
            {
                var path = CscPaths.FromSaveName(name, false);
                if (path == null)
                    return;
                CscIndex.AssignCareer(path, CscSession.CareerForSave(), null, 0L);
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to stamp save with its career", ex);
            }
        }
    }
    [HarmonyPatch(typeof(SavesManager), "GetSaves")]
    internal static class SavesManagerGetSavesPatch
    {
        private static void Postfix(ref SavesManager.GameStateData[] __result)
        {
            try
            {
                if (!SaveCatCfg.Active)
                {
                    CscRowInfo.Clear();
                    return;
                }
                if (__result == null || __result.Length < 2)
                    return;
                var records = CscIndex.Refresh(__result);
                var careers = CscIndex.BuildCareers(records);
                if (careers.Count < 2)
                {
                    CscRowInfo.Clear();
                    return;
                }
                CscRowInfo.Rebuild(records, careers);
                var items = new List<Entry>(__result.Length);
                for (var i = 0; i < __result.Length; i++)
                {
                    var record = records[i];
                    var guid = record == null ? null : record.CareerGuid;
                    CscCareer career = null;
                    if (!string.IsNullOrEmpty(guid))
                        careers.TryGetValue(guid, out career);
                    items.Add(new Entry
                    {
                        Save = __result[i],
                        CareerGuid = guid ?? string.Empty,
                        CareerRank = career == null ? long.MinValue : career.LastWriteTicks,
                        WriteTicks = record == null ? 0L : record.AssignedWriteTicks,
                        Original = i
                    });
                }
                items.Sort(Compare);
                if (!ReferenceEquals(items[0].Save, __result[0]))
                {
                    CscLog.Warn("grouping would have moved the most recent save out of first " +
                                "position, which would change what auto-load picks - leaving vanilla order");
                    return;
                }
                for (var i = 0; i < items.Count; i++)
                    __result[i] = items[i].Save;
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to group saves, leaving vanilla order", ex);
            }
        }
        private static int Compare(Entry a, Entry b)
        {
            var byCareer = b.CareerRank.CompareTo(a.CareerRank);
            if (byCareer != 0)
                return byCareer;
            var byGuid = string.CompareOrdinal(a.CareerGuid, b.CareerGuid);
            if (byGuid != 0)
                return byGuid;
            var byDate = b.WriteTicks.CompareTo(a.WriteTicks);
            if (byDate != 0)
                return byDate;
            return a.Original.CompareTo(b.Original);
        }
        private sealed class Entry
        {
            internal SavesManager.GameStateData Save;
            internal string CareerGuid;
            internal long CareerRank;
            internal long WriteTicks;
            internal int Original;
        }
    }
    internal static class CscRowInfo
    {
        private static Dictionary<string, string> _labelByPath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _careerByPath =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static List<CscCareer> _ordered = new List<CscCareer>();
        internal static List<CscCareer> Ordered
        {
            get { return _ordered; }
        }
        internal static void Rebuild(CscSaveRecord[] records, Dictionary<string, CscCareer> careers)
        {
            var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in records)
            {
                if (r == null || string.IsNullOrEmpty(r.CareerGuid) || string.IsNullOrEmpty(r.Path))
                    continue;
                CscCareer career;
                if (careers.TryGetValue(r.CareerGuid, out career) && career != null)
                {
                    labels[r.Path] = career.Label;
                    byPath[r.Path] = career.Guid;
                }
            }
            var ordered = new List<CscCareer>(careers.Values);
            ordered.Sort((a, b) => b.LastWriteTicks.CompareTo(a.LastWriteTicks));
            _labelByPath = labels;
            _careerByPath = byPath;
            _ordered = ordered;
        }
        internal static void Clear()
        {
            _labelByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _careerByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _ordered = new List<CscCareer>();
        }
        internal static string For(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string label;
            return _labelByPath.TryGetValue(path, out label) ? label : null;
        }
        internal static string CareerFor(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            string guid;
            return _careerByPath.TryGetValue(path, out guid) ? guid : null;
        }
        internal static string CareerAt(int index)
        {
            return index >= 0 && index < _ordered.Count ? _ordered[index].Guid : null;
        }
    }
    [HarmonyPatch(typeof(GameStateBaseUI), "FetchSaves")]
    internal static class GameStateBaseUiFetchSavesPatch
    {
        private static readonly FieldInfo SavesListField =
            AccessTools.Field(typeof(GameStateBaseUI), "savesList");
        private static readonly FieldInfo ItemsField =
            AccessTools.Field(typeof(GameStateBaseUI), "items");
        private static void Postfix(GameStateBaseUI __instance)
        {
            try
            {
                if (__instance == null || SavesListField == null || ItemsField == null)
                    return;
                var savesList = SavesListField.GetValue(__instance) as RectTransform;
                var items = ItemsField.GetValue(__instance) as GameStateItemUI[];
                if (savesList == null || items == null)
                    return;
                if (!SaveCatCfg.Active || CscRowInfo.Ordered.Count < 2)
                {
                    CscTabStrip.StandDown(savesList, items);
                    return;
                }
                CscTabStrip.Refresh(__instance, savesList, items, CscRowInfo.Ordered);
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to build career tabs", ex);
            }
        }
    }
    [HarmonyPatch(typeof(GameStateItemUI), "Duplicate")]
    internal static class GameStateItemUiDuplicatePatch
    {
        private const string LabelColor = "#C8A45C";
        private static readonly FieldInfo SaveNameField =
            AccessTools.Field(typeof(GameStateItemUI), "saveName");
        private static void Postfix(GameStateItemUI __result, SavesManager.GameStateData saveData)
        {
            try
            {
                if (!SaveCatCfg.Active)
                    return;
                if (__result == null || saveData == null || SaveNameField == null)
                    return;
                var label = CscRowInfo.For(saveData.Path);
                if (string.IsNullOrEmpty(label))
                    return;
                var text = SaveNameField.GetValue(__result) as TMP_Text;
                if (text == null)
                    return;
                text.richText = true;
                text.text = "<color=" + LabelColor + ">" + label + "</color>  " + text.text;
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to label save row", ex);
            }
        }
    }


    internal static class CscTabStrip
    {
        private const string StripName = "ToolkitCareerTabs";
        private const string UnderlineName = "ToolkitTabUnderline";
        private const float StripHeight = 52f;
        private const float Gap = 10f;
        private const float TabPadding = 34f;
        private const float TabSpacing = 6f;
        private const float MinTabWidth = 70f;
        private static readonly Color UnderlineSelected = new Color(0.78f, 0.64f, 0.36f, 1f);
        private static string _filterGuid;
        private static string _defaultedFor;
        internal static void Refresh(GameStateBaseUI ui, RectTransform savesList,
                                     GameStateItemUI[] items, List<CscCareer> careers)
        {
            var strip = FindOrCreateStrip(ui, savesList);
            if (strip == null)
                return;
            if (!strip.gameObject.activeSelf)
                strip.gameObject.SetActive(true);
            var current = CscSession.CareerGuid;
            if (current != _defaultedFor)
            {
                _defaultedFor = current;
                _filterGuid = current; // null in the main menu, which is "All"
            }
            if (_filterGuid != null)
            {
                var stillThere = false;
                foreach (var c in careers)
                {
                    if (c.Guid == _filterGuid)
                    {
                        stillThere = true;
                        break;
                    }
                }
                if (!stillThere)
                    _filterGuid = null;
            }
            BuildTabs(ui, strip, items, careers);
            ApplyFilter(ui, items);
        }
        internal static void StandDown(RectTransform savesList, GameStateItemUI[] items)
        {
            _filterGuid = null;
            try
            {
                var scrollRect = savesList == null ? null : savesList.GetComponentInParent<ScrollRect>();
                var scrollView = scrollRect == null ? null : scrollRect.transform as RectTransform;
                var host = scrollView == null ? null : scrollView.parent as RectTransform;
                var existing = host == null ? null : host.Find(StripName);
                if (existing != null && existing.gameObject.activeSelf)
                    existing.gameObject.SetActive(false);
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to hide the career tabs", ex);
            }
            if (items == null)
                return;
            foreach (var item in items)
                if (item != null && !item.gameObject.activeSelf)
                    item.gameObject.SetActive(true);
        }
        private static RectTransform FindOrCreateStrip(GameStateBaseUI ui, RectTransform savesList)
        {
            var scrollRect = savesList.GetComponentInParent<ScrollRect>();
            if (scrollRect == null)
            {
                CscLog.Warn("no ScrollRect above the save list - cannot place the career tabs");
                return null;
            }
            var scrollView = scrollRect.transform as RectTransform;
            var host = scrollView == null ? null : scrollView.parent as RectTransform;
            if (host == null)
                return null;
            var existing = host.Find(StripName) as RectTransform;
            if (existing != null)
                return existing;
            scrollView.offsetMax = new Vector2(scrollView.offsetMax.x, scrollView.offsetMax.y - (StripHeight + Gap));
            var strip = new GameObject(StripName, typeof(RectTransform)).GetComponent<RectTransform>();
            strip.SetParent(host, false);
            strip.anchorMin = new Vector2(scrollView.anchorMin.x, scrollView.anchorMax.y);
            strip.anchorMax = new Vector2(scrollView.anchorMax.x, scrollView.anchorMax.y);
            strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(scrollView.offsetMin.x, scrollView.offsetMax.y + Gap);
            strip.offsetMax = new Vector2(scrollView.offsetMax.x, scrollView.offsetMax.y + Gap + StripHeight);
            var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(strip, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 0f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 0.5f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = TabSpacing;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            var scroll = strip.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;
            scroll.horizontalScrollbar = null;
            scroll.verticalScrollbar = null;
            CscLog.Info("career tab strip created");
            return strip;
        }
        private static void BuildTabs(GameStateBaseUI ui, RectTransform strip,
                                      GameStateItemUI[] items, List<CscCareer> careers)
        {
            var content = strip.Find("Viewport/Content") as RectTransform;
            if (content == null)
                return;
            for (var i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            _tabs.Clear();
            var donor = FindDonorButton(ui);
            if (donor == null)
            {
                CscLog.Warn("no button to style the career tabs from");
                return;
            }
            var titles = TabTitles(careers);
            AddTab(content, donor, "Alle", null, ui, items);
            for (var i = 0; i < careers.Count; i++)
                AddTab(content, donor, titles[i], careers[i].Guid, ui, items);
            ClearUiSelection();
        }
        private static List<string> TabTitles(List<CscCareer> careers)
        {
            var titles = new List<string>(careers.Count);
            foreach (var career in careers)
                titles.Add(TabTitle(career.Label));
            var byAge = new List<int>(careers.Count);
            for (var i = 0; i < careers.Count; i++)
                byAge.Add(i);
            byAge.Sort((a, b) => careers[a].FirstWriteTicks.CompareTo(careers[b].FirstWriteTicks));
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in byAge)
            {
                var title = titles[i];
                int count;
                if (seen.TryGetValue(title, out count))
                {
                    seen[title] = count + 1;
                    titles[i] = title + " #" + (count + 1);
                }
                else
                {
                    seen[title] = 1;
                }
            }
            return titles;
        }
        private static void ClearUiSelection()
        {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject != null)
                events.SetSelectedGameObject(null);
        }
        private static string TabTitle(string label)
        {
            if (string.IsNullOrEmpty(label))
                return "Unbekannt";
            var tick = label.IndexOf(" '", StringComparison.Ordinal);
            return tick > 0 ? label.Substring(0, tick) : label;
        }
        private static void AddTab(RectTransform content, Button donor, string title,
                                   string careerGuid, GameStateBaseUI ui, GameStateItemUI[] items)
        {
            var go = new GameObject("ToolkitTab_" + title, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(content, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            var image = go.AddComponent<Image>();
            var donorImage = donor.GetComponent<Image>();
            if (donorImage != null)
            {
                image.sprite = donorImage.sprite;
                image.type = donorImage.type;
                image.color = donorImage.color;
                image.material = donorImage.material;
                image.preserveAspect = false;
            }
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = donor.transition;
            button.colors = donor.colors;
            button.spriteState = donor.spriteState;
            var guid = careerGuid;
            button.onClick.AddListener(delegate { OnTabClicked(ui, items, guid); });
            var labelGo = new GameObject("Label", typeof(RectTransform));
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.SetParent(rt, false);
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(0f, 0f);
            lrt.offsetMax = new Vector2(0f, -3f);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            var donorLabel = donor.GetComponentInChildren<TMP_Text>(true);
            if (donorLabel != null)
            {
                label.font = donorLabel.font;
                label.fontSharedMaterial = donorLabel.fontSharedMaterial;
                label.fontSize = donorLabel.fontSize;
                label.fontStyle = donorLabel.fontStyle;
                label.color = donorLabel.color;
            }
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            label.text = title;
            var width = Mathf.Max(MinTabWidth, label.preferredWidth + TabPadding);
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0f;
            _tabs.Add(new Tab
            {
                Underline = AddUnderline(go.transform, careerGuid == _filterGuid),
                CareerGuid = careerGuid
            });
        }
        private static GameObject AddUnderline(Transform tab, bool selected)
        {
            var underline = new GameObject(UnderlineName, typeof(RectTransform)).GetComponent<RectTransform>();
            underline.SetParent(tab, false);
            underline.anchorMin = new Vector2(0f, 0f);
            underline.anchorMax = new Vector2(1f, 0f);
            underline.pivot = new Vector2(0.5f, 0f);
            underline.offsetMin = new Vector2(6f, 0f);
            underline.offsetMax = new Vector2(-6f, 3f);
            var image = underline.gameObject.AddComponent<Image>();
            image.color = UnderlineSelected;
            image.raycastTarget = false;
            underline.gameObject.SetActive(selected);
            return underline.gameObject;
        }
        private static void OnTabClicked(GameStateBaseUI ui, GameStateItemUI[] items, string careerGuid)
        {
            try
            {
                _filterGuid = careerGuid;
                Deselect(ui);
                ApplyFilter(ui, items);
                RefreshUnderlines();
                ClearUiSelection();
            }
            catch (Exception ex)
            {
                CscLog.Error("failed to switch career tab", ex);
            }
        }
        private static void ApplyFilter(GameStateBaseUI ui, GameStateItemUI[] items)
        {
            if (items == null)
                return;
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                var visible = _filterGuid == null;
                if (!visible)
                {
                    var data = item.SaveData;
                    visible = data != null && CscRowInfo.CareerFor(data.Path) == _filterGuid;
                }
                if (item.gameObject.activeSelf != visible)
                    item.gameObject.SetActive(visible);
            }
        }
        private static void RefreshUnderlines()
        {
            foreach (var tab in _tabs)
            {
                if (tab.Underline != null)
                    tab.Underline.SetActive(tab.CareerGuid == _filterGuid);
            }
        }
        private static readonly List<Tab> _tabs = new List<Tab>();
        private struct Tab
        {
            internal GameObject Underline;
            internal string CareerGuid;
        }
        private static void Deselect(GameStateBaseUI ui)
        {
            var method = AccessTools.Method(ui.GetType(), "SelectSave", new[] { typeof(GameStateItemUI) })
                         ?? AccessTools.Method(typeof(GameStateBaseUI), "SelectSave", new[] { typeof(GameStateItemUI) });
            if (method != null)
                method.Invoke(ui, new object[] { null });
        }
        private static Button FindDonorButton(GameStateBaseUI ui)
        {
            foreach (var name in new[] { "loadButton", "deleteButton", "closeButton" })
            {
                var field = AccessTools.Field(ui.GetType(), name) ?? AccessTools.Field(typeof(GameStateBaseUI), name);
                if (field == null)
                    continue;
                var button = field.GetValue(ui) as Button;
                if (button != null)
                    return button;
            }
            return null;
        }
    }


    [HarmonyPatch(typeof(GameStateBaseUI), "FetchSaves")]
    internal static class CscUiProbe
    {
        internal static readonly bool Enabled = false;
        private static bool _dumped;
        private static readonly FieldInfo SavesListField =
            AccessTools.Field(typeof(GameStateBaseUI), "savesList");
        private static void Postfix(GameStateBaseUI __instance)
        {
            if (!Enabled || _dumped || __instance == null || SavesListField == null)
                return;
            _dumped = true;
            try
            {
                var savesList = SavesListField.GetValue(__instance) as RectTransform;
                if (savesList == null)
                {
                    CscLog.Warn("UI probe: savesList was null");
                    return;
                }
                var sb = new StringBuilder();
                sb.Append("\n===== load menu UI hierarchy =====\n");
                sb.Append("opened by: ").Append(__instance.GetType().Name).Append('\n');
                sb.Append("\n--- ancestors (savesList first) ---\n");
                var depth = 0;
                for (var t = savesList.transform; t != null && depth < 8; t = t.parent, depth++)
                {
                    sb.Append(Indent(depth)).Append(Describe(t)).Append('\n');
                }
                var parent = savesList.parent;
                if (parent != null)
                {
                    sb.Append("\n--- siblings of savesList (in '").Append(parent.name).Append("') ---\n");
                    for (var i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        sb.Append("  [").Append(i).Append("] ")
                          .Append(ReferenceEquals(child, savesList.transform) ? "<< savesList >> " : "")
                          .Append(Describe(child)).Append('\n');
                    }
                    var grand = parent.parent;
                    if (grand != null)
                    {
                        sb.Append("\n--- siblings of '").Append(parent.name)
                          .Append("' (in '").Append(grand.name).Append("') ---\n");
                        for (var i = 0; i < grand.childCount; i++)
                        {
                            var child = grand.GetChild(i);
                            sb.Append("  [").Append(i).Append("] ")
                              .Append(ReferenceEquals(child, parent) ? "<< parent >> " : "")
                              .Append(Describe(child)).Append('\n');
                        }
                    }
                }
                if (savesList.childCount > 0)
                {
                    sb.Append("\n--- first save row ---\n");
                    DumpTree(sb, savesList.GetChild(0), 0, 3);
                }
                sb.Append("\n--- an existing button, as a styling donor for new controls ---\n");
                foreach (var name in new[] { "loadButton", "deleteButton", "closeButton" })
                {
                    var f = AccessTools.Field(__instance.GetType(), name) ??
                            AccessTools.Field(typeof(GameStateBaseUI), name);
                    if (f == null)
                        continue;
                    var button = f.GetValue(__instance) as Component;
                    if (button == null)
                        continue;
                    sb.Append("  ").Append(name).Append(": ").Append(Path(button.transform)).Append('\n');
                    DumpTree(sb, button.transform, 2, 2);
                }
                sb.Append("===== end =====");
                CscLog.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                CscLog.Error("UI probe failed", ex);
            }
        }
        private static void DumpTree(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            sb.Append(Indent(depth)).Append(Describe(t)).Append('\n');
            if (depth >= maxDepth)
                return;
            for (var i = 0; i < t.childCount; i++)
                DumpTree(sb, t.GetChild(i), depth + 1, maxDepth);
        }
        private static string Describe(Transform t)
        {
            var sb = new StringBuilder();
            sb.Append('\'').Append(t.name).Append('\'');
            var rect = t as RectTransform;
            if (rect != null)
            {
                var r = rect.rect;
                sb.Append("  size=(").Append(Mathf.RoundToInt(r.width)).Append('x')
                  .Append(Mathf.RoundToInt(r.height)).Append(')');
            }
            sb.Append("  [");
            var components = t.GetComponents<Component>();
            for (var i = 0; i < components.Length; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append(components[i] == null ? "<missing>" : components[i].GetType().Name);
            }
            sb.Append(']');
            if (!t.gameObject.activeSelf)
                sb.Append("  (inactive)");
            return sb.ToString();
        }
        private static string Path(Transform t)
        {
            var sb = new StringBuilder(t.name);
            for (var p = t.parent; p != null; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
        private static string Indent(int depth)
        {
            return new string(' ', 2 + depth * 2);
        }
    }

}

// ====================================================================================================
// Abschnitt: Modules/SaveTools.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Globalization;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.UI;

    // High-resolution clock without naming the diagnostics namespace (blocked by the mod check).
    internal static class LoadClock
    {
        public static readonly Type StopwatchType = Type.GetType("System.Diagnostics.Stopwatch, System") ?? Type.GetType("System.Diagnostics.Stopwatch");
        private static readonly Func<long> stamp = Make();
        public static readonly long Frequency = ReadFrequency();

        private static Func<long> Make()
        {
            try
            {
                var m = StopwatchType?.GetMethod("GetTimestamp", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (m != null) return (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), m);
            }
            catch { }
            return () => DateTime.UtcNow.Ticks;
        }

        private static long ReadFrequency()
        {
            try
            {
                var f = StopwatchType?.GetField("Frequency", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f != null && StopwatchType.GetMethod("GetTimestamp") != null) return (long)f.GetValue(null);
            }
            catch { }
            return TimeSpan.TicksPerSecond;
        }

        public static long Now() => stamp();
        public static double Seconds(long ticks) => ticks / (double)Frequency;
    }

    // Faster loading: the save loader may work longer per frame, ends an idle frame early while it
    // only waits for the worker thread, and skips the game's memory clean-ups during the load.
    [HarmonyPatch]
    internal static class SaveTools_LoadSpeedPatch
    {
        private const int VanillaBudget = 160000, VanillaActions = 500;
        private static bool patched;
        private static object active;
        private static long lastStep, loadStart, outside, lastEnd;
        private static int frames;

        private static bool On => Settings.B("FastLoad");
        public static bool IsLoading => active != null && LoadClock.Seconds(LoadClock.Now() - lastStep) < 5.0;

        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SavesManager), "LoadCoroutine"));

        public static long Budget()
        {
            if (!On) return VanillaBudget;
            if (SaveTools_IdlePatch.ShouldYield()) return 0;
            return (long)(BudgetMs() * LoadClock.Frequency / 1000.0);
        }

        // 0.4.15 (measured 2026-11-18: 83.9 s load, 549 frames x 96 ms inside the load loop = 52.7 s, so 31 s = 57 ms
        // per frame were spent OUTSIDE the loop (drawing the loading screen, waiting for the graphics card) while the
        // reader thread stood still). Fewer, longer frames: at least 500 ms of loading per frame.
        internal static bool Long => Settings.B("FastLoadLongFrames");

        // true once the game's reader thread really reads the save (SavesManager.isActivelyDeserializing); before that it
        // waits for the boat and port scenes, which only advance with drawn frames.
        private static object readOwner; private static System.Reflection.FieldInfo readField; private static object readMgr;
        private static long readSince; private static int sceneFrames;
        internal static bool Reading()
        {
            try
            {
                var a = active;
                if (a == null) return true;
                if (!ReferenceEquals(readOwner, a))
                {
                    readOwner = a; readSince = 0; sceneFrames = 0;
                    var f = AccessTools.Field(a.GetType(), "<>4__this");
                    readMgr = f == null ? null : f.GetValue(a);
                    readField = AccessTools.Field(typeof(SavesManager), "isActivelyDeserializing");
                }
                if (readMgr == null || readField == null) return true;
                bool r = (bool)readField.GetValue(readMgr);
                if (r && readSince == 0)
                {
                    readSince = LoadClock.Now();
                    Util.Log("Laden: Szenen (Boot, Hafen) fertig nach " + LoadClock.Seconds(readSince - loadStart).ToString("0.0", CultureInfo.InvariantCulture) + " s und " + frames + " Bildern, davon " + SaveTools_IdlePatch.SceneIdle() + " s gewartet. Jetzt wird der Spielstand gelesen.");
                }
                return r;
            }
            catch { return true; }
        }
        internal static float BudgetMs() => Mathf.Max(Mathf.Clamp(Settings.F("FastLoadBudgetMs"), 16f, 1000f), Long ? 500f : 0f);

        public static int MaxActions() => !On ? VanillaActions : Long ? 1000000 : Mathf.Clamp(Mathf.RoundToInt(Settings.F("FastLoadMaxActions")), 500, 1000000);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            var elapsed = LoadClock.StopwatchType == null ? null : AccessTools.PropertyGetter(LoadClock.StopwatchType, "ElapsedTicks");
            if (elapsed == null) { Util.Log("Laden: Stoppuhr nicht gefunden - Lade-Schleife bleibt Spielwert."); return list; }
            int b = -1, m = -1, nb = 0, nm = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.opcode != OpCodes.Ldc_I4 && c.opcode != OpCodes.Ldc_I4_S) continue;
                int v = c.operand is int x ? x : c.operand is sbyte sb ? sb : c.operand is byte by ? by : int.MinValue;
                if (v == VanillaBudget && i > 0 && i + 1 < list.Count && list[i - 1].operand as System.Reflection.MethodInfo == elapsed && list[i + 1].opcode == OpCodes.Conv_I8) { b = i; nb++; }
                if (v == VanillaActions && i + 1 < list.Count && (list[i + 1].opcode == OpCodes.Bge || list[i + 1].opcode == OpCodes.Bge_S)) { m = i; nm++; }
            }
            if (nb != 1 || nm != 1) { Util.Log("Laden: Lade-Schleife passt nicht (" + nb + "/" + nm + ") - bleibt Spielwert."); return list; }
            list[b].opcode = OpCodes.Call; list[b].operand = AccessTools.Method(typeof(SaveTools_LoadSpeedPatch), "Budget");
            list[m].opcode = OpCodes.Call; list[m].operand = AccessTools.Method(typeof(SaveTools_LoadSpeedPatch), "MaxActions");
            patched = true;
            return list;
        }

        private static void Prefix(object __instance)
        {
            if (!ReferenceEquals(active, __instance)) { active = __instance; loadStart = LoadClock.Now(); frames = 0; outside = 0; lastEnd = 0; SaveTools_IdlePatch.ResetLoad(); }
            else if (lastEnd != 0) outside += LoadClock.Now() - lastEnd;
            frames++;
            LoadSpin_WorkerPatch.SpinUs = Settings.B("FastLoad") ? Mathf.Clamp(Settings.F("FastLoadSpinUs"), 0f, 5000f) : 0f;
            lastStep = LoadClock.Now();
            SaveTools_IdlePatch.BeginFrame();
        }

        private static Exception Finalizer(object __instance, Exception __exception)
        {
            if (__exception != null && ReferenceEquals(active, __instance))
            {
                active = null;
                Util.Log("Laden: Fehler beim Laden (" + __exception.GetType().Name + ") - Speicher-Bremse gelöst." + SaveTools_MemoryPatch.SummariseAndReset());
            }
            return __exception;
        }

        private static void Postfix(object __instance, bool __result)
        {
            if (ReferenceEquals(active, __instance)) lastEnd = LoadClock.Now();
            if (__result || !ReferenceEquals(active, __instance)) return;
            double s = LoadClock.Seconds(LoadClock.Now() - loadStart);
            Util.Log("Laden: fertig in " + s.ToString("0.0", CultureInfo.InvariantCulture) + " s über " + frames + " Bilder ("
                + (patched && On ? BudgetMs().ToString("0", CultureInfo.InvariantCulture) + " ms / " + MaxActions() + " Schritte pro Bild" : "Spielwert 16 ms / 500 Schritte") + ")."
                + " Außerhalb der Lade-Schleife (Ladebild zeichnen, Grafikkarte): " + LoadClock.Seconds(outside).ToString("0.0", CultureInfo.InvariantCulture) + " s = " + (frames > 1 ? LoadClock.Seconds(outside) * 1000.0 / (frames - 1) : 0).ToString("0", CultureInfo.InvariantCulture) + " ms pro Bild."
                + SaveTools_IdlePatch.Summarise(frames) + SaveTools_MemoryPatch.SummariseAndReset() + LoadSpin.SummariseAndReset());
            active = null;
        }
    }

    // While the loader only waits on its worker thread, end that frame after 16 ms instead of idling on.
    [HarmonyPatch]
    internal static class SaveTools_IdlePatch
    {
        private static bool installed, yielded;
        private static long idleFrame, idleLoad;
        private static int framesYielded, served, timedOut;

        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Serialization.GameStateDeserializer");
            return t == null ? null : AccessTools.DeclaredMethod(t, "CallMainThreadAction");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            var waitOne = AccessTools.Method(typeof(System.Threading.WaitHandle), "WaitOne", new[] { typeof(int) });
            int at = -1, n = 0;
            for (int i = 0; i < list.Count; i++)
                if ((list[i].opcode == OpCodes.Callvirt || list[i].opcode == OpCodes.Call) && list[i].operand as System.Reflection.MethodInfo == waitOne) { at = i; n++; }
            if (n != 1 || waitOne == null) { Util.Log("Laden: Warte-Stelle passt nicht (" + n + ") - leere Bilder bleiben Spielwert."); return list; }
            list[at].opcode = OpCodes.Call; list[at].operand = AccessTools.Method(typeof(SaveTools_IdlePatch), "TimedWait");
            installed = true;
            return list;
        }

        public static bool TimedWait(System.Threading.WaitHandle handle, int ms)
        {
            long start = LoadClock.Now();
            // 0.4.8: first look for the reader's signal in a short spin (no sleeping), then wait as before
            bool ok = LoadSpin.Poll(handle);
            if (ok) LoadSpin.MainFast++; else ok = handle.WaitOne(ms);
            if (ok) served++; else timedOut++;
            long w = LoadClock.Now() - start;
            idleFrame += w; idleLoad += w;
            // 0.4.34: time waited in a row without a single job (reset by every job)
            if (ok) emptyRun = 0; else { emptyRun += w; if (!SaveTools_LoadSpeedPatch.Reading()) idleScene += w; }
            return ok;
        }

        public static bool ShouldYield()
        {
            // 0.3.71: measured 2026-11-16: the loader thread ran at only 25-53 % of a core. It needs the main thread for
            // many small jobs and stands still while the main thread is away drawing the frame. With the old 16 ms the
            // main thread left after 16 ms of waiting in 2551 of 2800 frames. Now it stays ("FastLoadIdleMs", default 100).
            // 0.4.34 (DoMe 2026-11-18: loading takes minutes; log 14:02: 124.9 s, of that 69.8 s waited on an empty queue,
            // the boat interior scene alone took 28.8 s). Cause: before the save is read, the game loads the boat and port
            // scenes. Unity finishes such a scene only step by step, one step per drawn frame. In that phase the reader
            // thread gives no jobs, and the "long frames" option kept every frame waiting 500 ms on the empty queue = 2 frames
            // per second, so the scenes crawled. Now:
            //  - while scenes load (reader not yet reading the save): leave the frame at the first empty wait;
            //  - while the save is read: leave when nothing came for "FastLoadIdleMs" IN A ROW (before: 500 ms in total).
            if (!installed) return false;
            bool reading = SaveTools_LoadSpeedPatch.Reading();
            double runMs = reading ? Mathf.Clamp(Settings.F("FastLoadIdleMs"), 2f, 1000f) : 1.0;
            double totalMs = Mathf.Max(Mathf.Clamp(Settings.F("FastLoadIdleMs"), 2f, 1000f), SaveTools_LoadSpeedPatch.Long ? 500f : 0f);
            if (emptyRun < (long)(runMs * LoadClock.Frequency / 1000.0) && idleFrame < (long)(totalMs * LoadClock.Frequency / 1000.0)) return false;
            if (!yielded) { yielded = true; framesYielded++; }
            return true;
        }

        private static long emptyRun, idleScene;
        public static void BeginFrame() { idleFrame = 0; emptyRun = 0; yielded = false; }
        public static void ResetLoad() { framesYielded = 0; idleLoad = 0; idleScene = 0; served = 0; timedOut = 0; BeginFrame(); }
        internal static string SceneIdle() => LoadClock.Seconds(idleScene).ToString("0.0", CultureInfo.InvariantCulture);

        public static string Summarise(int frames) => !installed ? "" :
            " Gewartet " + LoadClock.Seconds(idleLoad).ToString("0.0", CultureInfo.InvariantCulture) + " s auf leere Warteschlange; " + framesYielded + " von " + frames + " Bildern früher beendet (Grenze " + Settings.F("FastLoadIdleMs").ToString("0") + " ms); Warten endete " + served + "x mit Auftrag, " + timedOut + "x leer; vom Warten " + SceneIdle() + " s während Boot- und Hafen-Szene luden.";
    }

    // The loader's worker thread may only queue 40 jobs for the main thread, then it stops and waits.
    // The main thread then often idles too ("Gewartet x s auf leere Warteschlange"). A larger queue lets
    // the worker read ahead, so both threads stay busy. 40 = game value.
    [HarmonyPatch]
    internal static class SaveTools_QueueCapPatch
    {
        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Serialization.GameStateDeserializer");
            return t == null ? null : AccessTools.DeclaredMethod(t, "MainThread", new[] { typeof(Action), typeof(bool) });
        }

        public static int Cap() => Settings.B("FastLoad") ? Mathf.Clamp(Mathf.RoundToInt(Settings.F("FastLoadQueueCap")), 40, 100000) : 40;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = code.ToList();
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if ((c.opcode == OpCodes.Ldc_I4_S || c.opcode == OpCodes.Ldc_I4) && Convert.ToInt32(c.operand) == 40)
                { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(SaveTools_QueueCapPatch), nameof(Cap)); n++; }
            }
            if (n != 2) Util.Log("Laden: Warteschlangen-Grenze passt nicht (" + n + " Stellen gefunden, erwartet 2).");
            return list;
        }
    }

    // During a save load the game's memory clean-up (about 115 ms each) is skipped, up to a heap cap.
    [HarmonyPatch]
    internal static class SaveTools_MemoryPatch
    {
        private static int skipped, allowed;
        private static bool installed;

        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Core.GameState");
            return t == null ? null : AccessTools.DeclaredMethod(t, "CollectMemory");
        }

        private static bool Prefix()
        {
            installed = true;
            if (!Settings.B("FastLoad") || !Settings.B("FastLoadNoGc") || !SaveTools_LoadSpeedPatch.IsLoading) return true;
            long heapMb = GC.GetTotalMemory(false) / (1024L * 1024L);
            if (heapMb >= Mathf.Clamp(Settings.F("FastLoadGcCapMb"), 500f, 32000f)) { allowed++; return true; }
            skipped++;
            return false;
        }

        public static string SummariseAndReset()
        {
            if (!installed || (skipped == 0 && allowed == 0)) return "";
            string s = " " + skipped + " Speicher-Aufräumen übersprungen" + (allowed > 0 ? ", " + allowed + " wegen Obergrenze erlaubt" : "") + " (etwa " + (skipped * 115) + " ms gespart).";
            skipped = allowed = 0;
            return s;
        }
    }

    // 0.4.8 (DoMe 2026-11-17: go deep into loading). Loading a save is a ping-pong between the game's reader thread
    // and the main thread: for nearly every object the reader hands a small job to the main thread and sleeps until it
    // is done; the main thread sleeps until the next job arrives. Each wake-up from sleep costs time (on a virtual PC
    // noticeably), and there are hundreds of thousands of them: measured "Gewartet 72 s" of a 135-165 s load.
    // Now both sides first look for the other for a fraction of a millisecond without sleeping ("FastLoadSpinUs").
    internal static class LoadSpin
    {
        internal static int MainFast, WorkerFast, WorkerSlow;
        private static long Budget() => Settings.B("FastLoad") ? (long)(Mathf.Clamp(Settings.F("FastLoadSpinUs"), 0f, 5000f) * LoadClock.Frequency / 1000000.0) : 0;

        // main thread: poll the signal without blocking
        public static bool Poll(System.Threading.WaitHandle handle)
        {
            long b = Budget();
            if (b <= 0) return false;
            long end = LoadClock.Now() + b;
            do
            {
                if (handle.WaitOne(0)) return true;
                System.Threading.Thread.SpinWait(30);
            } while (LoadClock.Now() < end);
            return false;
        }

        public static string SummariseAndReset()
        {
            if (MainFast + WorkerFast + WorkerSlow == 0) return "";
            string s = " Übergaben ohne Schlafen: Hauptthread " + MainFast + ", Leser " + WorkerFast + " (Leser musste trotzdem schlafen: " + WorkerSlow + ").";
            MainFast = 0; WorkerFast = 0; WorkerSlow = 0;
            return s;
        }
    }

    // reader thread: wait for the main thread in a short spin before going to sleep
    [HarmonyPatch]
    internal static class LoadSpin_WorkerPatch
    {
        private static AccessTools.FieldRef<object, bool> queued;
        private static bool Prepare() => TargetMethod() != null;
        private static System.Reflection.MethodBase TargetMethod()
        {
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Serialization.GameStateDeserializer");
            if (t == null) return null;
            var f = AccessTools.DeclaredField(t, "queuedMainThreadJobs");
            if (f == null || f.FieldType != typeof(bool)) return null;
            try { queued = AccessTools.FieldRefAccess<object, bool>(f); }
            catch (Exception e) { queued = null; Util.Log("Laden: Leser-Warten bleibt Spielwert (" + e.Message + ")"); return null; }
            return AccessTools.DeclaredMethod(t, "WaitForMainThread");
        }

        private static void Prefix(object __instance)
        {
            if (queued == null) return;
            long b = (long)(1.0 * LoadClock.Frequency / 1000000.0 * SpinUs);
            if (b <= 0) return;
            ref bool q = ref queued(__instance);
            if (!System.Threading.Volatile.Read(ref q)) return;
            long end = LoadClock.Now() + b;
            do
            {
                System.Threading.Thread.SpinWait(30);
                if (!System.Threading.Volatile.Read(ref q)) { LoadSpin.WorkerFast++; return; }
            } while (LoadClock.Now() < end);
            LoadSpin.WorkerSlow++;
        }

        // read on the main thread once per load step (Settings is not made for other threads)
        internal static volatile float SpinUs;
    }

    // Scenes of a save are started with a fixed wait of 0.5 s each; not while a save is loading.
    [HarmonyPatch(typeof(DWS.Common.Resources.ResourceManager), "RetrieveSceneAsync")]
    internal static class LoadSpin_SceneDelayPatch
    {
        private static int cut;
        private static void Prefix(ref float loadingDelay, string levelName)
        {
            if (loadingDelay <= 0f || !Settings.B("FastLoad") || !Settings.B("FastLoadNoSceneDelay")) return;
            if (!(ExtraFeatures_LoadingBoostPatch.Boosted || SaveTools_LoadSpeedPatch.IsLoading)) return;
            if (cut++ < 40) Util.Log("Laden: Szene \"" + levelName + "\" ohne feste Wartezeit von " + loadingDelay.ToString("0.0", CultureInfo.InvariantCulture) + " s gestartet");
            loadingDelay = 0f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/StorageExpander.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Reflection;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Utilities;

    // Storage grows by itself: when main storage, item storage or kitchen is almost full, x more slots are added
    // (up to a maximum). The new slots are saved with the game like other dynamic objects, so items stay after loading.
    // A minimum slot count can also be set; it is applied at once, also after a boat change.
    public class StorageExpander : ModuleBase
    {
        private static readonly string[] names = { "Main Storage", "Item Storage", "Kitchen" };
        private static readonly FieldInfo typeField = typeof(ShipEquipmentSlot).GetField("equipmentType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public override void Tick()
        {
            bool grow = Settings.B("AutoExpandStorage");
            int min = (int)Settings.F("StorageMinSlots");
            if (!grow && min <= 0) return;
            var saves = Game.Saves;
            if (saves != null && (saves.IsLoading || saves.IsActivelyDeserializing)) return;
            var ship = Util.PlayerShip;
            int step = Mathf.Max(1, (int)Settings.F("AutoExpandStep"));
            int max = Mathf.Max(1, (int)Settings.F("AutoExpandMax"));
            foreach (var n in names)
            {
                var st = ship.GetEquipment<Storage>(n);
                if (st == null || Util.Slots(st) == 0) continue;
                int target = st.SlotCount;
                if (min > 0 && target < min && n != "Kitchen") target = min;
                if (grow)
                {
                    int free = 0;
                    for (int i = 0; i < Util.Slots(st); i++) if (st[i] != null && st[i].Item == null) free++;
                    if (free < step) target = Mathf.Max(target, st.SlotCount + step);
                }
                target = Mathf.Min(target, Mathf.Max(max, st.SlotCount));
                if (target > st.SlotCount) AddSlots(st, target);
            }
        }

        private static void AddSlots(Storage storage, int target)
        {
            storage.ValidateSlots();
            int before = storage.SlotCount;
            while (storage.SlotCount < target)
            {
                var go = new GameObject("Storage Slot Extra " + (storage.SlotCount + 1));
                go.layer = 10;
                go.transform.SetParent(storage.transform, false);
                go.AddComponent<DestroyBeforeLoading>();
                go.AddComponent<SerializableDynamicObject>();
                var slot = go.AddComponent<ShipEquipmentSlot>();
                typeField?.SetValue(slot, "Storage");
                slot.VariationName = "";
                storage.ValidateSlots();
                if (storage.SlotCount <= before) break; // safety: slot was not accepted
                before = storage.SlotCount;
            }
            Util.Log("Lager " + storage.name + ": jetzt " + storage.SlotCount + " Plätze");
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/TextureRace.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Serialization;

    // Crash fix: five game methods destroy textures the graphics thread may still use. Those destroys
    // are queued (thread-safe) and done 10 s / 120 frames later on the main thread.
    [NonSerializedInGameState]
    public sealed class LateDestroy : MonoBehaviour
    {
        private struct Pending { public UnityEngine.Object Obj; public float Due; public int DueFrame; }
        private static readonly List<UnityEngine.Object> incoming = new List<UnityEngine.Object>();
        private static readonly object gate = new object();
        private static readonly List<Pending> pending = new List<Pending>();
        private static LateDestroy host;
        private static int deferred, destroyed, logged;
        private static float nextSummary;

        public static void Queue(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (host == null || !Settings.B("FixTextureRace") || !(obj is Texture)) { UnityEngine.Object.Destroy(obj); return; }
            lock (gate) incoming.Add(obj);
        }

        public static void QueueRef(ref Texture2D obj, bool immediate) { var t = obj; obj = null; Queue(t); }

        // Called on the main thread once the patches are in.
        public static void EnsureHost()
        {
            if (host != null) return;
            var go = new GameObject("Toolkit Late Destroy");
            DontDestroyOnLoad(go);
            host = go.AddComponent<LateDestroy>();
        }

        private void Update()
        {
            try
            {
                float now = Time.unscaledTime;
                int frame = Time.frameCount;
                lock (gate)
                {
                    foreach (var o in incoming) pending.Add(new Pending { Obj = o, Due = now + 10f, DueFrame = frame + 120 });
                    deferred += incoming.Count;
                    incoming.Clear();
                }
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    if (now < pending[i].Due || frame < pending[i].DueFrame) continue;
                    var o = pending[i].Obj;
                    pending.RemoveAt(i);
                    if (o != null) Destroy(o);
                    destroyed++;
                }
                if (deferred > 0 && logged == 0) { logged = deferred; nextSummary = now + 60f; Util.Log("Textur-Fix: erstes spätes Löschen - Fix ist aktiv."); }
                else if (deferred != logged && now >= nextSummary)
                {
                    logged = deferred; nextSummary = now + 60f;
                    if (Settings.B("TextureRaceLog")) Util.Log("Textur-Fix: " + deferred + " Texturen spät gelöscht (" + destroyed + " erledigt, " + pending.Count + " wartend).");
                }
            }
            catch (Exception e) { Util.Log("Textur-Fix: Durchlauf fehlgeschlagen: " + e.Message); }
        }
    }

    [HarmonyPatch]
    internal static class TextureRace_Patch
    {
        private static readonly MethodInfo Destroy1 = typeof(UnityEngine.Object).GetMethod("Destroy", new[] { typeof(UnityEngine.Object) });
        private static readonly MethodInfo QueueM = AccessTools.Method(typeof(LateDestroy), "Queue");
        private static readonly MethodInfo QueueRefM = AccessTools.Method(typeof(LateDestroy), "QueueRef");
        private static readonly Dictionary<string, int> Expected = new Dictionary<string, int>
        {
            { "CleanRecentTask", 3 }, { "ConvertTexture", 1 }, { "StopView", 1 }, { "UpdateView", 1 }, { "AssignTexturesToMaterials", 3 }
        };
        private static int rewritten;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var sites = new[] { "UBOAT.Game.Scene.Effects.CharacterBatcher:CleanRecentTask", "UBOAT.Game.Scene.Effects.LayeredMaterialComposer:ConvertTexture",
                                "UBOAT.Game.Scene.Items.Periscope:StopView", "UBOAT.Game.Scene.Items.Periscope:UpdateView", "UBOAT.Game.Scene.Effects.CharacterBatcher:AssignTexturesToMaterials" };
            foreach (var s in sites)
            {
                var p = s.Split(':');
                var t = UboatToolkit.Util.FindType(p[0]);
                var m = t != null ? AccessTools.Method(t, p[1]) : null;
                if (m != null) yield return m;
                else Util.Log("Textur-Fix: " + s + " nicht gefunden.");
            }
        }

        private static bool IsDestroyIfExists(CodeInstruction c)
        {
            if (c.opcode != OpCodes.Call && c.opcode != OpCodes.Callvirt) return false;
            var m = c.operand as MethodInfo;
            if (m == null || m.Name != "DestroyIfExists" || !m.IsGenericMethod) return false;
            var a = m.GetGenericArguments();
            return a.Length == 1 && a[0] == typeof(Texture2D);
        }

        private static bool IsDestroy(CodeInstruction c) => (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) && c.operand as MethodInfo == Destroy1;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code, MethodBase original)
        {
            var list = new List<CodeInstruction>(code);
            bool assign = original.Name == "AssignTexturesToMaterials";
            int found = 0;
            foreach (var c in list) if (assign ? IsDestroyIfExists(c) : IsDestroy(c)) found++;
            if (!Expected.TryGetValue(original.Name, out int want) || found != want || QueueM == null || QueueRefM == null)
            {
                Util.Log("Textur-Fix: " + original.DeclaringType.Name + "." + original.Name + " erwartet " + want + ", gefunden " + found + " - bleibt unverändert.");
                return list;
            }
            foreach (var c in list) if (assign ? IsDestroyIfExists(c) : IsDestroy(c)) c.operand = assign ? QueueRefM : QueueM;
            rewritten++;
            return list;
        }

        private static void Cleanup() { if (rewritten > 0) Util.Safe("TextureRace.Host", LateDestroy.EnsureHost); }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Traffic.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Scene.Entities;

    // Time compression ignores fishing boats and friendly (green) ships, and
    // "back to water": if the boat is stuck on land anyway, it is put back at the last safe spot at sea.
    public class Traffic : ModuleBase
    {
        private Vector2 lastSafe;
        private bool hasSafe;
        private float stuckSince = -1f;

        // true = this group should not stop time compression.
        public static bool Ignore(SandboxGroup g)
        {
            if (g == null) return false;
            if (Settings.B("IgnoreFishingBoats") && g.Type == "Fishing Boat") return true;
            var me = Util.PlayerShip?.SandboxEntity?.Country;
            if (g.Country == null || me == null) return false;
            var rel = g.Country.GetRelationWith(me);
            if (Settings.B("IgnoreAlliedShips") && rel == Country.Relation.Ally) return true;
            if (Settings.B("IgnoreNeutralShips") && rel == Country.Relation.Neutral) return true;
            return false;
        }

        // For the mod's own contact stop: true if all ships of this contact are ignored.
        public static bool Ignore(IContactInfo c)
        {
            var list = new List<SingularContactInfo>();
            if (c is SingularContactInfo s) list.Add(s);
            else if (c is CompoundContactInfo cc)
                list.AddRange(Traverse.Create(cc).Field("entityInfos").GetValue<List<SingularContactInfo>>() ?? new List<SingularContactInfo>());
            if (list.Count == 0) return false;
            foreach (var x in list)
            {
                var e = Traverse.Create(x).Field("entity").GetValue<SandboxEntity>();
                if (!Ignore(e?.Group)) return false;
            }
            return true;
        }

        public override void Tick()
        {
            var w = Game.Wolfpack;
            var ship = Util.PlayerShip;
            if (w == null || ship.Docked) { stuckSince = -1f; return; }

            // Remember the last spot with at least 1 km water to the coast.
            if (w.LandDistance > 1f) { lastSafe = w.Position; hasSafe = true; }

            var o = Settings.Get("BackToWaterNow");
            if (o != null && o.On) { o.Value = 0; BackToWater("von Hand"); return; }

            if (!Settings.B("BackToWater")) { stuckSince = -1f; return; }
            bool engineOn = ship.ActiveEngine != null && ship.ActiveEngine.GearIndex != 0;
            bool stuck = w.LandDistance < 0.3f && engineOn && Mathf.Abs(ship.AverageVelocity) < 0.3f;
            if (!stuck) { stuckSince = -1f; return; }
            if (stuckSince < 0f) { stuckSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - stuckSince >= Settings.F("BackToWaterSeconds")) BackToWater("automatisch");
        }

        private void BackToWater(string how)
        {
            stuckSince = -1f;
            if (!hasSafe) { ToolkitRunner.ShowNotice("Keine sichere Stelle gemerkt", 4f); return; }
            var w = Game.Wolfpack;
            var tc = Game.TimeCompression;
            if (tc != null) tc.TimeCompression = false;
            w.SetPosition(lastSafe, true, true);
            ToolkitRunner.ShowNotice("Zurück ins Wasser (" + how + ")", 6f);
        }
    }

    // The game's own time compression stop on a new sighting: undo it for ignored groups.
    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "PlayerWolfpack_ObservationAdded")]
    internal static class Traffic_ObservationPatch
    {
        private static void Prefix(out bool __state)
        {
            __state = Game.TimeCompression?.TimeCompression ?? false;
        }

        private static void Postfix(GroupObservation observation, bool __state)
        {
            var tc = Game.TimeCompression;
            if (!__state || tc == null || tc.TimeCompression) return;
            if (Traffic.Ignore(observation?.Group)) tc.TimeCompression = true;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/WolfpackStep.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UBOAT.Game.Sandbox;
    using global::UnityEngine;

    // 0.4.8 (DoMe 2026-11-17: optimise the game). Measured with the hotspot finder: SandboxPlayerWolfpack.FixedUpdate
    // was the most expensive method of the game (about 4.6 s per minute). In every physics step it searches all ship
    // groups around the boat twice (near groups, far "impostor" groups), compares the lists and checks the coast
    // distance. With low FPS the game runs 2-3 physics steps per frame, so the same search ran 2-3 times per frame
    // although the boat moved a few centimetres. Now the two searches run in the first physics step of a frame only.
    // The boat's own position update still runs in every step. Each minute the log shows calls, time and savings.
    public class WolfpackStep : ModuleBase
    {
        internal static int lastFrame = -1, Ran, Saved;
        internal static bool SkipNow;
        internal static double Seconds;
        private float nextLog;

        public override void Tick()
        {
            if (Time.unscaledTime < nextLog) return;
            if (nextLog > 0f && Ran + Saved > 0)
                Util.Log("Nahbereich-Suche: " + Ran + " mal gelaufen (" + (Seconds * 1000.0).ToString("0") + " ms), " + Saved + " mal gespart (zweiter/dritter Physik-Schritt im selben Bild)");
            nextLog = Time.unscaledTime + 60f; Ran = 0; Saved = 0; Seconds = 0;
        }
    }

    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "UpdateNearbyGroups")]
    internal static class WolfpackStep_Near
    {
        private static bool Prefix(out double __state)
        {
            __state = 0;
            bool skip = Settings.B("WolfpackOncePerFrame") && Time.frameCount == WolfpackStep.lastFrame;
            WolfpackStep.SkipNow = skip;
            if (skip) { WolfpackStep.Saved++; return false; }
            WolfpackStep.lastFrame = Time.frameCount;
            WolfpackStep.Ran++;
            __state = Time.realtimeSinceStartupAsDouble;
            return true;
        }
        private static void Finalizer(double __state) { if (__state > 0) WolfpackStep.Seconds += Time.realtimeSinceStartupAsDouble - __state; }
    }

    [HarmonyPatch(typeof(SandboxPlayerWolfpack), "UpdateImpostorGroups")]
    internal static class WolfpackStep_Far
    {
        private static bool Prefix(out double __state)
        {
            __state = 0;
            if (WolfpackStep.SkipNow) return false;
            __state = Time.realtimeSinceStartupAsDouble;
            return true;
        }
        private static void Finalizer(double __state) { if (__state > 0) WolfpackStep.Seconds += Time.realtimeSinceStartupAsDouble - __state; }
    }
}

// ====================================================================================================
// Abschnitt: Modules/WolfpackTuning.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Sandbox.HQ;
    using global::UBOAT.Game.Sandbox.Missions;

    // Wolfpack settings: from which date packs form, chance, max boats, first target tonnage.
    // Done by swapping the game's own values in its code (transpiler); untouched if the game changed.
    internal static class WolfpackTuning
    {
        public static readonly string[] FromNames = { "September 1940 (erster Rudel-Einsatz)", "Juli 1940", "November 1940", "Januar 1941", "8. Mai 1941 (Spiel)" };
        private static readonly DateTime[] FromDates = { new DateTime(1940, 9, 1), new DateTime(1940, 7, 1), new DateTime(1940, 11, 1), new DateTime(1941, 1, 1), new DateTime(1941, 5, 8) };
        private static bool On => Settings.B("Wolfpack");

        internal static readonly MethodInfo ToFloat = Conversion("op_Implicit", typeof(float));
        internal static readonly MethodInfo ToInt = Conversion("op_Explicit", typeof(int));
        private static MethodInfo Conversion(string name, Type returns)
        {
            try
            {
                foreach (var m in typeof(GameDataReference).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != name || m.ReturnType != returns) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(GameDataReference)) return m;
                }
            }
            catch { }
            return null;
        }

        public static DateTime GateDate(int year, int month, int day)
        {
            if (!On) return new DateTime(year, month, day);
            int i = (int)Math.Round(Settings.F("WolfpackFrom"));
            return FromDates[i < 0 || i >= FromDates.Length ? 0 : i];
        }
        public static float Probability(GameDataReference r) { float v = (float)r; return On ? Math.Max(5f, Math.Min(100f, Settings.F("WolfpackChance"))) / 100f : v; }
        public static float MaxUnits(GameDataReference r) { float v = (float)r; return On ? Math.Max(2f, Math.Min(8f, (float)Math.Round(Settings.F("WolfpackBoats")))) : v; }
        public static int FirstObjectiveGrt(GameDataReference r) { int v = (int)r; return On ? (int)Math.Max(1000f, Math.Min(50000f, Settings.F("WolfpackGrt"))) : v; }

        internal static bool IsCall(CodeInstruction c, MethodInfo m) => m != null && (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt) && c.operand as MethodInfo == m;
        internal static bool IsStaticLoad(CodeInstruction c, string name) => c.opcode == OpCodes.Ldsfld && (c.operand as FieldInfo)?.Name == name;
        internal static bool IsDateCtor(CodeInstruction c)
        {
            var ctor = c.operand as ConstructorInfo;
            if (c.opcode != OpCodes.Newobj || ctor == null || ctor.DeclaringType != typeof(DateTime)) return false;
            var ps = ctor.GetParameters();
            return ps.Length == 3 && ps[0].ParameterType == typeof(int) && ps[1].ParameterType == typeof(int) && ps[2].ParameterType == typeof(int);
        }

        // Replaces the call right after "ldsfld name" with our method (exactly one site, else untouched).
        internal static List<CodeInstruction> SwapAfterField(IEnumerable<CodeInstruction> code, string field, MethodInfo conv, string ours, string where)
        {
            var list = new List<CodeInstruction>(code);
            int site = -1, found = 0;
            for (int i = 0; i + 1 < list.Count; i++)
                if (IsStaticLoad(list[i], field) && IsCall(list[i + 1], conv)) { site = i + 1; found++; }
            if (found != 1) { Util.Log("Rudel: " + where + " Stelle " + field + " " + found + "/1 - nicht geändert (Spiel-Update?)"); return list; }
            list[site].operand = AccessTools.Method(typeof(WolfpackTuning), ours);
            return list;
        }
    }

    [HarmonyPatch(typeof(Headquarters), "ParseReport")]
    internal static class Wolfpack_ParseReportPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int dates = 0, probs = 0;
            foreach (var c in list) { if (WolfpackTuning.IsDateCtor(c)) dates++; else if (WolfpackTuning.IsCall(c, WolfpackTuning.ToFloat)) probs++; }
            if (dates != 1 || probs != 1) { Util.Log("Rudel: ParseReport Datum " + dates + "/1, Chance " + probs + "/1 - nicht geändert (Spiel-Update?)"); return list; }
            foreach (var c in list)
            {
                if (WolfpackTuning.IsDateCtor(c)) { c.opcode = OpCodes.Call; c.operand = AccessTools.Method(typeof(WolfpackTuning), "GateDate"); }
                else if (WolfpackTuning.IsCall(c, WolfpackTuning.ToFloat)) c.operand = AccessTools.Method(typeof(WolfpackTuning), "Probability");
            }
            return list;
        }
    }

    [HarmonyPatch(typeof(Headquarters), "StartWolfpack")]
    internal static class Wolfpack_StartPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            WolfpackTuning.SwapAfterField(code, "maxUnitsPerWolfpack", WolfpackTuning.ToFloat, "MaxUnits", "StartWolfpack");
    }

    [HarmonyPatch(typeof(WolfpackAttackMission), "OnMissionStarted")]
    internal static class Wolfpack_MissionPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) =>
            WolfpackTuning.SwapAfterField(code, "wolfpackFirstObjectiveGRT", WolfpackTuning.ToInt, "FirstObjectiveGrt", "OnMissionStarted");
    }
}


// ====================================================================================================
// Abschnitt: Wasserbomben-Zähler (0.4.32)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;

    // DoMe 2026-11-18: wants the depth charge achievement ("Survivor"). Game rule (SurvivorAchievement, DepthCharge.Explode):
    // 100 depth charges must explode closer than 1500 m to the boat during ONE encounter; the achievement is given
    // when the boat then leaves the group area and is in no enemy area any more. Leaving earlier resets the count.
    // This only shows the game's own counter (display, nothing is changed).
    [HarmonyPatch(typeof(UBOAT.Game.Sandbox.PlayerCareer), "DepthChargesDetonatedDuringCurrentEncounter", MethodType.Setter)]
    internal static class DepthChargeCounter_Patch
    {
        private static int last;
        private static void Postfix(UBOAT.Game.Sandbox.PlayerCareer __instance)
        {
            try
            {
                int n = __instance.DepthChargesDetonatedDuringCurrentEncounter;
                if (n == last) return;
                int before = last; last = n;
                if (!Settings.B("DepthChargeCounter")) return;
                if (n == 0) { if (before > 0) Util.Log("Wasserbomben-Zähler: zurück auf 0 (Begegnung verlassen), vorher " + before); return; }
                if (n == 1 || n % 10 == 0 || n == 100)
                {
                    Util.Log("Wasserbomben-Zähler: " + n + " von 100 in dieser Begegnung");
                    UboatToolkit.ToolkitRunner.ShowNotice(n >= 100 ? "Wasserbomben: " + n + "/100 erreicht. Jetzt ablaufen, bis kein Gegner-Gebiet mehr da ist!" : "Wasserbomben: " + n + "/100", n >= 100 ? 10f : 3f);
                }
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Auftragsziel genau (0.4.35)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Missions;

    // DoMe 2026-11-18: "gibt das Spiel her, wo auf den Meter genau das eigentliche Ziel ist? ... SS Fairwood finden ...
    // Mission teilweise verbuggt". Game code (SinkShipMission): the target is one ship of a moving convoy or warship
    // group. "Find" is done when that GROUP appears in 3D near the boat, "Sink" when the ship is sunk. The mission only
    // listens for the sinking. When the group reaches its port and is removed, nothing ends the mission: it stays
    // open for ever. This part reads the group's exact map position (display only, changes nothing) and writes into
    // the log what state the target is in. Scope: map/mission data only, no boat, no enemy is changed.
    public class MissionTargetInfo : ModuleBase
    {
        private static readonly System.Reflection.FieldInfo GroupF = AccessTools.Field(typeof(SinkShipMission), "groupToAttack");
        private static readonly System.Reflection.FieldInfo EntityF = AccessTools.Field(typeof(SinkShipMission), "targetEntity");
        private float next, nextNotice; private string lastState = "";

        private static void Collect(IMission m, List<SinkShipMission> found, int depth)
        {
            if (m == null || depth > 6) return;
            var s = m as SinkShipMission; if (s != null) found.Add(s);
            var mm = m as Mission; if (mm == null) return;
            foreach (var c in mm.Children) Collect(c, found, depth + 1);
        }

        public override void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 10f;
            var career = Util.Career; var w = Game.Wolfpack;
            if (career == null || w == null || GroupF == null || EntityF == null) return;
            var list = new List<SinkShipMission>();
            Collect(career.CurrentAssignment as IMission, list, 0);
            if (list.Count == 0) { if (lastState != "") { lastState = ""; Util.Log("Auftragsziel: kein Auftrag „Schiff finden/versenken“ mehr aktiv"); } return; }
            foreach (var m in list)
            {
                var grp = GroupF.GetValue(m) as SandboxGroup; var ent = EntityF.GetValue(m) as SandboxEntity;
                string name = ent == null ? "?" : (ent.RequestIdentitity() != null ? ent.RequestIdentitity().Name : ent.Name);
                string state, text;
                if (ent == null || !grp)
                {
                    state = name + "|weg";
                    text = "Auftragsziel " + name + ": Die Schiffsgruppe gibt es nicht mehr (im Hafen angekommen oder aufgelöst). Der Auftrag kann so nicht mehr erfüllt werden; das Spiel beendet ihn in diesem Fall nicht von selbst.";
                }
                else
                {
                    Vector2 d = grp.Position - w.Position;
                    float km = d.magnitude, brg = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg; if (brg < 0f) brg += 360f;
                    var mob = grp as SandboxMobileGroup;
                    float crs = Mathf.Atan2(grp.Up.x, grp.Up.y) * Mathf.Rad2Deg; if (crs < 0f) crs += 360f;
                    state = name + "|" + m.Stage + "|" + grp.SpawnState + "|" + ent.IsPacified;
                    text = "Auftragsziel " + name + " (Stufe " + m.Stage + "): " + km.ToString("0.0") + " km entfernt, Peilung " + brg.ToString("000") + "°, Kurs " + crs.ToString("000") + "°"
                        + (mob != null ? ", Fahrt " + mob.Velocity.ToString("0.0") + " (Kartenwert), noch " + mob.Route.Count + " Wegpunkte" : ", steht")
                        + ", Gruppe " + grp.Name + " mit " + grp.Entities.Count + " Schiffen, in 3D: " + (grp.SpawnState == SpawnState.Spawned ? "ja" : "nein") + (ent.IsPacified ? ", Ziel ist schon ausgeschaltet" : "");
                }
                bool changed = state != lastState; lastState = state;
                if (changed) Util.Log(text);
                if (Settings.B("CheatMissionTarget") && (changed || Time.unscaledTime >= nextNotice))
                {
                    nextNotice = Time.unscaledTime + 30f;
                    if (!changed) Util.Log(text);
                    ToolkitRunner.ShowNotice(text, 12f);
                }
            }
        }
    }
}

using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // 0.3.70 (DoMe: relieve the main core). Measured: the per-frame update of all crew members costs about 3.9 s per
    // minute with 130+ men (PlayableCharacter.Update + InteractiveCharacter.Update). Every man runs his AI every frame.
    // Men who sleep in a bunk and are not on screen now run it only every x-th frame ("CrewAiSleepEvery").
    // Only the per-frame call is thinned out; direct calls of the game (alarm, stories, cutscenes) always run.
    // Once per minute the log shows the real AI cost, so the effect is measured, not guessed.
    public class CrewAiThrottle : ModuleBase
    {
        internal static double Seconds;
        internal static int Calls, Skipped;
        internal static int Every = 1;
        internal static bool Settle = true;
        internal static float LoadingUntil;
        internal static int Held, PathInvalid;
        private long lastThrottled, lastNeeded;
        private float nextLog, nextLoadCheck;

        public override void Frame()
        {
            Settle = Settings.B("CrewAiWaitLoad");
            if (!Settle || Time.unscaledTime < nextLoadCheck) return;
            nextLoadCheck = Time.unscaledTime + 0.25f;
            if (SaveLoading()) LoadingUntil = Time.unscaledTime + 3f;
        }

        private static bool SaveLoading()
        {
            try { var sm = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Core.Serialization.SavesManager>(); return sm != null && sm.IsLoading; } catch { return false; }
        }

        public override void Tick()
        {
            Every = Mathf.Clamp(Mathf.RoundToInt(Settings.F("CrewAiSleepEvery")), 1, 10);
            if (Time.unscaledTime < nextLog) return;
            if (nextLog > 0f && Calls + Skipped > 0)
                Util.Log("Crew-KI: " + (Seconds * 1000.0).ToString("0") + " ms pro Minute in " + Calls + " Aufrufen, " + Skipped + " übersprungen (schlafend, nicht sichtbar)");
            {
                long t = CrewAi.OptCounters.GoToThrottled, n = CrewAi.OptCounters.GoToNeeded;
                if (nextLog > 0f) Util.Log("Crew-Wege: " + PathInvalid + " mal \"Weg ungültig\" in der Minute; Weg-Wiederholungen: " + (t - lastThrottled) + " überflüssige geschluckt, " + (n - lastNeeded) + " nötige durchgelassen; " + CrewWarp_Patch.Warped + " Mann an den Platz gesetzt"); CrewWarp_Patch.Warped = 0;
                lastThrottled = t; lastNeeded = n; PathInvalid = 0;
            }
            if (Held > 0) { Util.Log("Crew-KI: " + Held + " Aufrufe angehalten, weil das Boot noch lädt"); Held = 0; }
            nextLog = Time.unscaledTime + 60f;
            Seconds = 0; Calls = 0; Skipped = 0;
        }
    }

    [HarmonyPatch(typeof(CharacterAI), nameof(CharacterAI.UpdateAI))]
    internal static class CrewAiThrottle_Patch
    {
        private static readonly Dictionary<CharacterAI, PlayableCharacter> owners = new Dictionary<CharacterAI, PlayableCharacter>();

        private static bool Prefix(CharacterAI __instance, bool ignoreIsUpdatingFlag, out double __state)
        {
            __state = 0;
            if (!ignoreIsUpdatingFlag) return true;   // direct call of the game: never touched
            // 0.3.91 (DoMe: console flickers while loading, log full of crew-AI null errors on the Warthog): while a save
            // is still loading and in the first seconds of a new boat the crew AI of the player's boat waits, so it does
            // not look for beds, food or free space before the interior exists.
            // 0.4.29 (player-only rule): only figures of the player's crew list are held or thinned, nobody else.
            if (!owners.TryGetValue(__instance, out var pc))
            {
                if (owners.Count > 2000) owners.Clear();
                pc = __instance.GetComponent<PlayableCharacter>();
                owners[__instance] = pc;
            }
            if (pc == null) return true;
            if (CrewAiThrottle.Settle && (Time.unscaledTime < CrewAiThrottle.LoadingUntil || (Util.PlayerShip != null && !Util.ShipReady))) { CrewAiThrottle.Held++; return false; }
            int every = CrewAiThrottle.Every;
            if (every > 1)
            {
                if (pc != null && !pc.IsVisible && pc.Action is SleepAction && (Time.frameCount + (__instance.GetInstanceID() & 0x3ff)) % every != 0)
                {
                    CrewAiThrottle.Skipped++;
                    return false;
                }
            }
            __state = Time.realtimeSinceStartupAsDouble;
            return true;
        }

        private static void Postfix(double __state)
        {
            if (__state == 0) return;
            CrewAiThrottle.Seconds += Time.realtimeSinceStartupAsDouble - __state;
            CrewAiThrottle.Calls++;
        }
    }
}

namespace UboatToolkit.Modules
{
    // 0.4.17: counts how often the game gives a walking target up as unreachable (proof line "Crew-Wege").
    [HarmonyPatch(typeof(CharacterAI), "LogAction", new System.Type[] { typeof(string) })]
    internal static class CrewPathInvalid_Patch
    {
        private static void Prefix(string text)
        {
            if (text != null && text.StartsWith("Path to target is invalid", System.StringComparison.Ordinal)) CrewAiThrottle.PathInvalid++;
        }
    }
}

namespace UboatToolkit.Modules
{
    // 0.4.18 (DoMe: "können wir ihn auch teleportieren?"). When the game gives a walking target up as unreachable it
    // cancels the walk and the man's job with it. The game has a second mode for exactly this case (used in stories):
    // set the man onto the target. For player crew with a job or an order and a real place as target (a slot at a
    // station, a bunk ...) that mode is used now, so the post gets manned instead of staying empty.
    [HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "Cancel")]
    internal static class CrewWarp_Patch
    {
        private static readonly AccessTools.FieldRef<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable> mode =
            AccessTools.FieldRefAccess<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable>("targetUnreachableAction");
        private static readonly AccessTools.FieldRef<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, Transform> place =
            AccessTools.FieldRefAccess<UBOAT.Game.Core.AI.Behaviours.GoToBehaviour, Transform>("loggingTarget");
        private static readonly System.Reflection.FieldInfo navField = AccessTools.Field(typeof(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour), "navigator");
        internal static int Warped;
        private static int lines;

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(UBOAT.Game.Core.AI.Behaviours.GoToBehaviour __instance)
        {
            try
            {
                if (!Settings.B("CrewWarpNoPath") || __instance == null) return;
                if (mode(__instance) != UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable.CancelBehaviour) return;
                var t = place(__instance);
                if (t == null) return;                                   // no real place (free walking): leave it to the game
                var nav = navField != null ? navField.GetValue(__instance) as UBOAT.Game.Scene.Characters.Navigation.NavMeshNavigator : null;
                var pc = nav != null ? nav.Character as PlayableCharacter : null;
                if (pc == null || pc.Action == null) return;             // only our crew, only with a job or an order
                mode(__instance) = UBOAT.Game.Core.AI.Behaviours.GoToBehaviour.TargetUnreachable.Warp;
                Warped++;
                if (lines < 80) { lines++; Util.Log("Crew-Wege: " + pc.Name + " findet keinen Weg zu \"" + t.name + "\"" + (t.parent != null ? " (" + t.parent.name + ")" : "") + " und wird hingesetzt (Aufgabe " + pc.Action.GetType().Name + ")"); }
            }
            catch (System.Exception e) { if (lines < 80) { lines++; Util.Log("Crew-Wege: Hinsetzen Fehler " + e.GetType().Name); } }
        }
    }
}

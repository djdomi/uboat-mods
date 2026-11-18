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
                if (nextLog > 0f) Util.Log("Crew-Wege: " + PathInvalid + " mal \"Weg ungültig\" in der Minute; Weg-Wiederholungen: " + (t - lastThrottled) + " überflüssige geschluckt, " + (n - lastNeeded) + " nötige durchgelassen");
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
            if (CrewAiThrottle.Settle && (Time.unscaledTime < CrewAiThrottle.LoadingUntil || (Util.PlayerShip != null && !Util.ShipReady))) { CrewAiThrottle.Held++; return false; }
            int every = CrewAiThrottle.Every;
            if (every > 1)
            {
                if (!owners.TryGetValue(__instance, out var pc))
                {
                    if (owners.Count > 2000) owners.Clear();
                    pc = __instance.GetComponent<PlayableCharacter>();
                    owners[__instance] = pc;
                }
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

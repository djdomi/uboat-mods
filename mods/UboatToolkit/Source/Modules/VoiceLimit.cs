using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Fewer voices: the same line at most every x seconds, short waiting queue, optional gun callouts muted.
    // Every voice line of the game goes through SpeakController.Say (the 7-parameter version); a skipped line returns null
    // (the game handles null, it does the same when a man is injured).
    [HarmonyPatch]
    internal static class VoiceLimit_SayPatch
    {
        private static readonly Dictionary<string, float> lastSaid = new Dictionary<string, float>();
        private static FieldInfo ticketsF;
        private static int dropped; private static float nextLog;

        private static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Tasks.SpeakController");
            foreach (var m in AccessTools.GetDeclaredMethods(t))
                if (m.Name == "Say" && m.GetParameters().Length == 7) return m;
            return null;
        }

        private static bool Prefix(object __instance, string statementId1)
        {
            if (!Settings.B("VoiceLimit") || string.IsNullOrEmpty(statementId1)) return true;
            if (Settings.B("VoiceMuteGunCalls") && (statementId1 == "Artillery Aim" || statementId1 == "Fire!")) return Drop();
            float now = Time.unscaledTime;
            float gap = Settings.F("VoiceRepeatSec");
            int maxQ = Mathf.Max(1, Mathf.RoundToInt(Settings.F("VoiceMaxQueue")));
            if (Settings.B("ExplosionAuto"))
            {
                // Dynamic like the explosions: fluid = more voices allowed, low FPS = fewer.
                float load = Mathf.Clamp01(ExplosionLimit_Patch.Fps() / Mathf.Max(1f, Settings.F("ExplosionTargetFps")));
                gap = Mathf.Lerp(gap * 3f, gap * 0.5f, load);
                maxQ = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, maxQ * 2f, load)));
            }
            if (gap > 0f && lastSaid.TryGetValue(statementId1, out var t) && now - t < gap) return Drop();
            if (ticketsF == null) ticketsF = AccessTools.Field(__instance.GetType(), "tickets");
            var q = ticketsF?.GetValue(__instance) as System.Collections.ICollection;
            if (q != null && q.Count >= maxQ) return Drop();
            lastSaid[statementId1] = now;
            return true;
        }

        private static bool Drop()
        {
            dropped++;
            if (Time.unscaledTime > nextLog) { nextLog = Time.unscaledTime + 60f; Util.Log("Stimmen begrenzt: " + dropped + " Ansagen ausgelassen (gesamt)"); }
            return false;
        }
    }
}

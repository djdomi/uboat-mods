// Crew-KI (Teil des UBOAT Toolkit): CrewAIDiagnostics. Schalter: F10 > QoL > "Crew-KI".
using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using HarmonyLib;
using UBOAT.Game;
using UBOAT.Game.Core.AI;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Characters.Actions;
using Activator = UBOAT.Game.Scene.Characters.Activators.Activator; // System.Activator also in scope
using UBOAT.Game.Scene.Characters.Navigation;

namespace UboatToolkit.Modules.CrewAi
{
    internal static class DiagToggles
    {
        public static bool Verbose;
        public static bool VerboseSetting => UboatToolkit.Settings.B("CrewAILog");
        public static bool LogActions { get { return Verbose; } }
        public static bool LogBehaviours { get { return Verbose; } }
        public static bool LogGameLog { get { return Verbose; } }
        public static bool LogMovement { get { return Verbose; } }

        public static readonly bool LogAnimation = false;

        public static readonly bool LogWorldSimEntities = false;
    }

    internal static class CrewAIInstall
    {
        internal static void Install(Harmony harmony)
        {
            try
            {
                CrewAISettings.Load();   // first: Init and the patch classes read settings
                DiagLog.Init();
                int failed;
                int patched = CrewAIPatching.PatchOwnClasses(harmony, out failed);
                DiagLog.Write("=== Crew-KI (UBOAT Toolkit v" + DiagLog.ManifestVersion() + ") geladen ===" +
                              (DiagToggles.Verbose ? " (verbose logging)" : ""));
                DiagLog.Write("[Patches] " + patched + " patch class(es) applied" + (failed > 0 ? ", " + failed + " FAILED (see output_log.txt)" : ""));
                Debug.Log("[UboatToolkit Crew-KI] geladen (v" + DiagLog.ManifestVersion() + "): " + patched +
                          " patch class(es)" + (failed > 0 ? ", " + failed + " failed" : "") + "; log " + DiagLog.LogPath);
                DiagLog.Write("[Settings] " + CrewAISettings.Status);
                DiagLog.Write("[Settings] " + CrewAISettings.Summary());
                DiagLog.Write("[Config] goToThrottle=" + OptToggles.ThrottleGoToRetries +
                              " (every " + OptToggles.MinRetryFrames + " frames)" +
                              " | slotBlacklist=" + OptToggles.BlacklistUnreachableSlots +
                              " (needs " + OptToggles.MinDistinctFailures + " distinct failures, " +
                              OptToggles.BlacklistSeconds + "s real)" +
                              " | periscopePolicy=" + PeriscopeToggles.Policy +
                              " | worldSimLogging=" + DiagToggles.LogWorldSimEntities +
                              " | behaviourCooldown=" + CooldownToggles.Enabled +
                              " (" + CooldownToggles.DistinctTargetsBeforeCooldown + " targets/" +
                              CooldownToggles.FailureWindowSeconds + "s -> " +
                              CooldownToggles.CooldownSeconds + "s) " +
                              "| loiterFilter=" + LoiterToggles.Enabled +
                              " (max " + LoiterToggles.MaxIdleTravelMeters + "m, restricted=" +
                              string.Join("/", LoiterToggles.RestrictedCompartments) + ")" +
                              " | attackPeriscopeSkipperOnly=" + PeriscopeToggles.AttackPeriscopeSkipperOnly +
                              " | keepHatchOrder=" + HatchToggles.KeepCommandersHatchOrder +
                              " | proximity=" + ProximityToggles.Enabled +
                              " (falloff " + ProximityToggles.PerCompartmentFalloff +
                              "/compartment, floor " + ProximityToggles.MinScale + ")" +
                              " | transitLog=" + TransitToggles.Enabled +
                              " | noEatDuringAlarm=" + AlarmToggles.NoEatingDuringAlarm +
                              " (unless hunger>=" + AlarmToggles.CriticalHunger + ")" +
                              " | nearestBed=" + BedChoiceToggles.PreferNearestBed +
                              " (claim " + BedChoiceToggles.ClaimSeconds + "s, sticky=" +
                              BedChoiceToggles.StickyChoice + ", tieBreak=" +
                              BedChoiceToggles.RandomiseWithinTier + ")" +
                              " | perfProbe=" + PerfToggles.Enabled +
                              " (slow>" + PerfToggles.SlowCallMs + "ms)" +
                              " | orderTracking=" + OrderToggles.Enabled +
                              " (stall>" + OrderToggles.StallSeconds + "s)" +
                              " | restInBunks=" + RestToggles.Enabled +
                              " (idle " + RestToggles.IdleSecondsBeforeRest + "s, sailors only, reserve " +
                              RestToggles.ReserveFreeBunks + " bunks, restProfit " + RestToggles.RestProfit + ", via Use(bed))" +
                              " | crowdCensus=" + CrowdToggles.Enabled +
                              " | keepRunningJobs=" + RoleDropToggles.KeepRunningJobs +
                              " | bedSharing=" + BedSharingToggles.Enabled + " (" + BedSharingToggles.MenPerBunk +
                              " per sailor bunk, only when no empty bunk is left)" +
                              " | crewSlotWaterFix=" + BedSharingToggles.FixCrewSlotWaterCheck +
                              " | queueRoom=" + QueueRoomToggles.MakeRoomForPlayerOrders +
                              " (caps " + QueueRoomToggles.PerCharacterLimit + "/" +
                              QueueRoomToggles.GlobalLimit + ")");
            }
            catch (Exception e)
            {
                Debug.LogError("[UboatToolkit Crew-KI] Fehler beim Einbau");
                Debug.LogException(e);
            }
        }
    }

    internal static class DiagLog
    {
        private static StreamWriter _writer;
        private static readonly object _lock = new object();
        public static string LogPath { get; private set; }

        private static readonly Dictionary<string, float> _lastErrorAt = new Dictionary<string, float>();
        private const float ErrorThrottleSeconds = 5f;

        private const int KeepLogs = 10;

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(UboatToolkit.Settings.Folder, "CrewKI");
                Directory.CreateDirectory(dir);
                try { DiagToggles.Verbose = DiagToggles.VerboseSetting || File.Exists(Path.Combine(dir, "verbose.txt")); } catch { }
                if (!DiagToggles.Verbose) PruneOldLogs(dir);
                LogPath = Path.Combine(dir, "crew_ai_log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt");
                _writer = new StreamWriter(LogPath, false, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Debug.LogError("[UboatToolkit Crew-KI] Log-Datei konnte nicht geöffnet werden");
                Debug.LogException(e);
            }
        }

        private static void PruneOldLogs(string dir)
        {
            try
            {
                var logs = Directory.GetFiles(dir, "crew_ai_log_*.txt");
                Array.Sort(logs, StringComparer.Ordinal);
                for (int i = 0; i < logs.Length - (KeepLogs - 1); i++)
                {
                    try { File.Delete(logs[i]); } catch { }
                }
            }
            catch { }
        }

        public static string ManifestVersion()
        {
            try
            {
                var manager = DWS.Common.InjectionFramework.ScriptableObjectSingleton.LoadSingleton<UBOAT.Game.Core.Mods.ModManager>();
                var self = manager != null ? manager.GetMod(Assembly.GetExecutingAssembly()) : null;
                if (self == null || string.IsNullOrEmpty(self.Path)) return "?";
                string m = Path.Combine(self.Path, "Manifest.json");
                if (!File.Exists(m)) return "?";
                var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(m), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (match.Success) return match.Groups[1].Value;
            }
            catch { }
            return "?";
        }

        public static void Write(string line)
        {
            lock (_lock)
            {
                try
                {
                    if (_writer == null) return;
                    _writer.WriteLine("[gt=" + Time.time.ToString("F2") +
                                      " rt=" + Time.unscaledTime.ToString("F2") + "] " + line);
                }
                catch { /* never let logging crash the game */ }
            }
        }

        public static void WriteError(string tag, Exception ex)
        {
            try
            {
                float now = Time.unscaledTime;
                lock (_lock)
                {
                    float last;
                    if (_lastErrorAt.TryGetValue(tag, out last) && now - last < ErrorThrottleSeconds)
                        return;
                    _lastErrorAt[tag] = now;
                }
                Write("[ERROR:" + tag + "] " + ex.GetType().Name + ": " + ex.Message);
            }
            catch { /* absolute last resort */ }
        }

        public static string NameOf(PlayableCharacter c) { return c != null ? c.Name : "unknown"; }

        public static string NameOf(InteractiveCharacter c)
        {
            var p = c as PlayableCharacter;
            if (p != null) return p.Name;
            return c != null ? c.name : "unknown";
        }

        public static bool IsWorldSimName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < name.Length - 1; i++)
            {
                if (name[i] == '#' && name[i + 1] >= '0' && name[i + 1] <= '9') return true;
            }
            return false;
        }

        public static bool ShouldLog(string name)
        {
            if (DiagToggles.LogWorldSimEntities) return true;
            return !IsWorldSimName(name);
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StartAction",
        new Type[] { typeof(Activator), typeof(CharacterAction) })]
    internal static class Patch_StartAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(PlayableCharacter __instance, Activator activator, CharacterAction action)
        {
            if (!DiagToggles.LogActions) return;
            try
            {
                string who = DiagLog.NameOf(__instance);
                if (!DiagLog.ShouldLog(who)) return;
                string actionType = action != null ? action.GetType().Name : "null";
                string activatorName = activator != null ? activator.name : "null";
                DiagLog.Write("[Action] StartAction | character=" + who + " action=" + actionType +
                              " activator=" + activatorName);
            }
            catch (Exception e) { DiagLog.WriteError("StartAction", e); }
        }
    }

    [HarmonyPatch(typeof(PlayableCharacter), "StopCurrentAction", new Type[] { typeof(bool) })]
    internal static class Patch_StopCurrentAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Prefix(PlayableCharacter __instance, bool ignoreCanBeStoppedFlag)
        {
            if (!DiagToggles.LogActions) return;
            try
            {
                string who = DiagLog.NameOf(__instance);
                if (!DiagLog.ShouldLog(who)) return;
                string actionType = "none";
                try
                {
                    if (__instance != null && __instance.Action != null)
                        actionType = __instance.Action.GetType().Name;
                }
                catch { }
                DiagLog.Write("[Action] StopCurrentAction | character=" + who +
                              " currentAction=" + actionType +
                              " ignoreCanBeStoppedFlag=" + ignoreCanBeStoppedFlag);
            }
            catch (Exception e) { DiagLog.WriteError("StopCurrentAction", e); }
        }
    }

    internal static class ChurnTracker
    {
        private class Started { public int Frame; }
        private static readonly Dictionary<string, Started> _open = new Dictionary<string, Started>();
        private static readonly object _lock = new object();

        private static string Key(string owner, string behaviour) { return owner + "|" + behaviour; }

        public static void NoteStart(string owner, string behaviour)
        {
            if (!OptToggles.MeasureBehaviourChurn) return;
            try
            {
                OptCounters.BehaviourStarts++;
                lock (_lock) { _open[Key(owner, behaviour)] = new Started { Frame = Time.frameCount }; }
            }
            catch { }
        }

        public static bool NoteStop(string owner, string behaviour)
        {
            if (!OptToggles.MeasureBehaviourChurn) return false;
            try
            {
                Started s;
                string k = Key(owner, behaviour);
                lock (_lock)
                {
                    if (!_open.TryGetValue(k, out s)) return false;
                    _open.Remove(k);
                }
                if (s.Frame == Time.frameCount)
                {
                    OptCounters.BehaviourSameFrameStops++;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StartBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_CharacterAI_StartBehaviour
    {
        static void Postfix(CharacterAI __instance, AIBehaviour behaviour)
        {
            try
            {
                string owner = __instance != null ? __instance.name : "unknown";
                string b = behaviour != null ? behaviour.GetType().Name : "null";
                ChurnTracker.NoteStart(owner, b);
                BehaviourCooldown.NoteBehaviourStarted(owner, b);
                if (!DiagToggles.LogBehaviours || !DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] StartBehaviour | owner=" + owner + " behaviour=" + b);
            }
            catch (Exception e) { DiagLog.WriteError("StartBehaviour", e); }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "StopBehaviour", new Type[] { typeof(AIBehaviour) })]
    internal static class Patch_CharacterAI_StopBehaviour
    {
        static void Prefix(CharacterAI __instance, AIBehaviour behaviour)
        {
            try
            {
                string owner = __instance != null ? __instance.name : "unknown";
                string b = behaviour != null ? behaviour.GetType().Name : "null";
                bool sameFrame = ChurnTracker.NoteStop(owner, b);
                if (!DiagToggles.LogBehaviours || !DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] StopBehaviour | owner=" + owner + " behaviour=" + b +
                              (sameFrame ? " SAMEFRAME" : ""));
            }
            catch (Exception e) { DiagLog.WriteError("StopBehaviour", e); }
        }
    }

    [HarmonyPatch(typeof(AIBehaviour), "OnBehaviourStart")]
    internal static class Patch_AIBehaviour_OnStart
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(AIBehaviour __instance)
        {
            if (!DiagToggles.LogBehaviours) return;
            try
            {
                if (__instance == null) return;
                string owner = __instance.AI != null ? __instance.AI.name : "unknown";
                if (!DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] OnBehaviourStart | type=" + __instance.GetType().Name + " owner=" + owner);
            }
            catch (Exception e) { DiagLog.WriteError("OnBehaviourStart", e); }
        }
    }

    [HarmonyPatch(typeof(AIBehaviour), "OnBehaviourStop")]
    internal static class Patch_AIBehaviour_OnStop
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(AIBehaviour __instance)
        {
            if (!DiagToggles.LogBehaviours) return;
            try
            {
                if (__instance == null) return;
                string owner = __instance.AI != null ? __instance.AI.name : "unknown";
                if (!DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[Behaviour] OnBehaviourStop | type=" + __instance.GetType().Name + " owner=" + owner);
            }
            catch (Exception e) { DiagLog.WriteError("OnBehaviourStop", e); }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "Awake")]
    internal static class Patch_CharacterAI_Awake
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static readonly FieldInfo LogField = AccessTools.Field(typeof(CharacterAI), "log");
        static readonly FieldInfo ActionLogField = AccessTools.Field(typeof(CharacterAI), "actionLog");
        static bool _warnedOnce;

        static void Postfix(CharacterAI __instance)
        {
            if (!DiagToggles.LogGameLog) return;
            try
            {
                if (ActionLogField != null && ActionLogField.GetValue(__instance) == null)
                {
                    try
                    {
                        object instance = System.Activator.CreateInstance(ActionLogField.FieldType);
                        ActionLogField.SetValue(__instance, instance);
                    }
                    catch (Exception ctorEx)
                    {
                        if (!_warnedOnce)
                        {
                            _warnedOnce = true;
                            DiagLog.Write("[GameLog] couldn't construct " + ActionLogField.FieldType.Name +
                                          " (" + ctorEx.GetType().Name + ": " + ctorEx.Message +
                                          ") - leaving CharacterAI.log off to avoid crashing behaviour evaluation");
                        }
                    }
                }

                bool allocated = ActionLogField == null || ActionLogField.GetValue(__instance) != null;
                if (allocated && LogField != null) LogField.SetValue(__instance, true);
            }
            catch (Exception e) { DiagLog.WriteError("CharacterAI.Awake", e); }
        }
    }

    [HarmonyPatch(typeof(CharacterAI), "LogAction", new Type[] { typeof(string) })]
    internal static class Patch_CharacterAI_LogAction
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(CharacterAI __instance, string text)
        {
            if (!DiagToggles.LogGameLog) return;
            try
            {
                string owner = __instance != null ? __instance.name : "unknown";
                if (!DiagLog.ShouldLog(owner)) return;
                DiagLog.Write("[GameLog] " + owner + " | " + text);
            }
            catch (Exception e) { DiagLog.WriteError("LogAction", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "GoTo", new Type[] { typeof(Vector3), typeof(float), typeof(bool) })]
    internal static class Patch_Navigator_GoTo
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(NavMeshNavigator __instance, Vector3 navSpacePosition, float stopDistance, bool forceRun)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                bool throttled = GoToLogState.LastCallThrottled;
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                string area = (__instance != null && __instance.Area != null) ? __instance.Area.name : "?";
                DiagLog.Write("[Movement] GoTo | character=" + who + " target=" + navSpacePosition +
                              " stopDist=" + stopDistance + " forceRun=" + forceRun + " area=" + area +
                              " throttled=" + throttled);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.GoTo", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "Stop")]
    internal static class Patch_Navigator_Stop
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Prefix(NavMeshNavigator __instance)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Movement] Stop | character=" + who);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.Stop", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "StartWaitAt", new Type[] { typeof(Vector3) })]
    internal static class Patch_Navigator_StartWaitAtPos
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Postfix(NavMeshNavigator __instance, Vector3 targetPosition)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Movement] StartWaitAt(pos) | character=" + who + " pos=" + targetPosition);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.StartWaitAt", e); }
        }
    }

    [HarmonyPatch(typeof(NavMeshNavigator), "StopWait")]
    internal static class Patch_Navigator_StopWait
    {
        static bool Prepare() { return DiagToggles.Verbose; }   // logging only
        static void Prefix(NavMeshNavigator __instance)
        {
            if (!DiagToggles.LogMovement) return;
            try
            {
                string who = __instance != null ? DiagLog.NameOf(__instance.Character) : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Movement] StopWait | character=" + who);
            }
            catch (Exception e) { DiagLog.WriteError("Navigator.StopWait", e); }
        }
    }

    [HarmonyPatch(typeof(Animator), "SetTrigger", new Type[] { typeof(string) })]
    internal static class Patch_SetTrigger
    {
        static bool Prepare() { return DiagToggles.LogAnimation; }   // v1.0.0: patches EVERY Animator; not installed while off
        static void Prefix(Animator __instance, string name)
        {
            if (!DiagToggles.LogAnimation) return;
            try
            {
                string who = __instance != null ? __instance.name : "unknown";
                if (!DiagLog.ShouldLog(who)) return;
                DiagLog.Write("[Animation] SetTrigger | object=" + who + " trigger=" + name);
            }
            catch (Exception e) { DiagLog.WriteError("Animator.SetTrigger", e); }
        }
    }
}

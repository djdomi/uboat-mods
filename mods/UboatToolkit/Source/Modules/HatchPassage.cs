using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Characters.Navigation;
using UBOAT.Game.Scene.Entities.NavMesh;

namespace UboatToolkit.Modules
{
    // Hatches in first person: keep the walk/run passage the player started with (the game
    // drops to the slow walk passage mid-hatch), speed the passage up, a short run tail after
    // the hatch, optional walk animation for sprinting, and a frame-by-frame log for checking.
    internal enum PassageVariant { Auto, Walk, Run }

    internal static class HtfSettings
    {
        public static bool Enabled => Settings.B("FastHatch");
        public static bool HoldPassageState => Settings.B("HatchHoldState");
        public static PassageVariant PassageVariant
        {
            get { int v = Mathf.RoundToInt(Settings.F("HatchVariant")); return v == 1 ? PassageVariant.Walk : v == 2 ? PassageVariant.Run : PassageVariant.Auto; }
        }
        public static bool WalkAnimationForSprint => Settings.B("HatchWalkForSprint");
        public static float SprintSpeedMultiplier => Settings.F("HatchSprintSpeed");
        public static float WalkSpeedMultiplier => Settings.F("HatchWalkSpeed");
        public static float MaxAnimatorSpeed => Settings.F("HatchMaxSpeed");
        public static float PostPassageRunTailSeconds => Settings.F("HatchRunTail");
        public static bool Diagnostics => Settings.B("HatchLog");
    }

    internal static class HatchLog
    {
        private static bool reported;
        public static void Line(string text)
        {
            if (!HtfSettings.Diagnostics) return;
            if (!reported)
            {
                reported = true;
                Util.Log("Luken: tracker=" + (FppNavigatorTracker.Resolved ? "ok" : "FEHLT") + " passageHold=" + (PassageStatePatch.Resolved ? "ok" : "FEHLT") + " passageSpeed=" + (PassageSpeedPatch.Resolved ? "ok" : "FEHLT"));
                Util.Log("Luken: " + HatchDiagnostics.PatchReport());
                Util.Log("Luken: " + HatchDiagnostics.KnownHashReport());
            }
            Util.Log("Luken: " + text);
        }
        public static string F(float v, int d) => v.ToString("F" + d.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        public static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        public static string B(bool v) => v ? "1" : "0";
    }

    [HarmonyPatch(typeof(FPPCameraController), "Update")]
    internal static class FppNavigatorTracker
    {
        private static readonly FieldInfo NavigatorField =
            AccessTools.Field(typeof(FPPCameraController), "characterNavigator");
        private static readonly FieldInfo RunInputField =
            AccessTools.Field(typeof(FPPCameraController), "runInput");
        private static NavMeshNavigator _navigator;
        private static bool _runKeyHeld;
        private static int _frame = -1;
        public static bool Resolved { get { return NavigatorField != null && RunInputField != null; } }
        public static bool RunKeyHeld
        {
            get { return _runKeyHeld && Time.frameCount - _frame <= 2; }
        }
        public static bool IsPlayerNavigator(NavMeshNavigator candidate)
        {
            if (_navigator == null || candidate == null) return false;
            if (!ReferenceEquals(_navigator, candidate)) return false;
            return Time.frameCount - _frame <= 2;
        }
        private static void Postfix(FPPCameraController __instance)
        {
            if (!Resolved) return;
            try
            {
                
                _navigator = NavigatorField.GetValue(__instance) as NavMeshNavigator;
                _runKeyHeld = ReadRunKey(__instance);
                _frame = Time.frameCount;
            }
            catch (Exception)
            {
            }
        }
        private static bool ReadRunKey(FPPCameraController controller)
        {
            var link = RunInputField.GetValue(controller);
            if (link == null) return false;
            var assetProperty = link.GetType().GetProperty("Asset");
            if (assetProperty == null) return false;
            var axis = assetProperty.GetValue(link, null);
            if (axis == null) return false;
            var pressedProperty = axis.GetType().GetProperty("Pressed");
            if (pressedProperty == null) return false;
            return (bool)pressedProperty.GetValue(axis, null);
        }
    }


    [HarmonyPatch(typeof(NavMeshNavigator), "Update")]
    internal static class PassageStatePatch
    {
        private static readonly FieldInfo LinkHandler =
            AccessTools.Field(typeof(NavMeshNavigator), "currentLinkEventHandler");
        private static readonly FieldInfo ParamRunning =
            AccessTools.Field(typeof(NavMeshNavigator), "paramRunning");
        private static readonly FieldInfo ParamWalking =
            AccessTools.Field(typeof(NavMeshNavigator), "paramWalking");
        private static bool _faulted;
        private static UnityEngine.AI.OffMeshLink _activeLink;
        private static bool _holdRunning;
        private static bool _sprintAsWalk;
        private static bool _crossfaded;
        private static int _crossfades;
        private static int _passages;
        private static int _framesHeld;
        private static float _tailUntil;
        private static int _tailFrames;
        public static bool Resolved { get { return LinkHandler != null && ParamRunning != null; } }
        public static bool IsHoldingRunPassage
        {
            get { return _activeLink != null && _holdRunning; }
        }
        public static bool IsSprintingAsWalk
        {
            get { return _activeLink != null && _sprintAsWalk; }
        }
        private const int NormalPassageHash = -1632352039;
        private const int AlertPassageHash = -155423246;
        private const float NormalClipSeconds = 2.72f;
        private const float CrossfadeSeconds = 0.15f;
        public static string Report()
        {
            return "passage hold: " + HatchLog.I(_passages) + " passage(s), "
                 + HatchLog.I(_framesHeld) + " frame(s) held, "
                 + HatchLog.I(_tailFrames) + " tail frame(s), "
                 + HatchLog.I(_crossfades) + " sprint-as-walk crossfade(s)";
        }
        private static void Postfix(NavMeshNavigator __instance)
        {
            if (_faulted || !Resolved) return;
            try
            {
                var link = HatchLinkFor(__instance);
                if (link == null)
                {
                    if (FppNavigatorTracker.IsPlayerNavigator(__instance))
                    {
                        if (_activeLink != null && (_holdRunning || _sprintAsWalk))
                            _tailUntil = Time.unscaledTime + HtfSettings.PostPassageRunTailSeconds;
                        _activeLink = null;
                        RunTail(__instance);
                    }
                    return;
                }
                _tailUntil = 0f;
                if (!HtfSettings.Enabled || !HtfSettings.HoldPassageState)
                {
                    _activeLink = null;
                    return;
                }
                var animator = __instance.Animator;
                if (animator == null) return;
                int runningHash = (int)ParamRunning.GetValue(null);
                if (!ReferenceEquals(link, _activeLink))
                {
                    _activeLink = link;
                    bool sprinting = SprintIntent(animator, runningHash);
                    _holdRunning = DecideVariant(sprinting);
                    _sprintAsWalk = sprinting && !_holdRunning && HtfSettings.WalkAnimationForSprint;
                    _crossfaded = false;
                    _passages++;
                    if (HtfSettings.Diagnostics)
                        HatchLog.Line("passage #" + HatchLog.I(_passages) + " held as "
                                      + (_holdRunning ? "RUN" : (_sprintAsWalk ? "WALK (sprint)" : "WALK"))
                                      + " (link '" + link.name + "')");
                }
                animator.SetBool(runningHash, _holdRunning);
                _framesHeld++;
                if (_sprintAsWalk && !_crossfaded) LeaveAlertState(animator);
            }
            catch (Exception e)
            {
                _faulted = true;
                _activeLink = null;
                HatchLog.Line("passage hold disabled after an exception: " + e);
            }
        }
        private static void RunTail(NavMeshNavigator navigator)
        {
            if (_tailUntil <= 0f || Time.unscaledTime >= _tailUntil) return;
            if (!HtfSettings.Enabled || !HtfSettings.HoldPassageState) return;
            if (ParamWalking == null) return;
            if (!FppNavigatorTracker.RunKeyHeld) return;
            var animator = navigator.Animator;
            if (animator == null) return;
            if (!animator.GetBool((int)ParamWalking.GetValue(null))) return;
            animator.SetBool((int)ParamRunning.GetValue(null), true);
            _tailFrames++;
        }
        private static bool DecideVariant(bool sprinting)
        {
            if (HtfSettings.PassageVariant == PassageVariant.Walk) return false;
            if (HtfSettings.PassageVariant == PassageVariant.Run) return true;
            if (HtfSettings.WalkAnimationForSprint) return false;
            return sprinting;
        }
        private static void LeaveAlertState(Animator animator)
        {
            float nt;
            if (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == AlertPassageHash)
                nt = animator.GetNextAnimatorStateInfo(0).normalizedTime;
            else if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == AlertPassageHash)
                nt = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            else
                return;
            _crossfaded = true;
            _crossfades++;
            nt = Mathf.Clamp01(nt);
            animator.CrossFadeInFixedTime(NormalPassageHash, CrossfadeSeconds, 0, nt * NormalClipSeconds);
            if (HtfSettings.Diagnostics)
                HatchLog.Line("sprint-as-walk: Alert -> Normal crossfade at nt " + HatchLog.F(nt, 3));
        }
        private static bool SprintIntent(Animator animator, int runningHash)
        {
            if (FppNavigatorTracker.RunKeyHeld) return true;
            return animator.GetBool(runningHash);
        }
        private static UnityEngine.AI.OffMeshLink HatchLinkFor(NavMeshNavigator navigator)
        {
            if (!FppNavigatorTracker.IsPlayerNavigator(navigator)) return null;
            if (!navigator.IsOnOffMeshLink) return null;
            if (!(LinkHandler.GetValue(navigator) is NavMeshHatch)) return null;
            return navigator.CurrentLink;
        }
    }


    [HarmonyPatch(typeof(FPPCameraController), "ParseInputsFPPMode")]
    internal static class PassageSpeedPatch
    {
        public const float RunPassageParityMultiplier = 1.103f;
        private const float VanillaSpeedBase = 0.0001f;
        private const float VanillaSpeedGain = 1.25f;
        private static readonly FieldInfo FppNavigator =
            AccessTools.Field(typeof(FPPCameraController), "characterNavigator");
        private static readonly FieldInfo FppTravelDirection =
            AccessTools.Field(typeof(FPPCameraController), "offMeshLinkTravelDirection");
        private static readonly FieldInfo NavLinkHandler =
            AccessTools.Field(typeof(NavMeshNavigator), "currentLinkEventHandler");
        private static bool _faulted;
        private static int _frames;
        private static float _highestWritten;
        public static bool Resolved
        {
            get
            {
                return FppNavigator != null && FppTravelDirection != null && NavLinkHandler != null;
            }
        }
        public static string Report()
        {
            return "passage speed: " + HatchLog.I(_frames) + " frame(s), peak "
                 + HatchLog.F(_highestWritten, 3);
        }
        private static void Postfix(FPPCameraController __instance)
        {
            if (_faulted || !Resolved || !HtfSettings.Enabled) return;
            try
            {
                var navigator = FppNavigator.GetValue(__instance) as NavMeshNavigator;
                if (navigator == null || !navigator.IsOnOffMeshLink) return;
                if (!(NavLinkHandler.GetValue(navigator) is NavMeshHatch)) return;
                var animator = navigator.Animator;
                if (animator == null) return;
                float direction = (float)FppTravelDirection.GetValue(__instance);
                if (direction < 0f && !animator.IsInTransition(0)) return;
                float multiplier = Multiplier();
                if (multiplier <= 1.0001f) return;
                float vanilla = VanillaSpeedBase + Mathf.Max(0f, direction) * VanillaSpeedGain;
                float target = Mathf.Min(vanilla * multiplier, HtfSettings.MaxAnimatorSpeed);
                if (target <= vanilla) return;
                animator.speed = target;
                _frames++;
                if (target > _highestWritten) _highestWritten = target;
            }
            catch (Exception e)
            {
                _faulted = true;
                HatchLog.Line("passage speed disabled after an exception: " + e);
            }
        }
        private static float Multiplier()
        {
            if (PassageStatePatch.IsHoldingRunPassage)
                return Mathf.Max(HtfSettings.SprintSpeedMultiplier, RunPassageParityMultiplier);
            if (PassageStatePatch.IsSprintingAsWalk)
                return Mathf.Max(HtfSettings.SprintSpeedMultiplier, RunPassageParityMultiplier)
                       / RunPassageParityMultiplier;
            return HtfSettings.WalkSpeedMultiplier;
        }
    }


    [HarmonyPatch(typeof(FPPCameraController), "Update")]
    internal static class HatchDiagnostics
    {
        private static readonly FieldInfo FppNavigator =
            AccessTools.Field(typeof(FPPCameraController), "characterNavigator");
        private static readonly FieldInfo FppTravelDirection =
            AccessTools.Field(typeof(FPPCameraController), "offMeshLinkTravelDirection");
        private static readonly FieldInfo FppMovementByAnimation =
            AccessTools.Field(typeof(FPPCameraController), "movementControlledByAnimation");
        private static readonly FieldInfo FppRunInput =
            AccessTools.Field(typeof(FPPCameraController), "runInput");
        private static readonly FieldInfo NavLinkHandler =
            AccessTools.Field(typeof(NavMeshNavigator), "currentLinkEventHandler");
        private static readonly FieldInfo NavForceRun =
            AccessTools.Field(typeof(NavMeshNavigator), "forceRun");
        private static readonly FieldInfo NavParamWalking =
            AccessTools.Field(typeof(NavMeshNavigator), "paramWalking");
        private static readonly FieldInfo NavParamRunning =
            AccessTools.Field(typeof(NavMeshNavigator), "paramRunning");
        private static readonly FieldInfo NavMoveThroughHatch =
            AccessTools.Field(typeof(NavMeshNavigator), "moveThroughHatchHash");
        private static readonly FieldInfo NavLocomotionMode =
            AccessTools.Field(typeof(NavMeshNavigator), "locomotionModeHash");
        private static readonly FieldInfo HatchParamName =
            AccessTools.Field(typeof(NavMeshHatch), "animatorParameter");
        private static readonly FieldInfo HatchParamHash =
            AccessTools.Field(typeof(NavMeshHatch), "animatorParameterHash");
        private static readonly FieldInfo HatchNormalStates =
            AccessTools.Field(typeof(NavMeshHatch), "normalPassageStatesHashes");
        private static readonly FieldInfo HatchPassing =
            AccessTools.Field(typeof(NavMeshHatch), "passingNavigators");
        private static readonly FieldInfo HatchQueued =
            AccessTools.Field(typeof(NavMeshHatch), "navigators");
        private const int MaxLinesPerTraversal = 600;
        private const int PostPassageTraceFrames = 90;
        private static int _postFrames;
        private static bool _onLink;
        private static UnityEngine.AI.OffMeshLink _link;
        private static int _frames;
        private static int _lines;
        private static float _startTime;
        private static bool _runHeldAtEntry;
        private static int _lastStateHash;
        private static float _lastNormalizedTime;
        private static float _maxNormalizedTime;
        private static int _restarts;
        private static int _stateChanges;
        private static readonly List<string> _statesSeen = new List<string>();
        private static bool _faulted;
        public static string PatchReport()
        {
            var target = AccessTools.Method(typeof(FPPCameraController), "Update");
            var missing = new List<string>();
            if (target == null) missing.Add("FPPCameraController.Update");
            if (FppNavigator == null) missing.Add("characterNavigator");
            if (FppTravelDirection == null) missing.Add("offMeshLinkTravelDirection");
            if (FppMovementByAnimation == null) missing.Add("movementControlledByAnimation");
            if (FppRunInput == null) missing.Add("runInput");
            if (NavLinkHandler == null) missing.Add("currentLinkEventHandler");
            if (NavParamWalking == null) missing.Add("paramWalking");
            if (NavParamRunning == null) missing.Add("paramRunning");
            if (NavMoveThroughHatch == null) missing.Add("moveThroughHatchHash");
            if (HatchParamHash == null) missing.Add("animatorParameterHash");
            if (HatchNormalStates == null) missing.Add("normalPassageStatesHashes");
            if (missing.Count == 0)
                return "all 11 vanilla members resolved";
            return "MISSING " + missing.Count + ": " + string.Join(", ", missing.ToArray());
        }
        public static string KnownHashReport()
        {
            var sb = new StringBuilder();
            sb.Append("known hashes: ");
            sb.Append("'Get Through Hatch Normal'=").Append(HatchLog.I(Animator.StringToHash("Get Through Hatch Normal")));
            sb.Append(" 'Get Through Hatch Alert'=").Append(HatchLog.I(Animator.StringToHash("Get Through Hatch Alert")));
            sb.Append(" 'Bucket - Get Through Hole'=").Append(HatchLog.I(Animator.StringToHash("Bucket - Get Through Hole")));
            sb.Append(" | normalPassageStatesHashes=");
            var arr = HatchNormalStates == null ? null : HatchNormalStates.GetValue(null) as int[];
            if (arr == null)
            {
                sb.Append("unavailable");
            }
            else
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(HatchLog.I(arr[i]));
                }
            }
            return sb.ToString();
        }
        private static void Postfix(FPPCameraController __instance)
        {
            if (_faulted) return;
            if (!HtfSettings.Diagnostics)
            {
                EndTraversal("diagnostics switched off");
                return;
            }
            try
            {
                Trace(__instance);
            }
            catch (Exception e)
            {
                _faulted = true;
                HatchLog.Line("diagnostic disabled after an exception: " + e);
            }
        }
        private static void Trace(FPPCameraController controller)
        {
            var navigator = FppNavigator.GetValue(controller) as NavMeshNavigator;
            if (navigator == null)
            {
                EndTraversal("navigator went null");
                return;
            }
            if (!navigator.IsOnOffMeshLink)
            {
                if (_onLink)
                {
                    EndTraversal("left link");
                    _postFrames = PostPassageTraceFrames;
                }
                if (_postFrames > 0)
                {
                    _postFrames--;
                    LogPostPassageFrame(controller, navigator);
                }
                return;
            }
            _postFrames = 0;
            var animator = navigator.Animator;
            if (animator == null) return;
            var handler = NavLinkHandler.GetValue(navigator);
            var hatch = handler as NavMeshHatch;
            var link = navigator.CurrentLink;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            bool inTransition = animator.IsInTransition(0);
            float normalizedTime = state.normalizedTime;
            if (!_onLink || !ReferenceEquals(link, _link))
            {
                EndTraversal("superseded");
                BeginTraversal(controller, navigator, hatch, link);
            }
            _frames++;
            if (state.shortNameHash != _lastStateHash)
            {
                if (_lastStateHash != 0) _stateChanges++;
                _lastStateHash = state.shortNameHash;
                _lastNormalizedTime = normalizedTime;
                _maxNormalizedTime = normalizedTime;
                RecordState(animator, state);
            }
            else
            {
                if (normalizedTime < _lastNormalizedTime - 0.1f) _restarts++;
                _lastNormalizedTime = normalizedTime;
                if (normalizedTime > _maxNormalizedTime) _maxNormalizedTime = normalizedTime;
            }
            if (_lines >= MaxLinesPerTraversal) return;
            _lines++;
            var sb = new StringBuilder(320);
            sb.Append("f=").Append(HatchLog.I(Time.frameCount));
            sb.Append(" n=").Append(HatchLog.I(_frames));
            sb.Append(" state=").Append(HatchLog.I(state.shortNameHash));
            sb.Append(" known=").Append(HatchLog.B(IsNormalPassageState(state.shortNameHash)));
            sb.Append(" nt=").Append(HatchLog.F(normalizedTime, 3));
            sb.Append(" tr=").Append(HatchLog.B(inTransition));
            sb.Append(" clip=").Append(ClipDescription(animator));
            sb.Append(" spd=").Append(HatchLog.F(animator.speed, 4));
            sb.Append(" dir=").Append(HatchLog.F(ReadFloat(FppTravelDirection, controller), 3));
            sb.Append(" inter=").Append(HatchLog.F(animator.GetFloat("Interactivity"), 3));
            sb.Append(" walking=").Append(BoolParam(animator, NavParamWalking));
            sb.Append(" running=").Append(BoolParam(animator, NavParamRunning));
            sb.Append(" thruHatch=").Append(BoolParam(animator, NavMoveThroughHatch));
            sb.Append(" hatchBool=").Append(HatchBool(animator, hatch));
            sb.Append(" locMode=").Append(IntParam(animator, NavLocomotionMode));
            sb.Append(" mca=").Append(HatchLog.B(ReadBool(FppMovementByAnimation, controller)));
            sb.Append(" navRun=").Append(HatchLog.B(navigator.Run));
            sb.Append(" forceRun=").Append(HatchLog.B(ReadBool(NavForceRun, navigator)));
            sb.Append(" runKey=").Append(RunInputPressed(controller));
            if (inTransition)
            {
                var next = animator.GetNextAnimatorStateInfo(0);
                sb.Append(" next=").Append(HatchLog.I(next.shortNameHash));
                sb.Append(" nextNt=").Append(HatchLog.F(next.normalizedTime, 3));
            }
            HatchLog.Line(sb.ToString());
        }
        private static void BeginTraversal(FPPCameraController controller,
                                           NavMeshNavigator navigator,
                                           NavMeshHatch hatch,
                                           UnityEngine.AI.OffMeshLink link)
        {
            _onLink = true;
            _link = link;
            _frames = 0;
            _lines = 0;
            _startTime = Time.unscaledTime;
            _restarts = 0;
            _stateChanges = 0;
            _lastStateHash = 0;
            _maxNormalizedTime = 0f;
            _statesSeen.Clear();
            _runHeldAtEntry = navigator.Run;
            var sb = new StringBuilder(256);
            sb.Append("ENTER link='").Append(link == null ? "none" : link.name).Append('\'');
            sb.Append(" handler=").Append(HandlerName(navigator));
            sb.Append(" runHeld=").Append(HatchLog.B(_runHeldAtEntry));
            sb.Append(" runKey=").Append(RunInputPressed(controller));
            if (hatch != null)
            {
                var paramName = HatchParamName == null ? null : HatchParamName.GetValue(hatch) as string;
                sb.Append(" hatchParam='").Append(paramName ?? "?").Append('\'');
                sb.Append(" queued=").Append(HatchLog.I(ListCount(HatchQueued, hatch)));
                sb.Append(" passing=").Append(HatchLog.I(ListCount(HatchPassing, hatch)));
            }
            HatchLog.Line(sb.ToString());
        }
        private static void EndTraversal(string reason)
        {
            if (!_onLink) return;
            _onLink = false;
            _link = null;
            var sb = new StringBuilder(256);
            sb.Append("EXIT ").Append(reason);
            sb.Append(" runHeldAtEntry=").Append(HatchLog.B(_runHeldAtEntry));
            sb.Append(" frames=").Append(HatchLog.I(_frames));
            sb.Append(" dur=").Append(HatchLog.F(Time.unscaledTime - _startTime, 2)).Append('s');
            sb.Append(" restarts=").Append(HatchLog.I(_restarts));
            sb.Append(" stateChanges=").Append(HatchLog.I(_stateChanges));
            sb.Append(" maxNt=").Append(HatchLog.F(_maxNormalizedTime, 2));
            sb.Append(" states=[");
            for (int i = 0; i < _statesSeen.Count; i++)
            {
                if (i > 0) sb.Append(" | ");
                sb.Append(_statesSeen[i]);
            }
            sb.Append(']');
            if (_lines >= MaxLinesPerTraversal)
                sb.Append(" (per-frame lines were capped at ").Append(HatchLog.I(MaxLinesPerTraversal)).Append(')');
            HatchLog.Line(sb.ToString());
        }
        private static void LogPostPassageFrame(FPPCameraController controller,
                                                NavMeshNavigator navigator)
        {
            var animator = navigator.Animator;
            if (animator == null) return;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            var sb = new StringBuilder(220);
            sb.Append("POST f=").Append(HatchLog.I(Time.frameCount));
            sb.Append(" left=").Append(HatchLog.I(_postFrames));
            sb.Append(" state=").Append(HatchLog.I(state.shortNameHash));
            sb.Append(" nt=").Append(HatchLog.F(state.normalizedTime, 3));
            sb.Append(" tr=").Append(HatchLog.B(animator.IsInTransition(0)));
            sb.Append(" clip=").Append(ClipDescription(animator));
            sb.Append(" spd=").Append(HatchLog.F(animator.speed, 3));
            sb.Append(" walking=").Append(BoolParam(animator, NavParamWalking));
            sb.Append(" running=").Append(BoolParam(animator, NavParamRunning));
            sb.Append(" thruHatch=").Append(BoolParam(animator, NavMoveThroughHatch));
            sb.Append(" mca=").Append(HatchLog.B(ReadBool(FppMovementByAnimation, controller)));
            sb.Append(" navRun=").Append(HatchLog.B(navigator.Run));
            sb.Append(" runKey=").Append(HatchLog.B(FppNavigatorTracker.RunKeyHeld));
            HatchLog.Line(sb.ToString());
        }
        private static void RecordState(Animator animator, AnimatorStateInfo state)
        {
            if (_statesSeen.Count >= 12) return;
            _statesSeen.Add(HatchLog.I(state.shortNameHash) + "=" + ClipDescription(animator)
                            + (IsNormalPassageState(state.shortNameHash) ? "*" : ""));
        }
        private static string ClipDescription(Animator animator)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0) return "'none'";
            var clip = clips[0].clip;
            if (clip == null) return "'null'";
            return "'" + clip.name + "'/len=" + HatchLog.F(clip.length, 2)
                   + "/loop=" + HatchLog.B(clip.isLooping)
                   + (clips.Length > 1 ? "/+" + HatchLog.I(clips.Length - 1) : "");
        }
        private static bool IsNormalPassageState(int shortNameHash)
        {
            var arr = HatchNormalStates == null ? null : HatchNormalStates.GetValue(null) as int[];
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++)
                if (arr[i] == shortNameHash) return true;
            return false;
        }
        private static string HandlerName(NavMeshNavigator navigator)
        {
            var handler = NavLinkHandler == null ? null : NavLinkHandler.GetValue(navigator);
            if (handler == null) return "none";
            return handler.GetType().Name;
        }
        private static string BoolParam(Animator animator, FieldInfo hashField)
        {
            if (hashField == null) return "?";
            return HatchLog.B(animator.GetBool((int)hashField.GetValue(null)));
        }
        private static string IntParam(Animator animator, FieldInfo hashField)
        {
            if (hashField == null) return "?";
            return HatchLog.I(animator.GetInteger((int)hashField.GetValue(null)));
        }
        private static string HatchBool(Animator animator, NavMeshHatch hatch)
        {
            if (hatch == null || HatchParamHash == null) return "-";
            return HatchLog.B(animator.GetBool((int)HatchParamHash.GetValue(hatch)));
        }
        private static float ReadFloat(FieldInfo field, object target)
        {
            if (field == null) return float.NaN;
            return (float)field.GetValue(target);
        }
        private static bool ReadBool(FieldInfo field, object target)
        {
            if (field == null) return false;
            return (bool)field.GetValue(target);
        }
        private static int ListCount(FieldInfo field, object target)
        {
            if (field == null) return -1;
            var list = field.GetValue(target) as System.Collections.ICollection;
            return list == null ? -1 : list.Count;
        }
        private static string RunInputPressed(FPPCameraController controller)
        {
            if (FppRunInput == null) return "?";
            var link = FppRunInput.GetValue(controller);
            if (link == null) return "?";
            var assetProperty = link.GetType().GetProperty("Asset");
            if (assetProperty == null) return "?";
            var axis = assetProperty.GetValue(link, null);
            if (axis == null) return "?";
            var pressedProperty = axis.GetType().GetProperty("Pressed");
            if (pressedProperty == null) return "?";
            return HatchLog.B((bool)pressedProperty.GetValue(axis, null));
        }
    }

}

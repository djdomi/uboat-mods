using DWS.Common.Resources;
using DWS.Common;
using Debug = UnityEngine.Debug;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Core.Serialization;
using UBOAT.Game.Core;
using UBOAT.Game.Scene.Camera.DeviceControllers;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Effects;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Spaces;
using UBOAT.Game.Scene;
using UBOAT.Game.UI;
using UnityEngine;

namespace UboatToolkit.Modules.StationCtl
{
    // Instrument stations (hydrophone, TDC, torpedo bay, gyro receiver, echosounder ...):
    // wheel zoom toward the cursor, first-person style look (Pan) or turn (Rotate), free cursor
    // key, centre crosshair, direct dial dragging with Shift fine-adjust, no scroll changes on
    // dials, tooltips/readouts hidden while locked, live Angle-on-Bow dial.
    internal enum LookMode { Pan, Rotate }
    internal enum LookResponse { Translate, Rotate }

    internal static class StationClock
    {
        public static long Now() => (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        public static double MsSince(long t) => Now() - t;
    }

    internal static class StationCfg
    {
        public static bool Active => Settings.B("StationZoom");
        public static LookMode Mode => Mathf.RoundToInt(Settings.F("StLookMode")) == 1 ? LookMode.Rotate : LookMode.Pan;
        public static KeyCode CursorToggleKey => Keys.Code("KeyStationCursor", KeyCode.LeftAlt);
        public static KeyCode PanKey => Keys.Code("KeyStationDragLook", KeyCode.Mouse3);
        public static float PanSpeed => Mathf.Clamp(Settings.F("StPanSpeed"), 0.05f, 10f);
        public static float MaxPan => Mathf.Clamp(Settings.F("StMaxPan"), 0f, 5f);
        private static LookResponse R(string k) => Mathf.RoundToInt(Settings.F(k)) == 1 ? LookResponse.Rotate : LookResponse.Translate;
        public static LookResponse HydrophoneLook => R("StHydroLook");
        public static LookResponse TdcLook => R("StTdcLook");
        public static LookResponse TorpedoStationLook => R("StTorpLook");
        public static LookResponse GyroReceiverLook => R("StGyroLook");
        public static LookResponse DeviceStationLook => R("StOtherLook");
        public static float TdcPanScaleX => Mathf.Clamp(Settings.F("StTdcX"), 0f, 3f);
        public static float TdcPanScaleY => Mathf.Clamp(Settings.F("StTdcY"), 0f, 3f);
        public static float GyroReceiverPanScaleX => Mathf.Clamp(Settings.F("StGyroX"), 0f, 3f);
        public static float GyroReceiverPanScaleY => Mathf.Clamp(Settings.F("StGyroY"), 0f, 3f);
        public static float TorpedoBayForwardOffset => Mathf.Clamp(Settings.F("StTorpForward"), -2f, 2f);
        public static float MaxLookDegrees => Mathf.Clamp(Settings.F("StMaxLook"), 0f, 90f);
        public static bool HideBearingReadoutUntilCursorFreed => Settings.B("StHideReadout");
        public static bool AobRealtimeDrag => Settings.B("StAobLive");
        public static float AobSlewDegreesPerSecond => Mathf.Clamp(Settings.F("StAobSpeed"), 0f, 2000f);
        public static bool HideTooltipsUntilCursorFreed => Settings.B("StHideTooltips");
        public static float MinFov => Mathf.Clamp(Settings.F("StMinFov"), 5f, 90f);
        public static bool EnableCustomDialControl => Settings.B("StDialDirect");
        public static float ScreenFractionForFullRange => Mathf.Clamp(Settings.F("StDialRange"), 0.02f, 3f);
        public static float ShiftSensitivityScale => Mathf.Clamp(Settings.F("StDialFine"), 0.005f, 3f);
        public static bool DebugLogging => Settings.B("StationLog");
        public static void DebugLog(string m) { if (DebugLogging) Util.Log("Stationen: " + m); }
    }

    // Creates the crosshair once and gives the lens back when the feature is switched off.
    public class StationHost : ModuleBase
    {
        private bool created, last;
        public override void Tick()
        {
            if (!created) { created = true; StationCrosshair.Create(); }
            bool on = StationCfg.Active;
            if (last && !on) { DeviceStationZoomPatch.RestoreFov(); HydrophoneZoomPatch.RestoreFov(); }
            last = on;
        }
    }

    [HarmonyPatch]
    class StationCursorZoomPatch
    {
        internal static bool IsSupportedController(IExternalController controller)
        {
            return controller is HydrophoneController || controller is BasicDeviceController;
        }
        static float GetTargetFov(IExternalController controller)
        {
            if (controller is HydrophoneController) return HydrophoneZoomPatch.TargetFov;
            if (controller is BasicDeviceController) return DeviceStationZoomPatch.TargetFov;
            return -1f;
        }
        static bool GetIsScrollingThisFrame(IExternalController controller)
        {
            if (controller is HydrophoneController) return HydrophoneZoomPatch.IsScrollingThisFrame;
            if (controller is BasicDeviceController) return DeviceStationZoomPatch.IsScrollingThisFrame;
            return false;
        }
        static bool GetIsZoomingIn(IExternalController controller)
        {
            if (controller is HydrophoneController) return HydrophoneZoomPatch.IsZoomingIn;
            if (controller is BasicDeviceController) return DeviceStationZoomPatch.IsZoomingIn;
            return false;
        }
        internal static LookResponse ResponseFor(IExternalController controller)
        {
            if (controller is HydrophoneController)
                return StationCfg.HydrophoneLook;
            if (controller is GyroAngleReceiverController)
                return StationCfg.GyroReceiverLook;
            if (controller is TorpedoCalculatorController)
                return StationCfg.TdcLook;
            if (controller is TorpedoLauncherController
                || controller is TorpedoFireControlBoxesController)
                return StationCfg.TorpedoStationLook;
            return StationCfg.DeviceStationLook;
        }
        static void PanScaleFor(IExternalController controller, out float scaleX, out float scaleY)
        {
            if (controller is TorpedoCalculatorController)
            {
                scaleX = StationCfg.TdcPanScaleX;
                scaleY = StationCfg.TdcPanScaleY;
                return;
            }
            if (controller is GyroAngleReceiverController)
            {
                scaleX = StationCfg.GyroReceiverPanScaleX;
                scaleY = StationCfg.GyroReceiverPanScaleY;
                return;
            }
            scaleX = 1f;
            scaleY = 1f;
        }
        static float DollyFor(IExternalController controller)
        {
            if (controller is TorpedoLauncherController)
                return StationCfg.TorpedoBayForwardOffset;
            return 0f;
        }
        internal static bool IsAtSupportedStation()
        {
            MainCamera mc = MainCamera.Instance;
            if (mc == null || mc.CurrentMode != CameraMode.Generic)
                return false;
            return IsSupportedController(mc.GenericController.ExternalController);
        }
        const float MaxLookOffsetDegrees = 55f;
        const float DragSensitivityDegreesPerScreen = 90f;
        const float EdgePanZonePixels = 60f;
        const float EdgePanDegreesPerSecond = 60f;
        const float MinPanelDistance = 0.15f;
        const float MaxPanelDistance = 3f;
        static readonly ResourceLink<InputAxis> horizontalLookAxis = new ResourceLink<InputAxis>("Inputs/Horizontal Look");
        static readonly ResourceLink<InputAxis> verticalLookAxis = new ResourceLink<InputAxis>("Inputs/Vertical Look");
        static readonly AccessTools.FieldRef<VirtualSceneRenderer, int> overrideFrameIndexRef =
            AccessTools.FieldRefAccess<VirtualSceneRenderer, int>("overrideFrameIndex");
        static readonly AccessTools.FieldRef<VirtualSceneRenderer, VirtualScene> virtualSceneOverrideRef =
            AccessTools.FieldRefAccess<VirtualSceneRenderer, VirtualScene>("virtualSceneOverride");
        static float initialFov = -1f;
        static float currentZoomDepth = 0f;
        static float accumulatedYaw = 0f;
        static float accumulatedPitch = 0f;
        static Vector3 lastMousePosition;
        static Camera cachedCamera;
        static bool lockActive;
        internal static bool LockActive
        {
            get { return lockActive; }
            private set
            {
                if (lockActive == value) return;
                lockActive = value;
                StationCrosshair.SetVisible(value);
            }
        }
        static bool cursorReleased;
        internal static bool CursorReleased { get { return cursorReleased; } }
        static bool wasLockedLastFrame;
        static Vector2 pendingLookDegrees;
        static Vector2 panMeters;
        static bool loggedPanFailure;
        [HarmonyPatch(typeof(GenericCameraController), "StartController"), HarmonyPostfix]
        static void ResetOnEnter(IExternalController controller)
        {
            cachedCamera = MainCamera.Instance ? MainCamera.Instance.GetComponent<Camera>() : null;
            initialFov = (IsSupportedController(controller) && cachedCamera != null) ? cachedCamera.fieldOfView : -1f;
            currentZoomDepth = 0f;
            accumulatedYaw = 0f;
            accumulatedPitch = 0f;
            lastMousePosition = Input.mousePosition;
            cursorReleased = false;
            wasLockedLastFrame = false;
            pendingLookDegrees = Vector2.zero;
            panMeters = Vector2.zero;
            LockActive = false;
            TooltipSuppressionPatch.Forget();
        }
        [HarmonyPatch(typeof(GenericCameraController), "StopController"), HarmonyPostfix]
        static void ReleaseOnExit()
        {
            LockActive = false;
            wasLockedLastFrame = false;
            HydrophoneBearingUiPatch.Restore();
        }
        [HarmonyPatch(typeof(CursorManager), "CursorHidden", MethodType.Setter), HarmonyPrefix]
        static void ForceLockWhilePanning(ref bool value)
        {
            if (StationCfg.Active && LockActive && IsAtSupportedStation())
                value = true;
        }
        [HarmonyPatch(typeof(GenericCameraController), "Update"), HarmonyPrefix]
        static void SuppressScrollExitWhileZooming(GenericCameraController __instance)
        {
            if (!StationCfg.Active) return;
            if (__instance.ExternalController != null && IsSupportedController(__instance.ExternalController))
                __instance.MainCamera.Zoom = 0f;
        }
        [HarmonyPatch(typeof(GenericCameraController), "Update"), HarmonyPostfix]
        static void UpdatePanInput(GenericCameraController __instance, GameInput ___gameInput, UserSettings ___userSettings)
        {
            IExternalController controller = __instance.ExternalController;
            if (!StationCfg.Active || StationCfg.Mode != LookMode.Pan || controller == null || !IsSupportedController(controller)
                || __instance.MainCamera.CurrentMode != CameraMode.Generic)
            {
                LockActive = false;
                wasLockedLastFrame = false;
                return;
            }
            bool blocked = ___gameInput != null && ___gameInput.HasActiveBlockades();
            if (!blocked && Input.GetKeyDown(StationCfg.CursorToggleKey))
            {
                cursorReleased = !cursorReleased;
                if (!cursorReleased && StationCfg.HideTooltipsUntilCursorFreed)
                    TooltipSuppressionPatch.CloseOpenTooltips();
                StationCfg.DebugLog("Cursor " + (cursorReleased ? "released" : "locked"));
            }
            bool dragging = Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0);
            LockActive = !cursorReleased && !dragging && !blocked && !__instance.IsFullInterfaceVisible;
            bool lockedNow = LockActive && Cursor.lockState == CursorLockMode.Locked;
            if (lockedNow && wasLockedLastFrame)
            {
                ControlSettings settings = ___userSettings != null ? ___userSettings.ControlSettings : null;
                float sensitivity = settings != null ? settings.cameraLookSensitivity : 1f;
                InputAxis h = horizontalLookAxis.Asset;
                InputAxis v = verticalLookAxis.Asset;
                float x = h != null ? h.Value * sensitivity : 0f;
                float y = v != null ? v.Value * sensitivity : 0f;
                if (settings != null && settings.invertMouse)
                    y = -y;
                pendingLookDegrees += new Vector2(x, y);
            }
            wasLockedLastFrame = lockedNow;
        }
        [HarmonyPatch(typeof(GenericCameraController), "Update"), HarmonyPostfix]
        static void ForceCloseExitIfBlockedByHoverExemption(GenericCameraController __instance, PlayableCharacter ___character, GameInput ___gameInput)
        {
            if (!StationCfg.Active) return;
            IExternalController controller = __instance.ExternalController;
            if (controller == null || !IsSupportedController(controller))
                return;
            if (__instance.ExternalController != controller)
                return;
            bool closeFromGameInput = ___gameInput != null && ___gameInput.ClosePressed();
            bool closePressed = closeFromGameInput || Input.GetKeyDown(KeyCode.Escape);
            if (!closePressed)
                return;
            bool blocked = ___gameInput != null && ___gameInput.HasActiveBlockades();
            StationCfg.DebugLog("close pressed at station: gameInput=" + (___gameInput == null ? "NULL" : "ok")
                + ", fromGameInput=" + closeFromGameInput + ", escapeKey=" + Input.GetKeyDown(KeyCode.Escape)
                + ", blockades=" + blocked + ", station=" + controller.GetType().Name);
            if (blocked)
                return;
            __instance.MainCamera.DisruptZoom();
            if (!controller.OnUserExitRequest())
            {
                StationCfg.DebugLog("station refused the exit request (OnUserExitRequest false)");
                return;
            }
            if (__instance.MainCamera.NonMapFallbackMode == CameraMode.FPP)
                __instance.MainCamera.SetFPPController(___character, CameraTransitionType.FadeInOut, true, 1f);
            else
                __instance.MainCamera.SetMode(__instance.MainCamera.NonMapFallbackModeDescriptor, CameraTransitionType.FadeInOut, 1f);
            StationCfg.DebugLog("forced station exit");
        }
        static float EdgePanFactor(float coordinate, float axisLength)
        {
            if (coordinate > axisLength - EdgePanZonePixels)
                return Mathf.Clamp01((coordinate - (axisLength - EdgePanZonePixels)) / EdgePanZonePixels);
            if (coordinate < EdgePanZonePixels)
                return -Mathf.Clamp01((EdgePanZonePixels - coordinate) / EdgePanZonePixels);
            return 0f;
        }
        [HarmonyPatch(typeof(GenericCameraController), "LateUpdate"), HarmonyPostfix]
        static void ApplyCursorOffset(GenericCameraController __instance, VirtualSceneTransform ___currentTargetA, float ___targetsBlendFactor)
        {
            if (!StationCfg.Active) return;
            IExternalController controller = __instance.ExternalController;
            if (controller == null || !IsSupportedController(controller) || initialFov <= 0f || cachedCamera == null)
                return;
            if (StationCfg.Mode == LookMode.Pan)
                ApplyPan(__instance, controller, ___currentTargetA, ___targetsBlendFactor);
            else
                ApplyRotate(__instance, controller);
        }
        static void ApplyPan(GenericCameraController generic, IExternalController controller, VirtualSceneTransform targetA, float blendFactor)
        {
            if (targetA == null || blendFactor != 0f)
            {
                pendingLookDegrees = Vector2.zero;
                return;
            }
            Camera camera = cachedCamera;
            float dolly = DollyFor(controller);
            LookResponse response = ResponseFor(controller);
            if (response == LookResponse.Rotate)
            {
                ApplyOffset(generic, camera, new Vector3(0f, 0f, dolly));
                ApplyLookRotation(camera);
                return;
            }
            float panelDistance = Mathf.Clamp((controller.DOFTarget - targetA.position).magnitude, MinPanelDistance, MaxPanelDistance);
            float entryHalfTan = Mathf.Tan(initialFov * 0.5f * Mathf.Deg2Rad);
            float zoomScale = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) / entryHalfTan;
            float metersPerDegree = panelDistance * Mathf.Deg2Rad * zoomScale * StationCfg.PanSpeed;
            panMeters += pendingLookDegrees * metersPerDegree;
            pendingLookDegrees = Vector2.zero;
            float scaleX, scaleY;
            PanScaleFor(controller, out scaleX, out scaleY);
            float maxY = StationCfg.MaxPan * panelDistance * entryHalfTan * scaleY;
            float maxX = StationCfg.MaxPan * panelDistance * entryHalfTan * camera.aspect * scaleX;
            panMeters.x = Mathf.Clamp(panMeters.x, -maxX, maxX);
            panMeters.y = Mathf.Clamp(panMeters.y, -maxY, maxY);
            ApplyOffset(generic, camera, new Vector3(panMeters.x, panMeters.y, dolly));
        }
        static void ApplyOffset(GenericCameraController generic, Camera camera, Vector3 localOffset)
        {
            if (localOffset == Vector3.zero)
                return;
            Transform cameraTransform = camera.transform;
            cameraTransform.position += cameraTransform.rotation * localOffset;
            VirtualSceneRenderer renderer = generic.MainCamera.VirtualSceneRenderer;
            if (renderer == null || renderer.VirtualCamera == null || overrideFrameIndexRef(renderer) != Time.frameCount)
            {
                LogPanFailureOnce("vanilla did not override the interior camera this frame");
                return;
            }
            VirtualScene scene = virtualSceneOverrideRef(renderer);
            if (scene == null || scene.Target == null)
            {
                LogPanFailureOnce("no virtual scene override");
                return;
            }
            Quaternion interiorRotation = Quaternion.Inverse(scene.Target.rotation) * cameraTransform.rotation;
            Vector3 interiorPosition = renderer.VirtualCamera.transform.localPosition + interiorRotation * localOffset;
            renderer.OverrideLocalTransformation(scene, interiorPosition);
        }
        static void ApplyLookRotation(Camera camera)
        {
            float limit = StationCfg.MaxLookDegrees;
            accumulatedYaw = Mathf.Clamp(accumulatedYaw + pendingLookDegrees.x, -limit, limit);
            accumulatedPitch = Mathf.Clamp(accumulatedPitch - pendingLookDegrees.y, -limit, limit);
            pendingLookDegrees = Vector2.zero;
            if (accumulatedYaw == 0f && accumulatedPitch == 0f)
                return;
            camera.transform.rotation = camera.transform.rotation * Quaternion.Euler(accumulatedPitch, accumulatedYaw, 0f);
        }
        static void LogPanFailureOnce(string reason)
        {
            if (loggedPanFailure)
                return;
            loggedPanFailure = true;
            Util.Log("Stationen: Pan mode could not move the interior view: " + reason + ". Further occurrences are not logged.");
        }
        static void ApplyRotate(GenericCameraController generic, IExternalController controller)
        {
            float targetFov = GetTargetFov(controller);
            if (targetFov <= 0f)
                return;
            Camera camera = cachedCamera;
            ApplyOffset(generic, camera, new Vector3(0f, 0f, DollyFor(controller)));
            float targetZoomDepth = Mathf.Clamp01(1f - targetFov / initialFov);
            currentZoomDepth = Mathf.Lerp(currentZoomDepth, targetZoomDepth, Time.unscaledDeltaTime * 12f);
            Vector3 mousePosition = Input.mousePosition;
            Vector3 mouseDelta = mousePosition - lastMousePosition;
            lastMousePosition = mousePosition;
            if (Input.GetKey(StationCfg.PanKey) && currentZoomDepth > 0.02f)
            {
                accumulatedYaw += (mouseDelta.x / Screen.width) * DragSensitivityDegreesPerScreen;
                accumulatedPitch += -(mouseDelta.y / Screen.height) * DragSensitivityDegreesPerScreen;
                float edgeX = EdgePanFactor(mousePosition.x, Screen.width);
                float edgeY = EdgePanFactor(mousePosition.y, Screen.height);
                accumulatedYaw += edgeX * EdgePanDegreesPerSecond * Time.unscaledDeltaTime;
                accumulatedPitch += -edgeY * EdgePanDegreesPerSecond * Time.unscaledDeltaTime;
                accumulatedYaw = Mathf.Clamp(accumulatedYaw, -MaxLookOffsetDegrees, MaxLookOffsetDegrees);
                accumulatedPitch = Mathf.Clamp(accumulatedPitch, -MaxLookOffsetDegrees, MaxLookOffsetDegrees);
            }
            else if (GetIsScrollingThisFrame(controller) && GetIsZoomingIn(controller))
            {
                float cursorX = (mousePosition.x / Screen.width) - 0.5f;
                float cursorY = (mousePosition.y / Screen.height) - 0.5f;
                float t = Time.unscaledDeltaTime * 12f;
                accumulatedYaw = Mathf.Lerp(accumulatedYaw, cursorX * MaxLookOffsetDegrees, t);
                accumulatedPitch = Mathf.Lerp(accumulatedPitch, -cursorY * MaxLookOffsetDegrees, t);
            }
            if (currentZoomDepth < 0.001f)
            {
                accumulatedYaw = 0f;
                accumulatedPitch = 0f;
            }
            float yawOffset = accumulatedYaw * currentZoomDepth;
            float pitchOffset = accumulatedPitch * currentZoomDepth;
            camera.transform.rotation = camera.transform.rotation * Quaternion.Euler(pitchOffset, yawOffset, 0f);
        }
    }


    [HarmonyPatch(typeof(BasicDeviceController), "UpdateController")]
    class DeviceStationZoomPatch
    {
        const float ZoomSpeed = 40f;
        static float defaultFov = 56f;
        static float currentFov = defaultFov;
        static Camera cachedCamera;
        static bool atStation;
        internal static float TargetFov { get { return currentFov; } }
        internal static bool IsScrollingThisFrame { get; private set; }
        internal static bool IsZoomingIn { get; private set; }
        [HarmonyPatch(typeof(BasicDeviceController), "StartController"), HarmonyPostfix]
        static void ResetOnEnter(BasicDeviceController __instance)
        {
            cachedCamera = Camera.main;
            defaultFov = cachedCamera != null ? cachedCamera.fieldOfView : 56f;
            currentFov = defaultFov;
            atStation = true;
            StationCfg.DebugLog("Entered device station (" + __instance.GetType().Name + "), default fov=" + defaultFov);
        }
        [HarmonyPatch(typeof(BasicDeviceController), "StopController"), HarmonyPostfix]
        static void ForgetOnLeave()
        {
            atStation = false;
            currentFov = defaultFov;
        }
        internal static void RestoreFov()
        {
            currentFov = defaultFov;
            if (!atStation) return;
            WriteFov(defaultFov);
        }
        static void WriteFov(float fov)
        {
            Camera camera = cachedCamera;
            if (camera != null) camera.fieldOfView = fov;
        }
        [HarmonyPrefix]
        static void ApplyScrollZoom(BasicDeviceController __instance)
        {
            if (!StationCfg.Active) { IsScrollingThisFrame = false; return; }
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            float previousFov = currentFov;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                currentFov -= scroll * ZoomSpeed;
                currentFov = Mathf.Clamp(currentFov, StationCfg.MinFov, defaultFov);
                IsZoomingIn = scroll > 0f;
            }
            IsScrollingThisFrame = !Mathf.Approximately(currentFov, previousFov);
            Camera camera = cachedCamera != null ? cachedCamera : (cachedCamera = Camera.main);
            if (camera != null)
                camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, currentFov, Time.unscaledDeltaTime * 12f);
        }
    }


    [HarmonyPatch]
    class HydrophoneZoomPatch
    {
        const float DefaultFov = 65f;
        const float ZoomSpeed = 40f;
        static float currentFov = DefaultFov;
        static Camera cachedCamera;
        static bool atStation;
        internal static float TargetFov { get { return currentFov; } }
        internal static bool IsScrollingThisFrame { get; private set; }
        internal static bool IsZoomingIn { get; private set; }
        [HarmonyPatch(typeof(HydrophoneController), "StartController"), HarmonyPostfix]
        static void ResetZoomOnEnter()
        {
            currentFov = DefaultFov;
            cachedCamera = Camera.main;
            atStation = true;
            StationCfg.DebugLog("Hydrophone station entered, camera cached.");
        }
        [HarmonyPatch(typeof(HydrophoneController), "StopController"), HarmonyPostfix]
        static void ForgetOnLeave()
        {
            atStation = false;
            currentFov = DefaultFov;
        }
        internal static void RestoreFov()
        {
            currentFov = DefaultFov;
            if (!atStation) return;
            WriteFov(DefaultFov);
        }
        static void WriteFov(float fov)
        {
            Camera camera = cachedCamera;
            if (camera != null) camera.fieldOfView = fov;
        }
        [HarmonyPatch(typeof(HydrophoneController), "UpdateController"), HarmonyPrefix]
        static void ApplyScrollZoom()
        {
            if (!StationCfg.Active) { IsScrollingThisFrame = false; return; }
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            float previousFov = currentFov;
            if (Mathf.Abs(scroll) > 0.001f)
            {
                currentFov -= scroll * ZoomSpeed;
                currentFov = Mathf.Clamp(currentFov, StationCfg.MinFov, DefaultFov);
                IsZoomingIn = scroll > 0f;
            }
            IsScrollingThisFrame = !Mathf.Approximately(currentFov, previousFov);
            Camera camera = cachedCamera != null ? cachedCamera : (cachedCamera = Camera.main);
            if (camera != null)
                camera.fieldOfView = Mathf.Lerp(camera.fieldOfView, currentFov, Time.unscaledDeltaTime * 12f);
        }
    }


    [HarmonyPatch(typeof(Dial), "OnPointerDragUpdate")]
    class DialDragPatch
    {
        const float AngleSmoothingRate = 20f;
        const float MinRadiusPixels = 40f;
        const float MaxDeltaAnglePerFrame = 45f;
        static readonly FieldInfo dragModeField = AccessTools.Field(typeof(Dial), "dragMode");
        static readonly MethodInfo applyDeltaMethod = AccessTools.Method(typeof(Dial), "ApplyDelta");
        const int DragModeRotate = 1;
        static readonly ConditionalWeakTable<Dial, StrongBox<float>> smoothedAngles = new ConditionalWeakTable<Dial, StrongBox<float>>();
        [HarmonyPrefix]
        static bool ReplaceDragUpdate(Dial __instance)
        {
            if (!StationCfg.Active || !StationCfg.EnableCustomDialControl)
                return true;
            if (!Input.GetMouseButton(0))
            {
                smoothedAngles.Remove(__instance);
                return true;
            }
            if (dragModeField == null || applyDeltaMethod == null)
                return true;
            int dragMode = (int)dragModeField.GetValue(__instance);
            if (dragMode != DragModeRotate)
                return true;
            Camera camera = MainCamera.Instance ? MainCamera.Instance.InteriorCamera.Camera : null;
            if (camera == null)
                return true;
            Vector2 dialScreenPos = camera.WorldToScreenPointNonJittered(__instance.transform.position);
            Vector2 mouseScreenPos = Input.mousePosition;
            Vector2 offset = mouseScreenPos - dialScreenPos;
            float rawAngle = Mathf.Atan2(offset.x, offset.y) * Mathf.Rad2Deg;
            StrongBox<float> smoothed;
            if (!smoothedAngles.TryGetValue(__instance, out smoothed))
            {
                smoothedAngles.Add(__instance, new StrongBox<float>(rawAngle));
                return false;
            }
            float previousSmoothed = smoothed.Value;
            float newSmoothed = Mathf.LerpAngle(previousSmoothed, rawAngle, Time.unscaledDeltaTime * AngleSmoothingRate);
            smoothed.Value = newSmoothed;
            float radius = Mathf.Max(offset.magnitude, MinRadiusPixels);
            float clampedDeltaAngle = Mathf.Clamp(Mathf.DeltaAngle(previousSmoothed, newSmoothed), -MaxDeltaAnglePerFrame, MaxDeltaAnglePerFrame);
            float pixelDelta = clampedDeltaAngle * Mathf.Deg2Rad * radius;
            float pixelsForFullRange = Screen.height * StationCfg.ScreenFractionForFullRange;
            if (pixelsForFullRange < 1f)
                return false;
            float normalizedDelta = pixelDelta / pixelsForFullRange;
            if (Input.GetKey(KeyCode.LeftShift))
                normalizedDelta *= StationCfg.ShiftSensitivityScale;
            applyDeltaMethod.Invoke(__instance, new object[] { normalizedDelta });
            return false;
        }
    }


    [HarmonyPatch(typeof(Dial), "OnPointerEnterUpdate")]
    class DialScrollSuppressionPatch
    {
        static readonly AccessTools.FieldRef<Dial, float> sensitivityRef =
            AccessTools.FieldRefAccess<Dial, float>("sensitivity");
        static bool IsAtSupportedStation()
        {
            MainCamera mc = MainCamera.Instance;
            if (mc == null || mc.CurrentMode != CameraMode.Generic)
                return false;
            IExternalController controller = mc.GenericController.ExternalController;
            return StationCursorZoomPatch.IsSupportedController(controller);
        }
        [HarmonyPrefix]
        static void SuppressScrollAtStations(Dial __instance, out float __state)
        {
            __state = float.NaN;
            if (!StationCfg.Active || !IsAtSupportedStation())
                return;
            __state = sensitivityRef(__instance);
            sensitivityRef(__instance) = 0f;
        }
        [HarmonyFinalizer]
        static Exception RestoreSensitivity(Dial __instance, float __state, Exception __exception)
        {
            if (!float.IsNaN(__state) && __instance != null)
                sensitivityRef(__instance) = __state;
            return __exception;
        }
    }


    [HarmonyPatch]
    class TooltipSuppressionPatch
    {
        static readonly Type[] TooltipTypes =
        {
            typeof(Dial), typeof(PresetDial), typeof(InterpolatedPresetDial), typeof(HydrophoneCompensatorFrequencySwitch)
        };
        static readonly FieldInfo userSettingsField = AccessTools.Field(typeof(Dial), "userSettings");
        static readonly HashSet<Component> hovered = new HashSet<Component>();
        static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (Type t in TooltipTypes)
            {
                MethodInfo m = AccessTools.Method(t, "OnPointerEnter");
                if (m != null)
                    yield return m;
                else
                    Util.Log("Stationen: " + t.Name + ".OnPointerEnter not found - its tooltip will not be hidden.");
            }
        }
        static bool ShouldHide()
        {
            return StationCfg.Active
                && StationCfg.HideTooltipsUntilCursorFreed
                && StationCfg.Mode == LookMode.Pan
                && !StationCursorZoomPatch.CursorReleased
                && StationCursorZoomPatch.IsAtSupportedStation();
        }
        static ControlSettings GetControlSettings()
        {
            UserSettings settings = userSettingsField != null ? userSettingsField.GetValue(null) as UserSettings : null;
            return settings != null ? settings.ControlSettings : null;
        }
        [HarmonyPrefix]
        static void HideWhileLocked(object __instance, ref bool __state)
        {
            __state = false;
            if (!StationCursorZoomPatch.IsAtSupportedStation())
                return;
            Component component = __instance as Component;
            if (component != null)
                hovered.Add(component);
            if (!ShouldHide())
                return;
            ControlSettings controls = GetControlSettings();
            if (controls != null && !controls.disableDeviceDialsTooltips)
            {
                controls.disableDeviceDialsTooltips = true;
                __state = true;
            }
        }
        [HarmonyFinalizer]
        static Exception Restore(bool __state, Exception __exception)
        {
            if (__state)
            {
                ControlSettings controls = GetControlSettings();
                if (controls != null)
                    controls.disableDeviceDialsTooltips = false;
            }
            return __exception;
        }
        internal static void Forget()
        {
            hovered.Clear();
        }
        internal static void CloseOpenTooltips()
        {
            foreach (Component component in hovered)
            {
                if (component == null)
                    continue;
                try
                {
                    FieldInfo field = AccessTools.Field(component.GetType(), "tooltip");
                    if (field == null)
                        continue;
                    object tooltip = field.GetValue(component);
                    if (tooltip == null)
                        continue;
                    MethodInfo close = field.FieldType.GetMethod("Close", Type.EmptyTypes);
                    if (close != null)
                        close.Invoke(tooltip, null);
                    field.SetValue(component, null);
                }
                catch (Exception e)
                {
                    StationCfg.DebugLog("Could not close tooltip on " + component.GetType().Name + ": " + e.Message);
                }
            }
            hovered.Clear();
        }
    }


    [HarmonyPatch(typeof(HydrophoneUI), "Update")]
    class HydrophoneBearingUiPatch
    {
        static readonly FieldInfo bearingLabelField = AccessTools.Field(typeof(HydrophoneUI), "bearingLabel");
        static readonly FieldInfo surfaceWarningField = AccessTools.Field(typeof(HydrophoneUI), "surfaceWarning");
        static GameObject hiddenLabel;
        static GameObject hiddenWarning;
        static bool loggedLayout;
        static bool lastHide;
        static bool loggedState;
        static bool ShouldHide()
        {
            return StationCfg.Active
                && StationCfg.HideBearingReadoutUntilCursorFreed
                && StationCfg.Mode == LookMode.Pan
                && !StationCursorZoomPatch.CursorReleased
                && StationCursorZoomPatch.IsAtSupportedStation();
        }
        static string Describe(GameObject go)
        {
            if (go == null)
                return "<null>";
            string parent = go.transform.parent != null ? go.transform.parent.name : "<none>";
            string grand = (go.transform.parent != null && go.transform.parent.parent != null) ? go.transform.parent.parent.name : "<none>";
            return go.name + " (active=" + go.activeSelf + ", parent=" + parent + ", grandparent=" + grand + ")";
        }
        [HarmonyPostfix]
        static void ToggleHydrophoneHelpers(object __instance)
        {
            Component label = bearingLabelField != null ? bearingLabelField.GetValue(__instance) as Component : null;
            GameObject labelGo = null;
            if (label != null)
            {
                GameObject root = __instance is Component ? ((Component)__instance).gameObject : null;
                Transform parent = label.transform.parent;
                labelGo = (parent != null && parent.gameObject != root) ? parent.gameObject : label.gameObject;
            }
            GameObject warningGo = surfaceWarningField != null ? surfaceWarningField.GetValue(__instance) as GameObject : null;
            if (!loggedLayout)
            {
                loggedLayout = true;
                StationCfg.DebugLog("HydrophoneUI helpers found: bearingLabel=" + Describe(labelGo)
                    + ", surfaceWarning=" + Describe(warningGo)
                    + ", root=" + (__instance is Component ? ((Component)__instance).gameObject.name : "?"));
            }
            bool hide = ShouldHide();
            if (hide != lastHide || !loggedState)
            {
                lastHide = hide;
                loggedState = true;
                StationCfg.DebugLog("hydrophone helpers " + (hide ? "hidden" : "shown")
                    + " (mode=" + StationCfg.Mode + ", cursorReleased=" + StationCursorZoomPatch.CursorReleased
                    + ", atStation=" + StationCursorZoomPatch.IsAtSupportedStation()
                    + ", enabled=" + StationCfg.HideBearingReadoutUntilCursorFreed + ")");
            }
            Apply(labelGo, hide, ref hiddenLabel, true);
            Apply(warningGo, hide, ref hiddenWarning, false);
        }
        static void Apply(GameObject go, bool hide, ref GameObject tracked, bool restoreOnShow)
        {
            if (go == null)
                return;
            if (hide)
            {
                if (go.activeSelf)
                    go.SetActive(false);
                tracked = go;
            }
            else if (tracked != null)
            {
                if (restoreOnShow && !tracked.activeSelf)
                    tracked.SetActive(true);
                tracked = null;
            }
        }
        internal static void Restore()
        {
            if (hiddenLabel != null) { hiddenLabel.SetActive(true); hiddenLabel = null; }
            if (hiddenWarning != null) { hiddenWarning.SetActive(true); hiddenWarning = null; }
            loggedState = false;
        }
    }


    [HarmonyPatch(typeof(TorpedoCalculatorAngleOnBowDial))]
    class AobRealtimeDragPatch
    {
        const float MinDegreesBetweenCommits = 0.005f;
        const long SlowSolverMs = 3;
        static readonly MethodInfo computeImpactAngle = AccessTools.Method(typeof(TorpedoCalculator), "ComputeImpactAngleForDesiredAOB");
        static readonly MethodInfo getImpactAngle = AccessTools.PropertyGetter(typeof(TorpedoCalculator), "ImpactAngle");
        static readonly MethodInfo setDesiredImpactAngle = AccessTools.PropertySetter(typeof(TorpedoCalculator), "DesiredImpactAngle");
        static readonly MethodInfo getLocking = AccessTools.PropertyGetter(typeof(TorpedoCalculator), "IsLockingMechanismTriggered");
        static readonly MethodInfo getApplyCourseChanges = AccessTools.PropertyGetter(typeof(TorpedoCalculator), "ApplyCourseChanges");
        static readonly FieldInfo restoreFlagField = AccessTools.Field(typeof(TorpedoCalculator), "restoreApplyCourseChangesFlagAfterAOBChange");
        static readonly FieldInfo impactAngleChangeSpeedField = AccessTools.Field(typeof(TorpedoCalculator), "impactAngleChangeSpeed");
        static readonly FieldInfo aobCorrectionSpeedField = AccessTools.Field(typeof(TorpedoCalculator), "aobCorrectionSpeed");
        static bool dragging;
        static bool applyCourseChangesAtDragStart;
        static float lastCommittedTarget;
        static float lastCommitTime;
        static bool reportedSlowThisDrag;
        static bool warnedMissingMember;
        static bool Ready()
        {
            if (computeImpactAngle != null && getImpactAngle != null && setDesiredImpactAngle != null && restoreFlagField != null)
                return true;
            if (!warnedMissingMember)
            {
                warnedMissingMember = true;
                Util.Log("Stationen: TorpedoCalculator members for live AOB dragging not found - leaving vanilla ghost behaviour alone.");
            }
            return false;
        }
        static float Angle180(float degrees)
        {
            degrees = degrees % 360f;
            if (degrees > 180f) degrees -= 360f;
            else if (degrees < -180f) degrees += 360f;
            return degrees;
        }
        [HarmonyPatch("OnDragStart"), HarmonyPostfix]
        static void BeginDrag(TorpedoCalculator ___calculator, float ___targetAngleOnBow, Transform ___ghostPointer)
        {
            dragging = false;
            reportedSlowThisDrag = false;
            if (!StationCfg.Active || !StationCfg.AobRealtimeDrag || ___calculator == null || !Ready())
                return;
            if (___ghostPointer != null && ___ghostPointer.gameObject.activeSelf)
                ___ghostPointer.gameObject.SetActive(false);
            try
            {
                applyCourseChangesAtDragStart = getApplyCourseChanges != null && (bool)getApplyCourseChanges.Invoke(___calculator, null);
            }
            catch (Exception e)
            {
                StationCfg.DebugLog("could not read ApplyCourseChanges: " + e.Message);
                applyCourseChangesAtDragStart = false;
            }
            lastCommittedTarget = ___targetAngleOnBow;
            lastCommitTime = Time.unscaledTime;
            dragging = true;
        }
        [HarmonyPatch("ApplyDelta"), HarmonyPostfix]
        static void CommitWhileDragging(TorpedoCalculator ___calculator, float ___targetAngleOnBow)
        {
            if (!dragging || !StationCfg.Active || !StationCfg.AobRealtimeDrag || ___calculator == null || !Ready())
                return;
            try
            {
                if (getLocking != null && (bool)getLocking.Invoke(___calculator, null))
                    return;
                if (Mathf.Abs(Angle180(___targetAngleOnBow - lastCommittedTarget)) < MinDegreesBetweenCommits)
                    return;
                Commit(___calculator, ___targetAngleOnBow);
                lastCommitTime = Time.unscaledTime;
            }
            catch (Exception e)
            {
                dragging = false;
                Util.Log("Stationen: live AOB commit failed, reverting to vanilla behaviour for this drag: " + e.Message);
            }
        }
        [HarmonyPatch("OnDragStop"), HarmonyPostfix]
        static void EndDrag(TorpedoCalculator ___calculator)
        {
            if (!dragging)
                return;
            dragging = false;
            BoostMotorSpeed(___calculator);
            SetRestoreFlag(___calculator, applyCourseChangesAtDragStart);
        }
        static void Commit(TorpedoCalculator calculator, float targetAngleOnBow)
        {
            float currentImpactAngle = (float)getImpactAngle.Invoke(calculator, null);
            long watchStart = StationClock.Now();
            object solved = computeImpactAngle.Invoke(calculator, new object[] { (double)Angle180(targetAngleOnBow), currentImpactAngle });
            long solveMs = (long)StationClock.MsSince(watchStart);
            if (solveMs >= SlowSolverMs && !reportedSlowThisDrag)
            {
                reportedSlowThisDrag = true;
                StationCfg.DebugLog("AOB impact-angle solve took " + solveMs + " ms");
            }
            setDesiredImpactAngle.Invoke(calculator, new object[] { solved });
            lastCommittedTarget = targetAngleOnBow;
            BoostMotorSpeed(calculator);
            SetRestoreFlag(calculator, false);
        }
        static void BoostMotorSpeed(TorpedoCalculator calculator)
        {
            float slew = StationCfg.AobSlewDegreesPerSecond;
            if (slew <= 0f)
                return;
            try
            {
                if (impactAngleChangeSpeedField != null)
                    impactAngleChangeSpeedField.SetValue(calculator, slew);
                if (aobCorrectionSpeedField != null)
                    aobCorrectionSpeedField.SetValue(calculator, slew);
            }
            catch (Exception e)
            {
                StationCfg.DebugLog("could not raise AOB motor speed: " + e.Message);
            }
        }
        static void SetRestoreFlag(TorpedoCalculator calculator, bool value)
        {
            if (calculator == null || restoreFlagField == null)
                return;
            try
            {
                restoreFlagField.SetValue(calculator, value);
            }
            catch (Exception e)
            {
                StationCfg.DebugLog("could not restore ApplyCourseChanges flag: " + e.Message);
            }
        }
    }


    [NonSerializedInGameState]
    public class StationCrosshair : MonoBehaviour
    {
        const float OuterSize = 6f;
        const float InnerSize = 3f;
        static StationCrosshair instance;
        static bool drawFailed;
        static MethodInfo drawTexture;
        static PropertyInfo guiColor;
        internal static void Create()
        {
            try
            {
                Type gui = Type.GetType("UnityEngine.GUI, UnityEngine.IMGUIModule");
                if (gui != null)
                {
                    drawTexture = gui.GetMethod("DrawTexture", new Type[] { typeof(Rect), typeof(Texture) });
                    guiColor = gui.GetProperty("color", BindingFlags.Public | BindingFlags.Static);
                }
            }
            catch (Exception e)
            {
                Util.Log("Stationen: Could not resolve UnityEngine.GUI: " + e.Message);
            }
            if (drawTexture == null || guiColor == null)
            {
                Util.Log("Stationen: UnityEngine.GUI unavailable - Pan mode will run without a centre crosshair.");
                return;
            }
            GameObject go = new GameObject("Toolkit Station Crosshair");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<StationCrosshair>();
            instance.enabled = false;
        }
        internal static void SetVisible(bool on)
        {
            if (instance == null || drawFailed) return;
            if (instance.enabled != on) instance.enabled = on;
        }
        void OnGUI()
        {
            if (!StationCfg.Active || !StationCursorZoomPatch.LockActive || Cursor.lockState != CursorLockMode.Locked)
                return;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            try
            {
                object previous = guiColor.GetValue(null, null);
                DrawSquare(cx, cy, OuterSize, new Color(0f, 0f, 0f, 0.6f));
                DrawSquare(cx, cy, InnerSize, new Color(1f, 1f, 1f, 0.9f));
                guiColor.SetValue(null, previous, null);
            }
            catch (Exception e)
            {
                Util.Log("Stationen: Crosshair draw failed, disabling it: " + e.Message);
                drawFailed = true;
                enabled = false;
            }
        }
        static void DrawSquare(float cx, float cy, float size, Color color)
        {
            guiColor.SetValue(null, color, null);
            drawTexture.Invoke(null, new object[] { new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), Texture2D.whiteTexture });
        }
    }

}

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Core.Input;
using UBOAT.Game.Scene;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.UI;
using DWS.Common.InjectionFramework;

namespace UboatToolkit.Modules
{
    // Mouse wheel zoom in first person and in the free camera, smooth free-camera flight
    // (accelerate/brake, bank in turns, smoothed mouse look), hidden cursor in the free camera.
    internal static class CamCfg
    {
        public static bool On => Settings.B("CamWheelZoom");
        public static float FreeFov => Mathf.Clamp(Settings.F("CamFreeFov"), 1f, 150f);
        public static float FppMin => Mathf.Clamp(Settings.F("CamFppMinFov"), 1f, 150f);
        public static float FreeMin => Mathf.Clamp(Settings.F("CamFreeMinFov"), 1f, 150f);
        public static float FreeMax => Mathf.Clamp(Settings.F("CamFreeMaxFov"), 1f, 150f);
        public static float Percent => Mathf.Clamp(Settings.F("CamZoomPercent"), 0.1f, 50f);
        public static float FastMul => Mathf.Clamp(Settings.F("CamFastZoomMul"), 1f, 20f);
        public static bool Smooth => Settings.B("CamSmoothMove");
        public static float Accel => Mathf.Clamp(Settings.F("CamAccel"), 0f, 3f);
        public static float Decel => Mathf.Clamp(Settings.F("CamDecel"), 0f, 3f);
        public static float Bank => Mathf.Clamp(Settings.F("CamBank"), -15f, 15f);
        public static float MinAlt => Mathf.Clamp(Settings.F("CamMinAlt"), 1f, 500f);
        public static float LookSmooth => Mathf.Clamp(Settings.F("CamLookSmooth"), 0f, 2f);
        public static bool HideCursor => Settings.B("CamHideCursor");
        public static bool Protect => Settings.B("CamProtectScripted");
        public static bool Log => Settings.B("CamLog");

        public const float FallbackFpp = 60f;
        private static UserSettings user;
        private static bool userFailed;

        // The first-person field of view the player set in the game's video options.
        public static float GameFppFov()
        {
            if (user == null && !userFailed)
            {
                try { user = ScriptableObjectSingleton.LoadSingleton<UserSettings>(); }
                catch (Exception e) { userFailed = true; Util.Log("Kamera: Spiel-Blickwinkel nicht lesbar, nehme " + FallbackFpp + ": " + e.Message); }
            }
            var v = user != null ? user.VideoSettings : null;
            float fov = v != null ? v.fieldOfViewFPP : 0f;
            return fov > 1f ? fov : FallbackFpp;
        }

        public static void Range(float a, float b, out float lo, out float hi) { lo = Mathf.Min(a, b); hi = Mathf.Max(a, b); }
    }

    [HarmonyPatch(typeof(MainCamera), "Update")]
    internal static class CamZoom_Patch
    {
        private const float FppZoomSpeed = 50f, FppLerp = 12f, FreeLerp = 8f, ForeignHold = 0.5f;
        private static float fppFov = CamCfg.FallbackFpp, freeFov = 70f;
        private static CameraMode lastMode;
        private static Camera cam;
        private static float lastWritten = float.NaN, lastSeen = float.NaN, foreignUntil = -1f;
        private static string holdReason;
        private static bool resetAfterHold;
        private static readonly FieldInfo GameStateF = AccessTools.Field(typeof(MainCamera), "gameState");
        private static readonly FieldInfo GameInputF = AccessTools.Field(typeof(MainCamera), "gameInput");
        private static GameState gameState;
        private static GameInput gameInput;

        private static void Postfix(MainCamera __instance)
        {
            var mode = __instance.CurrentMode;
            if (!CamCfg.On)
            {
                // switched off: give the lens back once if we were the last to write it
                if (!float.IsNaN(lastWritten) && cam != null && (mode == CameraMode.FPP || mode == CameraMode.FreeFly))
                    cam.fieldOfView = mode == CameraMode.FPP ? CamCfg.GameFppFov() : CamCfg.FreeFov;
                lastWritten = lastSeen = float.NaN; holdReason = null;
                fppFov = CamCfg.FallbackFpp; freeFov = CamCfg.FreeFov;
                return;
            }
            if (mode != lastMode)
            {
                lastMode = mode; lastWritten = float.NaN;
                if (mode == CameraMode.FPP) fppFov = CamCfg.GameFppFov();
                if (CamCfg.Log) Util.Log("Kamera: Modus " + mode);
            }
            if (mode != CameraMode.FPP && mode != CameraMode.FreeFly)
            {
                EndHold(false);
                fppFov = CamCfg.FallbackFpp; freeFov = CamCfg.FreeFov; lastWritten = float.NaN;
                return;
            }
            if (cam == null) cam = __instance.GetComponent<Camera>();
            if (cam == null) return;
            string reason = CamCfg.Protect ? SequenceReason(__instance, mode) : null;
            if (reason != null)
            {
                if (holdReason == null && CamCfg.Log) Util.Log("Kamera: halte mich raus: " + reason + " (fov " + cam.fieldOfView.ToString("0.0") + ")");
                holdReason = reason; lastWritten = float.NaN;
                return;
            }
            if (holdReason != null) EndHold(true);
            bool blocked = InputBlocked();
            float scroll = blocked || Keys.Ctrl || Keys.Alt ? 0f : Input.GetAxis("Mouse ScrollWheel");
            bool reset = !blocked && Keys.Down("KeyCamZoomReset");
            if (mode == CameraMode.FPP)
            {
                float game = CamCfg.GameFppFov();
                if (reset) fppFov = game;
                if (Mathf.Abs(scroll) > 0.001f) fppFov -= scroll * FppZoomSpeed;
                CamCfg.Range(CamCfg.FppMin, game, out var lo, out var hi);
                fppFov = Mathf.Clamp(fppFov, lo, hi);
                Write(fppFov, FppLerp);
            }
            else
            {
                if (reset) freeFov = CamCfg.FreeFov;
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    float pct = CamCfg.Percent;
                    if (Keys.Held("KeyCamFastZoom")) pct *= CamCfg.FastMul;
                    freeFov *= Mathf.Exp(-scroll * 10f * pct / 100f);
                    if (CamCfg.Log) Util.Log("Kamera: Rad " + scroll.ToString("0.000") + " -> Ziel " + freeFov.ToString("0.00"));
                }
                CamCfg.Range(CamCfg.FreeMin, CamCfg.FreeMax, out var lo, out var hi);
                freeFov = Mathf.Clamp(freeFov, lo, hi);
                Write(freeFov, FreeLerp);
            }
        }

        // Cutscene, scripted camera, or the game itself driving the lens: stand aside.
        private static string SequenceReason(MainCamera main, CameraMode mode)
        {
            if (gameState == null && GameStateF != null) gameState = GameStateF.GetValue(null) as GameState;
            if (gameState != null && gameState.CutsceneMode) { resetAfterHold = true; return "Zwischensequenz"; }
            if (mode == CameraMode.FreeFly && main.FreeFlyController != null && main.FreeFlyController.StaticPerspective) { resetAfterHold = true; return "Skript-Kamera"; }
            float fov = cam.fieldOfView;
            if (!float.IsNaN(lastWritten) && Mathf.Abs(fov - lastWritten) > 0.05f) foreignUntil = Time.unscaledTime + ForeignHold;
            if (!float.IsNaN(lastSeen) && Mathf.Abs(fov - lastSeen) > 0.001f && Time.unscaledTime < foreignUntil) foreignUntil = Time.unscaledTime + ForeignHold;
            lastSeen = fov;
            return Time.unscaledTime < foreignUntil ? "Spiel steuert den Blickwinkel" : null;
        }

        private static void EndHold(bool log)
        {
            if (holdReason == null) return;
            if (resetAfterHold) { fppFov = CamCfg.GameFppFov(); freeFov = CamCfg.FreeFov; }
            else if (cam != null) { fppFov = cam.fieldOfView; freeFov = cam.fieldOfView; }
            if (log && CamCfg.Log) Util.Log("Kamera: weiter nach " + holdReason);
            holdReason = null; resetAfterHold = false; lastSeen = float.NaN;
        }

        private static void Write(float target, float rate)
        {
            float t = 1f - Mathf.Exp(-Time.unscaledDeltaTime * rate);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, target, t);
            lastWritten = cam.fieldOfView;
        }

        private static bool InputBlocked()
        {
            if (gameInput == null && GameInputF != null) gameInput = GameInputF.GetValue(null) as GameInput;
            return gameInput != null && gameInput.HasActiveBlockades();
        }
    }

    // Free camera: the cursor stays hidden unless a UI window really wants it.
    [HarmonyPatch(typeof(CursorManager), "CursorHidden", MethodType.Setter)]
    internal static class CamZoom_Cursor
    {
        private static bool warned;

        private static void Prefix(ref bool value, HashSet<object> ___cursorVisibilityFlags)
        {
            if (value || !CamCfg.On || !CamCfg.HideCursor) return;
            try
            {
                if (!Application.isFocused) return;
                var main = MainCamera.Instance;
                if (main == null || main.CurrentMode != CameraMode.FreeFly) return;
                if (WantedByUI(___cursorVisibilityFlags)) return;
                value = true;
            }
            catch (Exception e) { if (!warned) { warned = true; Util.Log("Kamera: Mauszeiger nicht ausblendbar: " + e.Message); } }
        }

        private static bool WantedByUI(HashSet<object> flags)
        {
            if (flags == null || flags.Count == 0) return false;
            foreach (var f in flags)
            {
                if (f == null) continue;
                if (f is FreeFlyCameraController) return true;
                var t = f.GetType();
                if (t.Namespace != null && t.Namespace.StartsWith("UBOAT.Game.Scene.Camera")) continue;
                if (CamCfg.Log) Util.Log("Kamera: Mauszeiger bleibt sichtbar für " + t.FullName);
                return true;
            }
            return false;
        }
    }

    // Free camera flight: smooth acceleration/braking, banking, altitude speed floor, smoothed look.
    [HarmonyPatch(typeof(FreeFlyCameraController), "Update")]
    public static class CamFreeFly
    {
        private const float NormalMult = 25f, FastMult = 250f, SlowMult = 4f, BankSmooth = 0.3f;
        private static InputAxis axisH, axisV2, axisV;
        private static Vector3 velocity, velocityRate, localOut;
        private static bool passThrough, stepped, rollApplied, haveYaw;
        private static int lastFrame = -10;
        private static Transform camT;
        private static MainCamera mainCam;
        private static float bank, bankRate, bankApplied, lookRateX, lookRateY, lastYaw;
        private static Quaternion lastLocal;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(InputAxis), "Value");
            MethodInfo[] repl = { AccessTools.Method(typeof(CamFreeFly), "AxisX"), AccessTools.Method(typeof(CamFreeFly), "AxisY"), AccessTools.Method(typeof(CamFreeFly), "AxisZ") };
            var list = new List<CodeInstruction>(instructions);
            var hits = new List<int>();
            for (int i = 0; i < list.Count; i++) if (getter != null && list[i].Calls(getter)) hits.Add(i);
            if (hits.Count != 3) { Util.Log("Kamera: Flug-Glättung nicht eingebaut (" + hits.Count + " statt 3 Achsen)."); return list; }
            for (int k = 0; k < 3; k++) { list[hits[k]].opcode = OpCodes.Call; list[hits[k]].operand = repl[k]; }
            var getAxis = AccessTools.Method(typeof(Input), "GetAxis", new[] { typeof(string) });
            var look = AccessTools.Method(typeof(CamFreeFly), "LookAxis");
            var lookHits = new List<int>();
            for (int i = 0; i < list.Count; i++) if (list[i].Calls(getAxis)) lookHits.Add(i);
            if (lookHits.Count == 3) foreach (int h in lookHits) { list[h].opcode = OpCodes.Call; list[h].operand = look; }
            else Util.Log("Kamera: Umschau-Glättung nicht eingebaut (" + lookHits.Count + " statt 3).");
            return list;
        }

        public static float LookAxis(string axisName)
        {
            float raw = Input.GetAxis(axisName);
            float tau = CamCfg.On ? CamCfg.LookSmooth : 0f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            bool isX = axisName == "Mouse X";
            if (tau <= 0.001f || dt <= 0f) { if (isX) lookRateX = 0f; else lookRateY = 0f; return raw; }
            float k = 1f - Mathf.Exp(-dt / tau);
            if (isX) { lookRateX += (raw / dt - lookRateX) * k; return lookRateX * dt; }
            lookRateY += (raw / dt - lookRateY) * k;
            return lookRateY * dt;
        }

        private static void Prefix()
        {
            stepped = false;
            mainCam = MainCamera.Instance;
            camT = mainCam != null ? mainCam.transform : null;
            if (Time.frameCount - lastFrame > 2) ResetState();
            lastFrame = Time.frameCount;
            if (rollApplied && camT != null)
            {
                // take our bank roll back out before the game reads the rotation
                if (Quaternion.Angle(camT.localRotation, lastLocal) < 0.01f) camT.localRotation = camT.localRotation * Quaternion.AngleAxis(-bankApplied, Vector3.forward);
                rollApplied = false;
            }
        }

        private static void Postfix()
        {
            if (!stepped) { velocity = velocityRate = Vector3.zero; bank = bankRate = 0f; haveYaw = false; lookRateX = lookRateY = 0f; return; }
            ApplyBank();
        }

        public static float AxisX(InputAxis a) { axisH = a; if (!stepped) Step(); return passThrough ? a.Value : localOut.x; }
        public static float AxisY(InputAxis a) { axisV2 = a; if (!stepped) Step(); return passThrough ? a.Value : localOut.y; }
        public static float AxisZ(InputAxis a) { axisV = a; if (!stepped) Step(); return passThrough ? a.Value : localOut.z; }

        private static void Step()
        {
            stepped = true;
            passThrough = !CamCfg.On || !CamCfg.Smooth || camT == null || axisH == null || axisV2 == null || axisV == null;
            if (passThrough) { velocity = velocityRate = Vector3.zero; return; }
            float mult = Input.GetKey(KeyCode.LeftShift) ? FastMult : Input.GetKey(KeyCode.LeftControl) ? SlowMult : NormalMult;
            var input = new Vector3(axisH.Value, axisV2.Value, axisV.Value);
            var target = camT.TransformDirection(input) * mult;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float st = target.sqrMagnitude >= velocity.sqrMagnitude ? CamCfg.Accel : CamCfg.Decel;
            if (st <= 0.001f) { velocity = target; velocityRate = Vector3.zero; }
            else if (dt > 0f) velocity = Vector3.SmoothDamp(velocity, target, ref velocityRate, st, Mathf.Infinity, dt);
            if (input == Vector3.zero && velocity.sqrMagnitude < 1e-4f) velocity = velocityRate = Vector3.zero;
            float comp = 1f;
            if (mainCam != null && !mainCam.IsInsideBoat)
            {
                float y = camT.position.y;
                comp = Mathf.Max(CamCfg.MinAlt, y) / Mathf.Max(1f, y);   // the game slows to a crawl near the water
            }
            localOut = camT.InverseTransformDirection(velocity) / mult * comp;
        }

        private static void ApplyBank()
        {
            float max = CamCfg.On ? CamCfg.Bank : 0f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (camT == null || Mathf.Abs(max) < 0.01f || dt <= 0f) { bank = bankRate = 0f; haveYaw = false; return; }
            float strafe = Mathf.Clamp(camT.InverseTransformDirection(velocity).x / NormalMult, -1f, 1f);
            float yaw = camT.eulerAngles.y;
            float yawRate = haveYaw ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f;
            lastYaw = yaw; haveYaw = true;
            float turn = Mathf.Clamp(yawRate / 90f, -1f, 1f);
            bank = Mathf.SmoothDamp(bank, (strafe * 0.6f + turn * 0.4f) * max, ref bankRate, BankSmooth, Mathf.Infinity, dt);
            if (Mathf.Abs(bank) < 0.001f) return;
            camT.localRotation = camT.localRotation * Quaternion.AngleAxis(bank, Vector3.forward);
            lastLocal = camT.localRotation; bankApplied = bank; rollApplied = true;
        }

        private static void ResetState() { velocity = velocityRate = Vector3.zero; bank = bankRate = 0f; lookRateX = lookRateY = 0f; haveYaw = false; rollApplied = false; }
    }
}

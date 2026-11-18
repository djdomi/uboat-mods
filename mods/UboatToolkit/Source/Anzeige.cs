// UBOAT Toolkit - Anzeige: Kamera, Licht, Karte, Meldungen, Ton
// Diese Datei fasst 15 frühere Einzeldateien zusammen (0.4.30). Jeder Abschnitt nennt seine alte Datei.

// ====================================================================================================
// Abschnitt: Modules/CameraTools.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;

}

// ====================================================================================================
// Abschnitt: Modules/CamZoom.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.Input;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.UI;
    using global::DWS.Common.InjectionFramework;

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

// ====================================================================================================
// Abschnitt: Modules/Events.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.IO;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Sandbox;
    using global::UBOAT.Game.Sandbox.Messages;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Items.Statuses;

    // - Time compression stops on damage (hull hit or new leak) and on radio messages.
    // - Speedometer + odometer (shown in the info line top left).
    // - War diary: important events with game date and position in logs/kriegstagebuch.txt.
    public class Events : ModuleBase
    {
        private float lastHealth = -1f;
        private int lastLeaks = -1;
        private Vector2 lastPos;
        private bool hasPos;
        public static float TripKm, TotalKm;
        private static GameTime gameTime;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            Damage(ship);
            Odometer(ship);
        }

        private void Damage(PlayerShip ship)
        {
            int leaks = 0;
            foreach (var e in Util.Find<LeakableEquipment>())
                if (e != null && e.HasStatus<Leaking>()) leaks++;
            float hp = ship.Health;
            bool hit = (lastHealth >= 0f && hp < lastHealth - 0.001f) || (lastLeaks >= 0 && leaks > lastLeaks);
            if (hit)
            {
                Diary("Schaden: Rumpf " + (hp * 100f).ToString("0") + " %, Lecks " + leaks);
                if (Settings.B("StopTimeOnDamage")) Game.StopTimeCompression("Schaden am Boot!");
            }
            lastHealth = hp; lastLeaks = leaks;
        }

        private void Odometer(PlayerShip ship)
        {
            var w = Game.Wolfpack;
            if (w == null) return;
            if (ship.Docked) { if (TripKm > 0f) Diary("Im Hafen. Fahrt: " + TripKm.ToString("0") + " km"); TripKm = 0f; hasPos = false; return; }
            var p = w.Position;
            if (hasPos)
            {
                float d = (p - lastPos).magnitude;
                if (d < 50f) { TripKm += d; TotalKm += d; } // bigger jumps = teleport / quick travel
            }
            lastPos = p; hasPos = true;
            if (Settings.B("Speedometer"))
                Speed = Mathf.Abs(ship.AverageVelocity) * 1.943844f;
        }

        public static float Speed;

        public static string SpeedText =>
            !Settings.B("Speedometer") ? null : Join(
                Settings.B("InfoSpeed") ? Speed.ToString("0.0") + " kn" : null,
                Settings.B("InfoTrip") ? "Fahrt: " + TripKm.ToString("0") + " km" : null,
                Settings.B("InfoTotal") ? "gesamt: " + TotalKm.ToString("0") + " km" : null);

        public static string Join(params string[] parts)
        {
            var l = new System.Collections.Generic.List<string>();
            foreach (var p in parts) if (!string.IsNullOrEmpty(p)) l.Add(p);
            return l.Count == 0 ? null : string.Join("  |  ", l);
        }

        public static void Diary(string text)
        {
            if (!Settings.B("WarDiary")) return;
            try
            {
                if (gameTime == null) gameTime = UnityEngine.Object.FindObjectOfType<GameTime>();
                string date = gameTime != null ? gameTime.CurrentDateTime.ToString("dd.MM.yyyy HH:mm") : "?";
                var w = Game.Wolfpack;
                string pos = w != null ? " [" + w.Position.x.ToString("0") + "/" + w.Position.y.ToString("0") + "]" : "";
                var dir = Path.Combine(Settings.Folder, "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "kriegstagebuch.txt"), date + pos + "  " + text + Environment.NewLine);
            }
            catch (Exception e) { Util.Log("Tagebuch: " + e.Message); }
        }
    }

    // Radio message received by the player -> stop time compression + diary entry.
    [HarmonyPatch]
    internal static class Events_RadioPatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var t in new[] { typeof(IntelMessage), typeof(Report), typeof(CustomMessage), typeof(GroupReportMessage), typeof(BasicMessage) })
            {
                var m = AccessTools.Method(t, "OnReceiveMessage", new[] { typeof(SandboxEntity) });
                if (m != null) yield return m;
            }
        }

        private static void Postfix(IMessage __instance, SandboxEntity entity)
        {
            if (entity == null || !entity.IsPlayerShip) return;
            if (__instance.Sender != null && __instance.Sender.IsPlayerShip) return;
            string who = __instance.SenderName ?? "?";
            Events.Diary("Funkspruch von " + who);
            if (Settings.B("StopTimeOnRadio")) Game.StopTimeCompression("Funkspruch von " + who);
        }
    }

    // No automatic alarm: the crew is only alerted when you order it yourself.
    [HarmonyPatch(typeof(PlayerShip), nameof(PlayerShip.StartAlarm))]
    internal static class Events_NoAutoAlarmPatch
    {
        private static bool Prefix(bool manualOrder) => manualOrder || !Settings.B("NoAutoAlarm");
    }
}

// ====================================================================================================
// Abschnitt: Modules/ExplosionLimit.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Shell explosions: the game spawns one explosion effect per hit (Shell.Explode). Here the effect is made smaller
    // and at most x per second are spawned. Damage is computed before and is not changed.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Entities.Shell), "Explode")]
    internal static class ExplosionLimit_Patch
    {
        private static readonly MethodInfo Inst = AccessTools.FirstMethod(typeof(Object), m => m.Name == "Instantiate" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)?.MakeGenericMethod(typeof(GameObject));
        private static readonly MethodInfo Mine = AccessTools.Method(typeof(ExplosionLimit_Patch), nameof(Spawn));
        // The game forbids direct calls to Object.Instantiate in mod code: the non-generic overload is called via reflection.
        private static readonly MethodInfo InstObj = typeof(Object).GetMethod("Instantiate", new[] { typeof(Object) });
        private static GameObject Clone(GameObject p) => (GameObject)InstObj.Invoke(null, new object[] { p });
        private static float second; private static int count; private static int skipped; private static float nextLog;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            int n = 0;
            foreach (var ci in code)
            {
                if (Inst != null && ci.Calls(Inst)) { n++; yield return new CodeInstruction(OpCodes.Call, Mine); continue; }
                yield return ci;
            }
            Util.Log("Explosionen: " + n + " Stelle(n) umgeleitet");
        }

        // Smoothed frames per second (real time).
        private static float fps = 60f, lastT;
        internal static float Fps()
        {
            float t = Time.unscaledTime, dt = Time.unscaledDeltaTime;
            if (t != lastT && dt > 0f) { lastT = t; fps = Mathf.Lerp(fps, 1f / dt, 0.2f); }
            return fps;
        }

        private static GameObject Spawn(GameObject prefab)
        {
            if (!Settings.B("ExplosionLimit")) return Clone(prefab);
            float now = Time.unscaledTime;
            float scale = Mathf.Clamp(Settings.F("ExplosionScale"), 0.1f, 1f);
            int max = Mathf.Max(1, Mathf.RoundToInt(Settings.F("ExplosionMaxPerSec")));
            if (Settings.B("ExplosionAuto"))
            {
                // Load-based detail: at target FPS or better full effects; below, scale and count go down with the FPS.
                float load = Mathf.Clamp01(Fps() / Mathf.Max(1f, Settings.F("ExplosionTargetFps")));
                scale = Mathf.Lerp(scale, 1f, load);
                max = load >= 0.999f ? 1000 : Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, max * 3f, load)));
            }
            if (now - second >= 1f) { second = now; count = 0; }
            if (++count > max)
            {
                skipped++;
                if (now > nextLog) { nextLog = now + 60f; Util.Log("Explosionen begrenzt: " + skipped + " Effekte ausgelassen (gesamt)"); }
                var dummy = new GameObject("SkippedExplosion");
                Object.Destroy(dummy, 0.1f);
                return dummy;
            }
            var go = Clone(prefab);
            float s = scale;
            if (s < 0.999f)
            {
                go.transform.localScale = prefab.transform.localScale * s;
                foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
            }
            return go;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/FadeReparent.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Fix: when a crew/port-worker figure fades in while its parent object (e.g. "Port Workers Controller")
    // is being switched on or off, the game moves its "Fade Effect Renderer" into that parent at the wrong
    // moment and Unity refuses with an error. The move is now done one frame later, when it is allowed.
    // If the parent is gone by then, the orphaned renderer is removed so nothing is left behind.
    public class FadeReparent : ModuleBase
    {
        private struct Job { public Transform Child, Parent; public bool Keep; public bool HadParent; }
        private static readonly List<Job> jobs = new List<Job>();
        private static int fixedCount;

        public static void Set(Transform child, Transform parent, bool keep)
        {
            if (child == null) return;
            if (!Settings.B("FixFadeReparent")) { child.SetParent(parent, keep); return; }
            jobs.Add(new Job { Child = child, Parent = parent, Keep = keep, HadParent = parent != null });
        }

        public override void Frame()
        {
            if (jobs.Count == 0) return;
            var list = jobs.ToArray(); jobs.Clear();
            foreach (var j in list)
            {
                if (j.Child == null) continue;
                try
                {
                    if (j.HadParent && j.Parent == null) { Object.Destroy(j.Child.gameObject); continue; }
                    j.Child.SetParent(j.Parent, j.Keep);
                    fixedCount++;
                }
                catch (System.Exception e) { Util.Log("Fade-Fix: " + e.Message); }
            }
        }

        public static int Fixed => fixedCount;
    }

    [HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.InteractiveCharacter), "FadeInOut")]
    internal static class FadeReparent_Patch
    {
        private static readonly MethodInfo SetParent2 = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(FadeReparent), nameof(FadeReparent.Set));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            var list = new List<CodeInstruction>(code);
            int n = 0;
            foreach (var c in list)
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand as MethodInfo == SetParent2)
                { c.opcode = OpCodes.Call; c.operand = Ours; n++; }
            if (n != 1) Util.Log("Fade-Fix: erwartet 1 Stelle, gefunden " + n + (n == 0 ? " - bleibt unverändert." : " - alle umgeleitet."));
            return list;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/IconFix.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Resources;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Fix 'Requested asset at path "Icons/Activators/Goods Depository" (or "Icons/Equipment/Fuel") doesn't exist.':
    // the game has no picture for some port depots, tanks and items, and logs a red error with a long stack
    // each time a storage window, banner or info box shows them. Now: picture quietly, else the folder's
    // "Unknown" picture, else the activator "Unknown" picture - no red error.
    [HarmonyPatch]
    internal static class IconFix
    {
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(IconFix), nameof(Get));
        private static readonly string[] TypeNames = {
            "UBOAT.Game.UI.ActivatorDetailsUI", "UBOAT.Game.UI.DeviceInfoUI", "UBOAT.Game.UI.Storage.StorageTabUI",
            "UBOAT.Game.UI.Storage.StorageWindow", "UBOAT.Game.UI.Storage.StorageUICount", "UBOAT.Game.UI.Storage.StorageSlotUI",
            "UBOAT.Game.UI.Banners.BaseActivatorBanner", "UBOAT.Game.UI.CharacterSelectionUI",
            "UBOAT.Game.UI.StoryPlayerCharacterUI", "UBOAT.Game.UI.StoryPlayerCharacterSlotUI", "UBOAT.Game.UI.EquipmentUpgradeItemUI", "UBOAT.Game.UI.SkillTokenUI", "UBOAT.Game.UI.Map.PlayerShipMarkerController", "UBOAT.Game.UI.Map.EntityMarkerUI", "UBOAT.Game.UI.Banners.CharacterActivatorBanner" };

        private static bool IsCall(object operand) => operand is MethodInfo m && m.Name == "RetrieveAsset"
            && m.IsGenericMethod && m.GetGenericArguments()[0] == typeof(Sprite) && m.GetParameters().Length == 3;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var list = new List<MethodBase>();
            foreach (var n in TypeNames)
            {
                var t = UboatToolkit.Util.FindType(n);
                if (t == null) continue;
                var types = new List<Type> { t };
                types.AddRange(t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
                foreach (var ty in types)
                    foreach (var m in AccessTools.GetDeclaredMethods(ty))
                    {
                        if (m.IsAbstract || m.IsGenericMethodDefinition || m.ContainsGenericParameters) continue;
                        try { if (PatchProcessor.ReadMethodBody(m).Any(i => IsCall(i.Value))) list.Add(m); } catch { }
                    }
            }
            return list;
        }

        public static Resource<Sprite> Get(ResourceManager rm, string path, object owner, bool logErrors)
        {
            if (!Settings.B("FixMissingIcons") || path == null) return rm.RetrieveAsset<Sprite>(path, owner, logErrors);
            if (rm.AssetExists(path)) return rm.RetrieveAsset<Sprite>(path, owner, logErrors);
            int i = path.LastIndexOf('/');
            if (i > 0) { var u = path.Substring(0, i) + "/Unknown"; if (rm.AssetExists(u)) return rm.RetrieveAsset<Sprite>(u, owner, false); }
            return rm.RetrieveAsset<Sprite>("Icons/Activators/Unknown", owner, false);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && IsCall(c.operand)) { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Resources;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Fix 'GameObject is already being activated or deactivated.' when coming back from the character
    // screen: the main camera is switched off, and while Unity switches it off the free camera moves it
    // to a new parent (not allowed then). The move is now done one frame later instead.
    [HarmonyLib.HarmonyPatch]
    internal static class CameraParentFix
    {
        private static int inDisable;
        private static UnityEngine.Transform pendingT, pendingParent;
        private static bool pendingWorld;

        [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.MainCamera), "OnDisable"), HarmonyLib.HarmonyPrefix]
        private static void Enter() => inDisable++;

        [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.MainCamera), "OnDisable"), HarmonyLib.HarmonyFinalizer]
        private static System.Exception Leave(System.Exception __exception) { if (inDisable > 0) inDisable--; return __exception; }

        [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Camera.FreeFlyCameraController), "StopController"), HarmonyLib.HarmonyTranspiler]
        private static System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> T(System.Collections.Generic.IEnumerable<HarmonyLib.CodeInstruction> code)
        {
            var setParent = HarmonyLib.AccessTools.Method(typeof(UnityEngine.Transform), "SetParent", new[] { typeof(UnityEngine.Transform), typeof(bool) });
            var ours = HarmonyLib.AccessTools.Method(typeof(CameraParentFix), nameof(SetParent));
            foreach (var c in code)
            {
                if ((c.opcode == System.Reflection.Emit.OpCodes.Callvirt || c.opcode == System.Reflection.Emit.OpCodes.Call) && c.operand as System.Reflection.MethodInfo == setParent)
                { c.opcode = System.Reflection.Emit.OpCodes.Call; c.operand = ours; }
                yield return c;
            }
        }

        public static void SetParent(UnityEngine.Transform t, UnityEngine.Transform parent, bool worldStays)
        {
            if (inDisable > 0 && Settings.B("FixCameraParent")) { pendingT = t; pendingParent = parent; pendingWorld = worldStays; return; }
            t.SetParent(parent, worldStays);
        }

        internal static void RunPending()
        {
            if (pendingT == null) return;
            var t = pendingT; pendingT = null;
            try { if (t != null && pendingParent != null) t.SetParent(pendingParent, pendingWorld); } catch (System.Exception e) { Util.Log("Kamera-Fix: " + e.Message); }
        }
    }

    public class CameraParentFixModule : ModuleBase
    {
        public override void Frame() => CameraParentFix.RunPending();
    }
}

namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Linq;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Resources;
    using global::HarmonyLib;
    using global::UnityEngine;

    // 0.3.93 (DoMe: debug console still pops up about once a minute): many other UI parts (contact list, cursor
    // action icons, periscope overlays) ask for pictures the game does not ship. Each one wrote a red
    // "Requested asset at path ... doesn't exist." error, which opens the console. A missing picture is harmless:
    // for paths under Icons/ and Images/ the error is now only counted, not written.
    [HarmonyPatch]
    internal static class IconFix_MissingLog
    {
        internal static int Hidden;
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Mono shares one method body for all class types (Sprite, Texture2D, ...): patching the Sprite version covers them all.
            var list = new List<MethodBase>();
            foreach (var t in new[] { typeof(DWS.Common.Resources.ResourceBase<Sprite>) })
            {
                var m = AccessTools.DeclaredMethod(t, "LogAssetMissing");
                if (m != null && !list.Contains(m)) list.Add(m);
            }
            var ab = UboatToolkit.Util.FindType("DWS.Common.Resources.AssetBundleContainedResource`1");
            if (ab != null)
                foreach (var a in new[] { typeof(Sprite) })
                {
                    try { var m = AccessTools.DeclaredMethod(ab.MakeGenericType(a), "LogAssetMissing"); if (m != null && !list.Contains(m)) list.Add(m); } catch { }
                }
            return list;
        }

        private static bool Prefix(object __instance)
        {
            try
            {
                if (!Settings.B("FixMissingIcons")) return true;
                var p = Traverse.Create(__instance).Property("Path").GetValue<string>();
                if (p == null || !(p.StartsWith("Icons/") || p.StartsWith("Images/"))) return true;
                if (Hidden++ < 20) Util.Log("Fehlendes Bild ohne roten Fehler übersprungen: " + p);
                return false;
            }
            catch { return true; }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/Lighting.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Items;

    // QoL #15: red light at night, blue light while hunted. #16: automatic searchlight (off by default).
    // The light only switches when the situation changes, so you can still switch by hand in between.
    public class Lighting : ModuleBase
    {
        private LightController.Preset? lastWanted;
        private bool? lastSearchlight;
        private Weather weather;

        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (weather == null) weather = Object.FindObjectOfType<Weather>();
            bool night = weather != null && weather.IsNight;

            if (Settings.B("AutoLight"))
            {
                var wanted = LightController.Preset.Surface;
                // DoMe: surfaced = red, blue only once we dive (submerged or diving)
                bool under = ship.SubmergedOrGoingToSubmerge;
                if (Settings.B("AutoLightHuntedBlue") && under) wanted = LightController.Preset.SilentRun;
                else if (Settings.B("AutoLightNightRed") && !under) wanted = LightController.Preset.Alarm;

                if (lastWanted != wanted)
                {
                    var sws = Util.Find<LightSwitch>(); var sw = sws.Length > 0 ? sws[0] : null;   // 0.4.6: interior scene too
                    // 0.4.9: since 0.4.6 the switch in the interior is found; right after a load it is not ready yet
                    // (error in the game's OnLightPresetChanged), so wait for the boat and try again next second.
                    if (sw != null && Util.ShipReady)
                    {
                        try { sw.SetColor(wanted, quiet: true); lastWanted = wanted; }
                        catch (System.Exception) { }
                    }
                }
            }
            else lastWanted = null;

            if (Settings.B("AutoSearchlight"))
            {
                bool on = night && !ship.SubmergedOrGoingToSubmerge && !ship.IsHunted;
                if (lastSearchlight != on)
                {
                    foreach (var s in Util.Find<Searchlight>())
                        if (Util.IsPlayers(s)) s.SetDisableFlag(DeviceDisableFlag.TurnedOff, !on);
                    lastSearchlight = on;
                }
            }
            else lastSearchlight = null;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/LightModes.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::DWS.Common.InjectionFramework;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Actions;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Interactions;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Spaces;
    using global::UBOAT.Game.Scene.Tasks;
    using global::UBOAT.Game.UI.CharacterSwitchUI;
    using GameActivator = global::UBOAT.Game.Scene.Characters.Activators.Activator;

    // Light modes: each of the three switch positions (Normal / Rot / Blau) gets its own style:
    // 0 = Spiel-Original, 1 = warm (dimmed warm white light), 2 = Notlicht (dimmed red light).
    // Plus warm bulb colours, red conning tower at night, own emergency colour, crew effects,
    // automatic emergency light after damage, automatic silent running and new switch labels.
    internal static class LightCfg
    {
        public const int Original = 0, Warm = 1, Emergency = 2;
        public const float MeasuredGain = 1.42f;
        public static readonly string[] StyleKeys = { "LightStyleNormal", "LightStyleRed", "LightStyleBlue" };
        public static readonly string[] StyleNames = { "Spiel-Original", "warm", "Notlicht" };
        public static readonly string[] PosNames = { "Normal (weiß)", "Rot", "Blau" };

        public static bool On => Settings.B("LightModes");
        public static int Style(int pos) => pos < 0 || pos > 2 ? Original : Mathf.Clamp(Mathf.RoundToInt(Settings.F(StyleKeys[pos])), 0, 2);
        public static int Level(string key) => Mathf.Clamp(Mathf.RoundToInt(Settings.F(key)), 0, 100);
        public static Color Col(string prefix) =>
            new Color(Mathf.Clamp01(Settings.F(prefix + "R") / 255f), Mathf.Clamp01(Settings.F(prefix + "G") / 255f), Mathf.Clamp01(Settings.F(prefix + "B") / 255f), 1f);

        // Output percentage (0 = off, 100 = game's full light) -> preset weight the game reads.
        public static float Weight(int pct, float gain)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            if (!(gain > 0.41f)) gain = 1f;
            return (0.4f + pct / 100f * (gain - 0.4f)) / gain;
        }

        // The current switch position, -1 without a boat.
        public static int Pos => LightLive.Lighting == null ? -1 : (int)LightLive.Lighting.CurrentPreset;
        public static int CurStyle => Style(Pos);
        // "Night light": a dimmed warm light on the red or blue position.
        public static bool NightStyle => On && Pos > 0 && CurStyle == Warm;
        public static bool EmergencyStyle => On && Pos >= 0 && CurStyle == Emergency;

        // Fingerprint of everything that changes the look, to re-tint after a menu change.
        public static string Stamp()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var k in new[] { "LightModes", "LightWarm", "LightLevelNormal", "LightLevelNight", "LightLevelEmerg", "LightRedTower", "LightCustomEmerg",
                                      "LightStyleNormal", "LightStyleRed", "LightStyleBlue",
                                      "LightDayR", "LightDayG", "LightDayB", "LightNightR", "LightNightG", "LightNightB",
                                      "LightTowerR", "LightTowerG", "LightTowerB", "LightEmergR", "LightEmergG", "LightEmergB" })
                sb.Append(Settings.F(k)).Append('|');
            return sb.ToString();
        }
    }

    internal static class LightLook
    {
        private static readonly FieldInfo PresetsF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "presets");
        private static readonly FieldInfo SwappingF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "swappingPreset");
        private static readonly FieldInfo ProbeSetsF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "reflectionProbeSets");
        private static readonly FieldInfo ControllersF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "lightControllers");
        private static readonly FieldInfo WhiteF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "lightsColorMultiplier");
        private static readonly FieldInfo RedF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "alarmLightsColorMultiplier");
        private static readonly FieldInfo BlueF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "silentRunLightsColorMultiplier");
        private static readonly MethodInfo GameApplyColour = AccessTools.Method(typeof(PlayerShipInteriorLighting), "ApplyColorMultiplier");
        private static readonly FieldInfo MultipliedF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "multipliedPresets");
        private static readonly FieldInfo PresetMultipliersF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "presetMultipliers");
        private static readonly FieldInfo IntensityF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "lightsIntensityMultiplier");
        private static readonly FieldInfo BulbF = AccessTools.Field(typeof(LightController), "normalColor");
        private static readonly FieldInfo PresetChangedF = AccessTools.Field(typeof(PlayerShipInteriorLighting), "PresetChanged");

        private static PlayerShipInteriorLighting gainFor, reportedFor, towerFor, towerLoggedFor;
        private static float whiteGain = LightCfg.MeasuredGain, redGain = LightCfg.MeasuredGain;
        private static bool[] towerMask;
        private static int fadeFrame = -10;

        public static void ResetTower() { towerFor = null; towerMask = null; }

        public static void RaisePresetChanged(PlayerShipInteriorLighting l)
        {
            if (PresetChangedF == null || l == null) return;
            (PresetChangedF.GetValue(l) as Action)?.Invoke();
        }

        private static void ReadGains(PlayerShipInteriorLighting l)
        {
            if (ReferenceEquals(gainFor, l)) return;
            gainFor = l;
            whiteGain = redGain = LightCfg.MeasuredGain;
            try
            {
                float k = IntensityF == null ? LightCfg.MeasuredGain : (float)IntensityF.GetValue(l);
                var pm = PresetMultipliersF?.GetValue(l) as float[];
                whiteGain = k * (pm != null && pm.Length > 0 ? pm[0] : 1f);
                redGain = k * (pm != null && pm.Length > 1 ? pm[1] : 1f);
            }
            catch (Exception e) { Util.Log("Licht: Helligkeits-Faktoren nicht lesbar, nehme " + LightCfg.MeasuredGain + ": " + e.Message); }
        }

        // Which light set a position lights up with its style.
        public static int SetFor(int pos)
        {
            int s = LightCfg.Style(pos);
            if (s == LightCfg.Warm) return 0;
            if (s == LightCfg.Emergency) return 1;
            return Mathf.Clamp(pos, 0, 2);
        }

        public static float[] Targets(PlayerShipInteriorLighting l, int pos)
        {
            var t = new float[3];
            pos = Mathf.Clamp(pos, 0, 2);
            if (!LightCfg.On) { t[pos] = 1f; return t; }
            ReadGains(l);
            int s = LightCfg.Style(pos);
            if (s == LightCfg.Warm) t[0] = LightCfg.Weight(LightCfg.Level(pos == 0 ? "LightLevelNormal" : "LightLevelNight"), whiteGain);
            else if (s == LightCfg.Emergency) t[1] = LightCfg.Weight(LightCfg.Level("LightLevelEmerg"), redGain);
            else t[pos] = 1f;
            return t;
        }

        public static void ReportGame(PlayerShipInteriorLighting l)
        {
            if (l == null || ReferenceEquals(reportedFor, l)) return;
            reportedFor = l;
            Util.Safe("Licht.Report", () =>
            {
                var ic = System.Globalization.CultureInfo.InvariantCulture;
                var pm = PresetMultipliersF?.GetValue(l) as float[];
                var sb = new System.Text.StringBuilder("Licht (Spielwerte): Set-Faktoren ");
                sb.Append(pm == null ? "?" : string.Join("/", Array.ConvertAll(pm, f => f.ToString("0.###", ic))));
                sb.Append(", Grundhelligkeit ").Append(IntensityF == null ? "?" : ((float)IntensityF.GetValue(l)).ToString("0.###", ic));
                sb.Append(", weiß ").Append(Rgb(WhiteF, l)).Append(", rot ").Append(Rgb(RedF, l)).Append(", blau ").Append(Rgb(BlueF, l));
                var ctrls = ControllersF?.GetValue(l) as LightController[];
                if (ctrls != null && BulbF != null)
                {
                    Color w = Color.black, r = Color.black; int n = 0;
                    foreach (var c in ctrls) { if (c == null) continue; w += (Color)BulbF.GetValue(c); r += c.AlarmColor; n++; }
                    if (n > 0) sb.Append(", Lampen ").Append(n).Append(" Schnitt weiß ").Append(Rgb(w / n)).Append(" rot ").Append(Rgb(r / n));
                }
                int missing = 0;
                foreach (object o in new object[] { PresetsF, SwappingF, ProbeSetsF, ControllersF, WhiteF, RedF, BlueF, GameApplyColour, MultipliedF })
                    if (o == null) missing++;
                sb.Append(missing == 0 ? " | alle Spiel-Felder gefunden" : " | FEHLENDE Spiel-Felder: " + missing);
                Util.Log(sb.ToString());
            });
        }

        private static string Rgb(FieldInfo f, object o) => f == null ? "?" : Rgb((Color)f.GetValue(o));
        private static string Rgb(Color c)
        {
            var ic = System.Globalization.CultureInfo.InvariantCulture;
            return c.r.ToString("0.###", ic) + "," + c.g.ToString("0.###", ic) + "," + c.b.ToString("0.###", ic);
        }

        public static bool FadeAlive => Time.frameCount - fadeFrame <= 2;
        public static bool Swapping(PlayerShipInteriorLighting l) => l != null && SwappingF != null && (bool)SwappingF.GetValue(l);

        // Brings the weights back to our target if the game moved them (e.g. its own depth switch).
        public static void Reconcile(PlayerShipInteriorLighting l)
        {
            if (l == null || PresetsF == null || FadeAlive || Swapping(l)) return;
            var target = Targets(l, (int)l.CurrentPreset);
            var w = (float[])PresetsF.GetValue(l);
            bool off = false;
            for (int i = 0; i < Mathf.Min(w.Length, 3); i++) if (Mathf.Abs(w[i] - target[i]) > 0.001f) off = true;
            if (!off) return;
            StartFade(l);
            if (LightCfg.On) ApplyTint(l);
        }

        public static void StartFade(PlayerShipInteriorLighting l)
        {
            if (l == null || PresetsF == null || SwappingF == null || FadeAlive) return;
            fadeFrame = Time.frameCount;
            l.StartCoroutine(Fade(l));
        }

        private static IEnumerator Fade(PlayerShipInteriorLighting l)
        {
            SwappingF.SetValue(l, true);
            while (l != null)
            {
                fadeFrame = Time.frameCount;
                int pos = (int)l.CurrentPreset;
                var target = Targets(l, pos);
                var p = (float[])PresetsF.GetValue(l);
                bool done = true;
                for (int i = 0; i < 3; i++)
                {
                    float step = Time.deltaTime;
                    if (p[i] < target[i]) p[i] = Mathf.Min(p[i] + step, target[i]);
                    else if (p[i] > target[i]) p[i] = Mathf.Max(p[i] - step, target[i]);
                    if (p[i] != target[i]) done = false;
                }
                SetProbes(l, LightCfg.On ? SetFor(pos) : pos);
                l.ApplyLightControllersPresets();
                if (done) break;
                yield return null;
            }
            fadeFrame = -10;
            if (l != null) SwappingF.SetValue(l, false);
        }

        private static void SetProbes(PlayerShipInteriorLighting l, int active)
        {
            var sets = ProbeSetsF?.GetValue(l) as GameObject[];
            if (sets == null) return;
            for (int i = 0; i < sets.Length; i++)
                if (sets[i] != null && sets[i].activeSelf != (i == active)) sets[i].SetActive(i == active);
        }

        public static void ApplyTint(PlayerShipInteriorLighting l)
        {
            var ctrls = ControllersF?.GetValue(l) as LightController[];
            if (ctrls == null) return;
            int pos = (int)l.CurrentPreset;
            int s = LightCfg.Style(pos);
            Color white = WhiteF == null ? Color.white : (Color)WhiteF.GetValue(l);
            Color red = RedF == null ? Color.white : (Color)RedF.GetValue(l);
            Color blue = BlueF == null ? Color.white : (Color)BlueF.GetValue(l);
            Color c;
            if (s == LightCfg.Emergency) c = Settings.B("LightCustomEmerg") ? Color.white : red;
            else if (s == LightCfg.Warm) c = !Settings.B("LightWarm") ? white : LightCfg.Col(pos == 0 ? "LightDay" : "LightNight");
            else c = pos == 0 ? white : pos == 1 ? red : blue;

            bool[] tower = (s == LightCfg.Warm && pos > 0 && Settings.B("LightRedTower")) ? TowerMask(l, ctrls) : null;
            Color towerCol = LightCfg.Col("LightTower");
            for (int i = 0; i < ctrls.Length; i++)
                if (ctrls[i] != null) ctrls[i].ApplyColorMultiplier(tower != null && i < tower.Length && tower[i] ? towerCol : c);
            Push(l, ctrls);
        }

        private static void Push(PlayerShipInteriorLighting l, LightController[] ctrls)
        {
            var multiplied = MultipliedF?.GetValue(l) as float[];
            var presets = PresetsF?.GetValue(l) as float[];
            bool remix = multiplied != null && presets != null && multiplied.Length >= 3 && presets.Length >= 3 && multiplied[0] + multiplied[1] + multiplied[2] > 0f;
            foreach (var c in ctrls)
            {
                if (c == null || c.Light == null) continue;   // not prepared yet
                if (remix) c.UpdatePresets(multiplied, presets); else c.UpdatePresets();
            }
        }

        public static void RestoreGameTint(PlayerShipInteriorLighting l)
        {
            if (l == null || GameApplyColour == null) return;
            GameApplyColour.Invoke(l, null);   // our postfix does nothing while the mode is off
            var ctrls = ControllersF?.GetValue(l) as LightController[];
            if (ctrls != null) Push(l, ctrls);
        }

        private static bool[] TowerMask(PlayerShipInteriorLighting l, LightController[] ctrls)
        {
            if (ReferenceEquals(towerFor, l) && towerMask != null && towerMask.Length == ctrls.Length) return towerMask;
            var mask = new bool[ctrls.Length];
            int n = 0;
            var interior = l.GetComponent<PlayerShipInterior>();
            Compartment ct = interior == null ? null : interior.GetCompartment("Conning Tower Interior");
            if (ct == null)
            {
                if (!ReferenceEquals(towerLoggedFor, l)) { towerLoggedFor = l; Util.Log("Licht: Turm nicht gefunden (0 von " + ctrls.Length + " Lampen)"); }
                return mask;
            }
            for (int i = 0; i < ctrls.Length; i++)
                if (ctrls[i] != null && ct.Contains(ctrls[i].transform.position)) { mask[i] = true; n++; }
            towerFor = l; towerMask = mask;
            Util.Log("Licht: Turm hat " + n + " von " + ctrls.Length + " Lampen");
            return mask;
        }
    }

    // Live state: the lighting, the switch, damage, saving, settings changes.
    internal static class LightLive
    {
        public static PlayerShipInteriorLighting Lighting;
        public static LightSwitch Switch;
        private static SavesManager saves;
        private static readonly MethodInfo OnPresetChanged = AccessTools.Method(typeof(LightSwitch), "OnLightPresetChanged");
        private static float nextTick, nextDamage, nextReconcile, saveHoldUntil = -1f;
        private static string stamp;
        private static bool? wasOn;
        public static bool Damaged;
        public static bool Saving => (saves != null && saves.IsSaving) || Time.unscaledTime < saveHoldUntil;

        public static void Remember(PlayerShipInteriorLighting l) { if (l != null) Lighting = l; }

        public static void RefreshCrewNoise()
        {
            if (Switch == null || Lighting == null || OnPresetChanged == null) return;
            OnPresetChanged.Invoke(Switch, new object[] { Lighting.CurrentPreset });
        }

        public static PlayerCrew Crew()
        {
            try { return InjectionFramework.Instance.GetInstance<PlayerCrew>(); } catch { return null; }
        }

        // Called from the light switch every frame (cheap: work is throttled).
        public static void Frame(PlayerShip ship, SavesManager sm)
        {
            if (sm != null) saves = sm;
            float now = Time.unscaledTime;
            if (now < nextTick) return;
            nextTick = now + 1f;
            LightEffects.SyncPerformance(false);
            LightAuto.TickSilent(ship);
            if (now >= nextDamage) { nextDamage = now + 6.1f; RefreshDamage(ship); }
        }

        private static void RefreshDamage(PlayerShip ship)
        {
            if (ship == null || ship.Equipment == null) { Damaged = false; return; }
            float missing = 0f; int leaks = 0;
            var list = ship.Equipment;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var e = list[i];
                if (e == null || e.MaxHealth == null) continue;
                float max = e.MaxHealth.Value;
                if (max <= 0f) continue;
                missing += max - e.Health;
                var le = e as LeakableEquipment;
                if (le != null && le.IsLeaking && (le.Compartment == null || !le.Compartment.Evacuation)) leaks++;
            }
            bool was = Damaged;
            Damaged = leaks > Settings.F("LightLeaks") || missing > Settings.F("LightDamage");
            if (was != Damaged) Util.Log("Licht: Schaden " + (Damaged ? "SCHWER" : "ok") + " (fehlende Haltbarkeit " + missing.ToString("F1") + ", Lecks " + leaks + ")");
        }

        public static void BeforeSave()
        {
            saveHoldUntil = Time.unscaledTime + 1.5f;
            LightEffects.SyncPerformance(true);
            LightAction.RemoveQueued();
        }

        // Once per second: notices menu changes (on/off, styles, colours) and keeps the weights right.
        public static void Reconcile()
        {
            float now = Time.unscaledTime;
            if (now < nextReconcile) return;
            nextReconcile = now + 1f;
            bool on = LightCfg.On;
            if (wasOn != on)
            {
                bool first = wasOn == null;
                wasOn = on;
                if (!first || on) Switched(on);
            }
            if (Lighting == null) return;
            string s = LightCfg.Stamp();
            if (on && s != stamp)
            {
                stamp = s;
                Util.Safe("Licht.Tint", () => { LightLook.ApplyTint(Lighting); LightLabels.Apply(); LightLook.RaisePresetChanged(Lighting); });
            }
            Util.Safe("Licht.Reconcile", () => LightLook.Reconcile(Lighting));
        }

        private static void Switched(bool on)
        {
            Util.Safe("Licht.Switch", () =>
            {
                if (on) LightLabels.Apply(); else LightLabels.Restore();
                LightEffects.SyncPerformance(!on);
                RefreshCrewNoise();
                stamp = null;
                if (Lighting == null) return;
                LightLook.StartFade(Lighting);
                if (on) LightLook.ApplyTint(Lighting); else LightLook.RestoreGameTint(Lighting);
                LightLook.RaisePresetChanged(Lighting);
            });
        }

        // Buttons in the F10 tab: switch the light position right now.
        public static void SwitchTo(int pos)
        {
            Util.Safe("Licht.SwitchTo", () =>
            {
                var ship = Util.PlayerShip;
                var sw = Switch != null ? Switch : (Util.Find<LightSwitch>().Length > 0 ? Util.Find<LightSwitch>()[0] : null);
                if (sw != null) sw.SetColor((LightController.Preset)pos, quiet: true);
                else Lighting?.SetPreset((LightController.Preset)pos);
                if (Lighting != null) Lighting.WasPresetSetManually = true;
            });
        }
    }

    [HarmonyPatch(typeof(PlayerShipInteriorLighting), "SetPreset")]
    internal static class Light_SetPresetPatch
    {
        private static bool Prefix(PlayerShipInteriorLighting __instance, LightController.Preset newPreset, ref LightController.Preset ___currentPreset, ref float ___presetChangeTime)
        {
            if (!LightCfg.On) return true;
            var oldPreset = ___currentPreset;
            float oldTime = ___presetChangeTime;
            try
            {
                LightLive.Remember(__instance);
                bool changed = ___currentPreset != newPreset;
                ___currentPreset = newPreset;
                ___presetChangeTime = float.PositiveInfinity;
                LightLook.StartFade(__instance);
                if (changed)
                {
                    LightLook.ApplyTint(__instance);
                    LightEffects.SyncPerformance(false);
                    try { LightLook.RaisePresetChanged(__instance); } catch (Exception e) { Util.Log("Licht: Schalter-Anzeige nicht aktualisiert: " + e.Message); }
                }
                return false;
            }
            catch (Exception e)
            {
                Util.Log("Licht.SetPreset: " + e.Message);
                ___currentPreset = oldPreset;
                ___presetChangeTime = oldTime;
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerShipInteriorLighting), "Start")]
    internal static class Light_StartPatch
    {
        private static void Postfix(PlayerShipInteriorLighting __instance)
        {
            LightLive.Remember(__instance);
            LightLook.ResetTower();
            LightLook.ReportGame(__instance);
            if (!LightCfg.On) return;
            Util.Safe("Licht.Start", () => { LightLook.StartFade(__instance); LightLook.ApplyTint(__instance); });
        }
    }

    [HarmonyPatch(typeof(PlayerShipInteriorLighting), "ApplyColorMultiplier")]
    internal static class Light_ApplyColourPatch
    {
        private static void Postfix(PlayerShipInteriorLighting __instance)
        {
            if (LightCfg.On) Util.Safe("Licht.Tint", () => LightLook.ApplyTint(__instance));
        }
    }

    // Own emergency colour: swaps the red bulb colour while the lights are mixed.
    [HarmonyPatch(typeof(LightController), "UpdatePresets", new[] { typeof(float[]), typeof(float[]) })]
    internal static class Light_EmergencyColourPatch
    {
        private static void Prefix(LightController __instance, out Color? __state)
        {
            __state = null;
            if (!LightCfg.On || !Settings.B("LightCustomEmerg") || LightCfg.CurStyle != LightCfg.Emergency) return;
            try { __state = __instance.AlarmColor; __instance.AlarmColor = LightCfg.Col("LightEmerg"); } catch { }
        }
        private static Exception Finalizer(LightController __instance, Color? __state, Exception __exception)
        {
            if (__state.HasValue) try { __instance.AlarmColor = __state.Value; } catch { }
            return __exception;
        }
    }

    internal static class LightEffects
    {
        // Night light: crew performance lower by x points ("Base" delta modifier, back to 1 before saving).
        public static void SyncPerformance(bool forceGame)
        {
            var crew = LightLive.Crew();
            if (crew == null) return;
            bool night = !forceGame && !LightLive.Saving && LightCfg.NightStyle;
            float target = night ? 1f - Settings.F("LightNightPerf") / 100f : 1f;
            for (int i = 0; i < crew.Count; i++)
            {
                var pc = crew[i];
                if (pc == null || pc.Performance == null) continue;
                var m = pc.Performance.GetDeltaModifier("Base");
                if (m != null && m.Value != target) m.Value = target;
            }
        }
    }

    // The game ties oxygen / lookout effects to the brightest light set. With our styles the
    // switch POSITION keeps its tactical meaning: Blau = silent oxygen use, Rot = night vision.
    [HarmonyPatch(typeof(LightSwitch), "Update")]
    internal static class Light_SwitchUpdatePatch
    {
        private static void Postfix(LightSwitch __instance, Resource ___oxygen, Modifier ___oxygenUseModifier, Parameter ___silentRunOxygenUsageScale,
                                    Modifier[] ___sightRangeModifiers, Parameter ___redLightObservationModifier,
                                    PlayerShipInteriorLighting ___lighting, Weather ___weather, SavesManager ___savesManager)
        {
            LightLive.Remember(___lighting);
            LightLive.Reconcile();
            if (!LightCfg.On) return;
            try
            {
                var ship = __instance.ParentEntity as PlayerShip;
                if (ship == null || ___lighting == null) return;
                LightLive.Switch = __instance;
                var pos = ___lighting.CurrentPreset;
                if (pos == LightController.Preset.SilentRun && ___oxygen != null && ___oxygenUseModifier != null && ___silentRunOxygenUsageScale != null && ___oxygen.Gain.Value < 0f)
                    ___oxygenUseModifier.Value = ___silentRunOxygenUsageScale.Value;
                if (___sightRangeModifiers != null && ___redLightObservationModifier != null && ___weather != null)
                {
                    float f = 1f;
                    float vis = ___weather.SunPositionVisibilityFactor * ___weather.CloudinessVisibilityFactor * ___weather.LightingsVisibilityFactor;
                    if (pos == LightController.Preset.Alarm && vis > 0f) f = 1f + (1f / vis - 1f) * ___redLightObservationModifier.Value;
                    foreach (var m in ___sightRangeModifiers) if (m != null) m.Value = f;
                }
                LightLive.Frame(ship, ___savesManager);
            }
            catch (Exception e) { Util.Log("Licht.Switch: " + e.Message); }
        }
    }

    // The blue light's crew-noise cut belongs to the blue light: gone when Blau shows warm or Notlicht.
    [HarmonyPatch(typeof(LightSwitch), "OnLightPresetChanged")]
    internal static class Light_CrewNoisePatch
    {
        private static void Postfix(Modifier ___crewNoiseModifier, LightController.Preset preset)
        {
            if (!LightCfg.On || ___crewNoiseModifier == null) return;
            if (LightCfg.Style((int)preset) != LightCfg.Original) ___crewNoiseModifier.Value = 1f;
        }
    }

    // Night light: sleeping crew recover faster.
    [HarmonyPatch(typeof(UBOAT.Game.Core.AI.Behaviours.SleepBehaviour), "OnBehaviourUpdate")]
    internal static class Light_SleepPatch
    {
        private static Need scaled;
        private static float saved;
        private static void Prefix(Need ___fatigue)
        {
            scaled = null;
            if (___fatigue == null || !LightCfg.NightStyle) return;
            float f = 1f + Settings.F("LightNightSleep") / 100f;
            if (f == 1f) return;
            saved = ___fatigue.satiationSpeed;
            ___fatigue.satiationSpeed = saved * f;
            scaled = ___fatigue;
        }
        private static Exception Finalizer(Exception __exception)
        {
            if (scaled != null) { scaled.satiationSpeed = saved; scaled = null; }
            return __exception;
        }
    }

    // Emergency light: the electric motor draws less from the batteries.
    [HarmonyPatch(typeof(PlayerShipEngine), "DoUpdate")]
    internal static class Light_ElectricPatch
    {
        private static float lastWritten = float.NaN;
        private static void Postfix(PlayerShipEngine __instance, Modifier ___energyModifier, PlayerShip ___playerShip)
        {
            if (!LightCfg.On || ___energyModifier == null || ___playerShip == null) return;
            if (!ReferenceEquals(__instance, ___playerShip.ElectricEngine)) return;
            if (!LightCfg.EmergencyStyle) { lastWritten = float.NaN; return; }
            float v = ___energyModifier.Value;
            if (v == lastWritten) return;
            lastWritten = v * (1f - Settings.F("LightEmergPower") / 100f);
            ___energyModifier.Value = lastWritten;
        }
    }

    [HarmonyPatch(typeof(SavesManager), "Save")]
    internal static class Light_BeforeSavePatch
    {
        private static void Prefix() { Util.Safe("Licht.BeforeSave", LightLive.BeforeSave); }
    }

    internal static class LightAuto
    {
        private static bool weEnabled, suppressed;

        // Auto silent running: on below the swap depth, off again above it. A manual "off" while deep is respected.
        public static void TickSilent(PlayerShip ship)
        {
            if (ship == null) return;
            ISilentRunningController src;
            try { src = InjectionFramework.Instance.GetInstance<ISilentRunningController>(); } catch { return; }
            if (src == null) return;
            bool autoOn = LightCfg.On && Settings.B("LightAutoSilent");
            bool isOn = src.IsSilentRunningEnabled;
            float swap = Settings.F("LightSwapDepth");
            bool deep = ship.DeckDepth > swap && ship.TargetDepth > swap;
            bool shallow = ship.DeckDepth < swap && ship.TargetDepth < swap;
            if (weEnabled && !isOn) { weEnabled = false; suppressed = true; }
            if (shallow) suppressed = false;
            if (!autoOn) { weEnabled = false; return; }
            int step = 0;
            if (shallow && weEnabled && isOn) { weEnabled = false; step = -1; }
            else if (deep && !isOn && !suppressed && ship.DepthPreset != DepthPreset.HydrophoneCheck && (!Settings.B("LightSilentAlarmOnly") || ship.Alarmed))
            { weEnabled = true; step = 1; }
            if (step == 0) return;
            src.SetSilentRunningEnabled(step > 0, true);
            Util.Log("Licht: Schleichfahrt automatisch " + (step > 0 ? "AN" : "AUS") + " bei " + ship.DeckDepth.ToString("F0") + " m");
        }

        // The position that shows Notlicht (Blau first, then Rot); Blau if none does.
        public static LightController.Preset EmergencyPos()
        {
            if (LightCfg.Style(2) == LightCfg.Emergency) return LightController.Preset.SilentRun;
            if (LightCfg.Style(1) == LightCfg.Emergency) return LightController.Preset.Alarm;
            return LightController.Preset.SilentRun;
        }
    }

    // The game's "lighting management" (crew switch the light themselves).
    [HarmonyPatch(typeof(AutomaticActionsController), "GetPreferredLighting")]
    internal static class Light_PreferredPatch
    {
        private static void Postfix(ref LightController.Preset __result, Weather ___weather)
        {
            if (!LightCfg.On) return;
            try
            {
                if (Settings.B("LightAutoEmerg") && LightLive.Damaged) { __result = LightAuto.EmergencyPos(); return; }
                var game = __result;
                bool night = LightCfg.Style(1) == LightCfg.Original ? game == LightController.Preset.Alarm : (___weather != null && ___weather.IsNight);
                if (game == LightController.Preset.SilentRun && LightCfg.Style(2) == LightCfg.Original) return;   // game: blue while silent running
                __result = night ? LightController.Preset.Alarm : LightController.Preset.Surface;
            }
            catch (Exception e) { Util.Log("Licht.Preferred: " + e.Message); }
        }
    }

    // Light switch action "Auto silent running" (toggles the setting). Never saved in the game state.
    [NonSerializedInGameState]
    internal sealed class ToolkitAutoSilentAction : DeviceUseAction
    {
        public ToolkitAutoSilentAction(GameDataRow data) : base(data) { Init(); }
        public ToolkitAutoSilentAction(ToolkitAutoSilentAction source, GameActivator activator) : base(source, activator) { Init(); }
        private void Init() { StateDescription = LocalizedString.CreateUnlocalized("Stellt automatische Schleichfahrt um"); }

        protected override void StartMainCharacter(PlayableCharacter character)
        {
            UseDevice(character, null, null, int.MinValue, null, true, SlotUsageFlags.AllowUnderWaterAlways);
        }
        protected override ActionState GetState(GameActivator activator, PlayableCharacter character)
        {
            if (!LightCfg.On) return ActionState.Hidden;
            return base.GetState(activator, character);
        }
        public override string GetLocalizationKeyword(GameActivator activator, PlayableCharacter character) => "Auto Silent Running";
        public override LocalizedString GetLocalizedName(GameActivator activator, PlayableCharacter character, ActionActivationContext context) =>
            LocalizedString.CreateUnlocalized(Settings.B("LightAutoSilent") ? "Auto-Schleichfahrt: AN (klicken = aus)" : "Auto-Schleichfahrt: AUS (klicken = an)");
        public override string GetLocalizedDescription(GameActivator activator, PlayableCharacter character) =>
            "Unter " + Settings.F("LightSwapDepth") + " m schaltet die Crew Schleichfahrt selbst ein, beim Auftauchen wieder aus.";
        public override string GetLocalizedState() => "Stellt automatische Schleichfahrt um";
        public override string GetIconName(GameActivator activator, PlayableCharacter character) => "Switch Light to SilentRun";
        public override int GetSortingPriority(GameActivator activator, PlayableCharacter character) => 3;
        protected override void OnDeviceUsed()
        {
            var o = Settings.Get("LightAutoSilent");
            if (o != null) { o.Value = o.On ? 0 : 1; Settings.Save(); }
            CompleteAction();
        }
    }

    internal static class LightAction
    {
        public const string ActionName = "UboatToolkit.AutoSilentRunning";
        private const string Tag = "Light Switch";

        public static void RemoveQueued()
        {
            var crew = LightLive.Crew();
            if (crew == null) return;
            var buffer = new List<CharacterAction>();
            for (int i = 0; i < crew.Count; i++)
            {
                var pc = crew[i];
                if (pc == null) continue;
                if (pc.Action is ToolkitAutoSilentAction) pc.StopCurrentAction(true);
                RemoveFrom(pc.ActionQueue, buffer);
            }
            RemoveFrom(crew.GlobalActionQueue, buffer);
        }

        private static void RemoveFrom(ActionQueue q, List<CharacterAction> buffer)
        {
            if (q == null || q.Actions == null) return;
            buffer.Clear();
            foreach (CharacterAction a in q.Actions) if (a is ToolkitAutoSilentAction) buffer.Add(a);
            foreach (var a in buffer) q.Remove(a, false);
        }

        public static void AddTo(Dictionary<string, List<CharacterActionsDatabase.CharacterActionInfo>> map, string key, CharacterActionsDatabase.CharacterActionInfo info)
        {
            if (!map.TryGetValue(key, out var list)) { list = new List<CharacterActionsDatabase.CharacterActionInfo>(); map[key] = list; }
            list.Add(info);
        }
    }

    [HarmonyPatch(typeof(CharacterActionsDatabase), "LoadActions")]
    internal static class Light_RegisterActionPatch
    {
        private static void Postfix(Dictionary<string, CharacterActionsDatabase.CharacterActionInfo> ___actionsByName,
                                    Dictionary<string, List<CharacterActionsDatabase.CharacterActionInfo>> ___actionsByActivatorTag,
                                    Dictionary<string, List<CharacterActionsDatabase.CharacterActionInfo>> ___actionsByCharacterTag)
        {
            Util.Safe("Licht.Action", () =>
            {
                var action = new ToolkitAutoSilentAction((GameDataRow)null);
                var info = new CharacterActionsDatabase.CharacterActionInfo(LightAction.ActionName, action, new[] { "Light Switch" }, new[] { "*" }, ActionContext.Normal | ActionContext.Queue, "", "");
                ___actionsByName[LightAction.ActionName] = info;
                LightAction.AddTo(___actionsByActivatorTag, "Light Switch", info);
                LightAction.AddTo(___actionsByCharacterTag, "*", info);
                Util.Log("Licht: Aktion „Auto-Schleichfahrt“ am Lichtschalter angemeldet");
            });
        }
    }

    // Switch labels follow the chosen style (e.g. "Rot" shows "Nachtlicht" when it is warm).
    internal static class LightLabels
    {
        private static readonly FieldInfo StringsF = AccessTools.Field(typeof(Locale), "strings");
        private static readonly FieldInfo ComparerF = AccessTools.Field(typeof(Locale), "stringComparer");
        private static readonly Dictionary<string, string> originals = new Dictionary<string, string>(StringComparer.Ordinal);

        private static Dictionary<string, string> Table()
        {
            var locale = ScriptableObjectSingleton.LoadSingleton<Locale>();
            if (locale == null || StringsF == null || ComparerF == null) return null;
            var cmp = ComparerF.GetValue(locale) as Locale.LocaleStringComparer;
            if (cmp != null) cmp.comparisonBufferLen = 0;
            return StringsF.GetValue(locale) as Dictionary<string, string>;
        }

        private static void Set(Dictionary<string, string> t, string key, string value)
        {
            if (!originals.ContainsKey(key)) originals[key] = t.TryGetValue(key, out var old) ? old : null;
            t[key] = value;
        }
        private static void Back(Dictionary<string, string> t, string key)
        {
            if (!originals.TryGetValue(key, out var old)) return;
            if (old == null) t.Remove(key); else t[key] = old;
            originals.Remove(key);
        }

        public static void Apply()
        {
            if (!LightCfg.On || !Settings.B("LightLabels")) { Restore(); return; }
            Util.Safe("Licht.Labels", () =>
            {
                var t = Table();
                if (t == null) return;
                string[] ui = { "UI/CharacterSwitchUI/White Lighting", "UI/CharacterSwitchUI/Red Lighting", "UI/CharacterSwitchUI/Blue Lighting" };
                string[] act = { "Activators/Switch Light to Surface", "Activators/Switch Light to Alarm", "Activators/Switch Light to SilentRun" };
                string night = "Gedämpftes warmes Licht für die Nacht: die Crew arbeitet etwas langsamer und schläft besser.";
                string emerg = "Gedämpftes rotes Notlicht: weniger Strom für die E-Maschinen.";
                for (int p = 1; p < 3; p++)
                {
                    int s = LightCfg.Style(p);
                    if (s == LightCfg.Original) { Back(t, ui[p]); Back(t, act[p]); Back(t, act[p] + " Description"); Back(t, act[p] + " Short Description"); continue; }
                    bool warm = s == LightCfg.Warm;
                    Set(t, ui[p], warm ? "Nacht" : "Notlicht");
                    Set(t, act[p], warm ? "Nachtlicht einschalten" : "Notlicht einschalten");
                    Set(t, act[p] + " Description", warm ? night : emerg);
                    if (t.ContainsKey(act[p] + " Short Description") || originals.ContainsKey(act[p] + " Short Description"))
                        Set(t, act[p] + " Short Description", warm ? night : emerg);
                }
                Set(t, "Activators/Auto Silent Running", "Auto-Schleichfahrt");
                Set(t, "Activators/Auto Silent Running Description", "Unter " + Settings.F("LightSwapDepth") + " m schaltet die Crew Schleichfahrt selbst ein, beim Auftauchen wieder aus.");
            });
        }

        public static void Restore()
        {
            if (originals.Count == 0) return;
            Util.Safe("Licht.LabelsBack", () =>
            {
                var t = Table();
                if (t == null) return;
                foreach (var kv in originals) { if (kv.Value == null) t.Remove(kv.Key); else t[kv.Key] = kv.Value; }
                originals.Clear();
            });
        }
    }

    [HarmonyPatch(typeof(CharacterSwitchLightingUI), "Start")]
    internal static class Light_SwitchPanelPatch
    {
        private static void Prefix() { if (LightCfg.On) LightLabels.Apply(); }
    }

    // F10 tab "Licht": style per switch position, colours, switch buttons. Levels and effects follow below (generic list).
    internal static class LightTab
    {
        public static void Draw()
        {
            Imgui.Label("Licht-Modi: jede Schalter-Stellung bekommt ihren eigenen Stil. Erst oben „Licht-Modi an“ anhaken (unten in der Liste).");
            if (!LightCfg.On) Imgui.Label("(Licht-Modi sind gerade AUS: das Spiel zeigt sein normales Licht.)");
            for (int p = 0; p < 3; p++)
            {
                var o = Settings.Get(LightCfg.StyleKeys[p]);
                if (o == null) continue;
                Imgui.BeginH();
                Imgui.Label("Stellung " + LightCfg.PosNames[p] + ":", 160);
                for (int s = 0; s < 3; s++)
                    if (Imgui.Button((LightCfg.Style(p) == s ? "> " : "") + LightCfg.StyleNames[s], 150)) { o.Value = s; Settings.Save(); }
                Imgui.EndH();
            }
            Imgui.Space(6);
            Imgui.BeginH();
            Imgui.Label("Jetzt umschalten:", 160);
            for (int p = 0; p < 3; p++)
                if (Imgui.Button((LightCfg.Pos == p ? "> " : "") + LightCfg.PosNames[p], 150)) LightLive.SwitchTo(p);
            Imgui.EndH();
            Imgui.Space(6);
            Colour("Warmes Licht tagsüber (Stellung Normal)", "LightDay");
            Colour("Warmes Licht nachts (Stellung Rot/Blau mit „warm“)", "LightNight");
            Colour("Turm-Licht nachts (wenn „Turm rot“ an)", "LightTower");
            Colour("Eigene Notlicht-Farbe (wenn „eigene Notlicht-Farbe“ an)", "LightEmerg");
            if (Imgui.Button("Farben auf Standard", 200))
                foreach (var k in new[] { "LightDay", "LightNight", "LightTower", "LightEmerg" })
                    foreach (var ch in new[] { "R", "G", "B" }) { var o = Settings.Get(k + ch); if (o != null) o.Value = o.Default; }
            if (Imgui.Button("Licht-Werte ins Log schreiben", 300) && LightLive.Lighting != null)
                Util.Safe("Licht.Log", () => { Util.Log("Licht: Stellung " + LightCfg.Pos + ", Stil " + LightCfg.CurStyle + ", Schaden " + LightLive.Damaged); });
            Imgui.Space(8);
        }

        private static void Colour(string label, string prefix)
        {
            Imgui.Label(label + ":");
            Imgui.BeginH();
            foreach (var ch in new[] { "R", "G", "B" })
            {
                var o = Settings.Get(prefix + ch);
                if (o == null) continue;
                Imgui.Label(ch + " " + Mathf.RoundToInt(o.Value), 60);
                o.Value = Mathf.Round(Imgui.Slider(Mathf.Clamp(o.Value, 0, 255), 0, 255, 140));
            }
            Imgui.EndH();
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/MapColors.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections;
    using global::System.Collections.Generic;
    using global::System.Globalization;
    using global::System.IO;
    using global::System.Reflection;
    using global::System.Text;
    using global::HarmonyLib;
    using global::TMPro;
    using global::UnityEngine;
    using global::UBOAT.Game;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.UI.MainMenu;

    // Readable map: grid square labels (AN15 ...) readable on every map skin, two extra skins
    // ("Elfenbein" and "Eigene Farben" with colours set in F10 > Karte), detailed per-skin values in
    // toolkit-map.txt (re-read while the game runs), plus a pixel measurement of the finished frame
    // and a skin report for the log.
    internal static class MapCfg
    {
        internal const string IvoryName = "Elfenbein";
        internal const string CustomName = "Eigene Farben";
        private static readonly Dictionary<string, string> v = new Dictionary<string, string>(StringComparer.Ordinal);
        private static DateTime stamp;
        private static float nextPoll = -1f;
        internal static int Generation;
        internal static string File => Path.Combine(Settings.Folder, "toolkit-map.txt");

        internal const string Defaults =
            "# Karten-Lesbarkeit (UBOAT Toolkit). Zeile: Schluessel: Wert. Farben als #RRGGBB oder #RRGGBBAA.\n" +
            "# Pro Karten-Stil geht auch: GridLabelColor.<Stil>, PlaceNameColor.<Stil>, GridLineColor.<Stil>,\n" +
            "#   GridLabelFont.<Stil>, GridLabelFontMaterial.<Stil>, GridLabelFaceColor.<Stil>, GridLabelFaceDilate.<Stil>,\n" +
            "#   GridLabelOutline.<Stil>, GridLabelOutlineColor.<Stil>, GridLabelOutlineWidth.<Stil>\n" +
            "# Stile im Spiel: Default, Semi-Realistic, Desaturated, Paper-Like (+ Elfenbein, Eigene Farben)\n" +
            "GridLabelFont: FrankRuhlLibre-Black SDF\n" +
            "GridLabelFontMaterial: FrankRuhlLibre-Black SDF Map Geographic Name\n" +
            "GridLabelFaceOverride: true\n" +
            "GridLabelFaceColor: #FFFFFFFF\n" +
            "GridLabelFaceDilate: 0.02\n" +
            "GridLabelFaceDilateByDepth: 1=0.02, 2=0.02, 3=0.05, 4=0.09, 5=0.13\n" +
            "GridLabelOutline: false\n" +
            "GridLabelOutlineColor: #EDE7D8C0\n" +
            "GridLabelOutlineWidth: 0.15\n" +
            "GridLabelOutlineWidthByDepth: 1=0.12, 2=0.12, 3=0.15, 4=0.18, 5=0.20\n" +
            "GridLabelAlphaFloor: 255\n" +
            "GridLabelNormalizeSize: false\n" +
            "GridLabelBaseFontSize: 32\n" +
            "IvoryBaseSkin: Paper-Like\n" +
            "IvoryLandColors: _BaseColor=#DFD7C4, _SceneColor=#DFD7C4\n" +
            "IvoryLandFloats: _GlobalColorIntensity=0.20\n" +
            "IvorySeaColors: _BaseColor=#EDE8DB, _Color=#B9B2A0\n" +
            "IvorySeaFloats: _GlobalColorIntensity=0.16\n" +
            "PixelSampleSeconds: 6\n" +
            "Custom.Sea: #CECEC9\n" +
            "Custom.Land: #BFB499\n" +
            "Custom.Grid: #1A1A1A\n" +
            "Custom.Place: #4D4D4D\n" +
            "Custom.Neutral: #808080\n" +
            "Custom.Allies: #2E394D\n" +
            "Custom.Axis: #4D2A26\n";

        internal static void Poll(bool force = false)
        {
            if (!force && Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 1f;
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                if (!System.IO.File.Exists(File)) System.IO.File.WriteAllText(File, Defaults);
                var t = System.IO.File.GetLastWriteTimeUtc(File);
                if (!force && t == stamp && v.Count > 0) return;
                stamp = t;
                v.Clear();
                Parse(Defaults);
                Parse(System.IO.File.ReadAllText(File));
                Generation++;
            }
            catch (Exception e) { Util.Log("Karte: toolkit-map.txt nicht lesbar: " + e.Message); if (v.Count == 0) Parse(Defaults); }
        }
        private static void Parse(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                int i = l.IndexOf(':');
                if (i <= 0) continue;
                v[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
            }
        }
        internal static void Write(string key, string value)
        {
            Poll(true);
            try
            {
                var lines = new List<string>(System.IO.File.ReadAllLines(File));
                bool done = false;
                for (int i = 0; i < lines.Count; i++)
                    if (lines[i].TrimStart().StartsWith(key + ":")) { lines[i] = key + ": " + value; done = true; }
                if (!done) lines.Add(key + ": " + value);
                System.IO.File.WriteAllLines(File, lines.ToArray());
            }
            catch { }
            v[key] = value;
            Generation++;
        }

        internal static string Str(string key) { Poll(); return v.TryGetValue(key, out var s) && s.Length > 0 ? s : null; }
        internal static string PerSkin(string key, string skin) => (skin != null ? Str(key + "." + skin) : null) ?? Str(key);
        internal static bool Bool(string key, bool def) { var s = Str(key); return s != null && bool.TryParse(s, out var b) ? b : def; }
        internal static float Float(string key, float def) { var s = Str(key); return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def; }
        internal static Color32? Color(string key) { var s = Str(key); return s != null && TryColor(s, out var c) ? c : (Color32?)null; }
        internal static Color32? SkinColor(string prefix, string skin) => string.IsNullOrEmpty(skin) ? null : Color(prefix + skin);

        internal static Dictionary<string, float> ByDepth(string key)
        {
            var d = new Dictionary<string, float>();
            var s = Str(key);
            if (s == null) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) d[kv[0].Trim()] = f;
            }
            return d;
        }
        internal static Dictionary<string, Color32> ColorList(string key)
        {
            var d = new Dictionary<string, Color32>();
            var s = Str(key);
            if (s == null) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && TryColor(kv[1], out var c)) d[kv[0].Trim()] = c;
            }
            return d;
        }
        internal static Dictionary<string, float> FloatList(string key)
        {
            var d = new Dictionary<string, float>();
            var s = Str(key);
            if (s == null) return d;
            foreach (var part in s.Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) d[kv[0].Trim()] = f;
            }
            return d;
        }

        internal static bool TryColor(string s, out Color32 c)
        {
            c = new Color32(0, 0, 0, 255);
            s = s.Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return false;
            try
            {
                byte P(int i) => byte.Parse(s.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                c = new Color32(P(0), P(2), P(4), s.Length == 8 ? P(6) : (byte)255);
                return true;
            }
            catch { return false; }
        }
        internal static string Hex(Color32 c) => "#" + c.r.ToString("X2") + c.g.ToString("X2") + c.b.ToString("X2") + (c.a != 255 ? c.a.ToString("X2") : "");

        // Skins whose grid text borrows the paper map's ink (vanilla light grey text is unreadable there).
        internal static bool UsesPaperInk(string skin) => skin == "Default" || skin == "Semi-Realistic" || skin == "Desaturated";
        internal static bool GridLikeSeaNames(string skin) => UsesPaperInk(skin) || skin == "Paper-Like";
        internal const string InkSource = "Paper-Like";
    }

    internal static class MapSkins
    {
        internal static MapRenderer Map;
        internal static NavalGridRenderer Grid;
        private static readonly Dictionary<string, Color32> vanillaGrid = new Dictionary<string, Color32>();
        private static readonly Dictionary<string, Color32[]> vanillaPlace = new Dictionary<string, Color32[]>();
        private static Material ownLand, ownSea;
        private static string ownFor;
        private static readonly Dictionary<int, Material> ownedLabelMats = new Dictionary<int, Material>();
        private static Material ownedBase; private static string ownedSkin; private static int ownedGen = -1;
        private static Material prefabMat; private static TMP_FontAsset prefabFont;
        private static bool geoCaptured, everNormalized; private static Vector3 prefabScale = Vector3.one; private static Vector2 prefabSize;
        private static readonly Dictionary<MapLabel, int> placeClass = new Dictionary<MapLabel, int>();
        internal static bool StyledOnce;

        internal static bool On => Settings.B("MapReadable");
        internal static bool IvoryOn => On && Settings.B("MapIvory");
        internal static string Current => Map?.CurrentMapSkin?.name;
        internal static bool IsCustom(string s) => s == MapCfg.CustomName;
        internal static bool OwnSkinNow => (IvoryOn && Current == MapCfg.IvoryName) || (On && IsCustom(Current));

        internal static List<MapSkin> Skins(MapRenderer m) => m == null ? null : Traverse.Create(m).Field("mapSkins").GetValue<List<MapSkin>>();
        private static T F<T>(object o, string f) => o == null ? default(T) : Traverse.Create(o).Field(f).GetValue<T>();

        internal static Color Custom(string what)
        {
            MapCfg.TryColor(MapCfg.Str("Custom." + what) ?? "#808080", out var c);
            return c;
        }

        // ---------- label colours ----------
        internal static void ApplyLabelColors(List<MapSkin> skins)
        {
            if (skins == null) return;
            foreach (var s in skins) if (s != null && !string.IsNullOrEmpty(s.name) && !vanillaGrid.ContainsKey(s.name)) vanillaGrid[s.name] = s.gridLabelColor;
            foreach (var s in skins) if (s != null && !string.IsNullOrEmpty(s.name)) s.gridLabelColor = LabelColor(s.name);
        }
        internal static Color32 LabelColor(string skin)
        {
            if (!vanillaGrid.TryGetValue(skin, out var vanilla)) vanilla = new Color32(33, 33, 33, 192);
            if (!On) return vanilla;
            if (IsCustom(skin)) { Color32 g = Custom("Grid"); g.a = 255; return g; }
            var exp = MapCfg.SkinColor("GridLabelColor.", skin);
            if (exp.HasValue)
            {
                var c = exp.Value;   // old "white/near-black" defaults on borrowed-ink skins mean "not set"
                bool stale = MapCfg.UsesPaperInk(skin) && c.a == 255 && ((c.r == 0x1A && c.g == 0x1A && c.b == 0x1A) || (c.r == 0xF5 && c.g == 0xF2 && c.b == 0xEA) || (c.r == 255 && c.g == 255 && c.b == 255) || (c.r == 0xF2 && c.g == 0xF2 && c.b == 0xF2));
                if (!stale) return c;
            }
            if (MapCfg.GridLikeSeaNames(skin))
            {
                var o = MapCfg.Color("BorrowedInkColor");
                if (o.HasValue) return o.Value;
                if (SeaInk(skin, out var ink)) return ink;
            }
            if (vanilla.a == 0) return vanilla;
            byte floor = (byte)Mathf.Clamp(MapCfg.Float("GridLabelAlphaFloor", 255), 0, 255);
            if (vanilla.a < floor) vanilla.a = floor;
            return vanilla;
        }
        private static bool SeaInk(string skin, out Color32 ink)
        {
            ink = default(Color32);
            var exp = On ? MapCfg.SkinColor("PlaceNameColor.", skin) : null;
            if (exp.HasValue) { ink = exp.Value; return true; }
            string src = MapCfg.UsesPaperInk(skin) ? MapCfg.InkSource : skin;
            if (!vanillaPlace.TryGetValue(src, out var b) && !vanillaPlace.TryGetValue(skin, out b)) return false;
            if (b == null || b.Length == 0) return false;
            ink = b[0];
            if (ink.a < 0x80) ink.a = 0xE6;
            return true;
        }
        internal static void ApplyPlaceColors(List<MapSkin> skins)
        {
            if (skins == null) return;
            foreach (var s in skins)
                if (s != null && !string.IsNullOrEmpty(s.name) && !IsCustom(s.name) && !vanillaPlace.ContainsKey(s.name))
                    vanillaPlace[s.name] = new[] { s.customLabelColor1, s.customLabelColor2, s.customLabelColor3 };
            vanillaPlace.TryGetValue(MapCfg.InkSource, out var ink);
            foreach (var s in skins)
            {
                if (s == null || string.IsNullOrEmpty(s.name) || IsCustom(s.name) || !vanillaPlace.ContainsKey(s.name)) continue;
                var van = vanillaPlace[s.name];
                var basis = On && MapCfg.UsesPaperInk(s.name) && ink != null ? ink : van;
                var c = On ? MapCfg.SkinColor("PlaceNameColor.", s.name) : null;
                s.customLabelColor1 = c ?? basis[0];
                s.customLabelColor2 = c ?? basis[1];
                s.customLabelColor3 = c ?? basis[2];
            }
        }
        internal static bool ChangesPlaceNames(string skin) =>
            skin != null && vanillaPlace.ContainsKey(skin) && On && (MapCfg.SkinColor("PlaceNameColor.", skin).HasValue || MapCfg.UsesPaperInk(skin));

        internal static void RecolorGridLabels()
        {
            var sq = F<Dictionary<string, NavalGridRendererSquare>>(Grid, "squares");
            string skin = Current;
            if (sq == null || skin == null) return;
            Color c = LabelColor(skin);
            foreach (var p in sq)
            {
                var l = p.Value?.Label;
                if (l == null) continue;
                l.color = c;
                Style(l, skin, p.Value.Square == null ? -1 : p.Value.Square.Depth);
            }
        }

        // ---------- label font, material, outline, size ----------
        internal static void Style(TextMeshProUGUI label, string skin, int depth)
        {
            if (label == null || Grid == null) return;
            if (On) StyledOnce = true;
            var prefab = F<TextMeshProUGUI>(Grid, "labelPrefab");
            if (prefab == null) return;
            Normalize(label, prefab, depth);
            if (prefabFont == null) prefabFont = prefab.font;
            if (prefabMat == null) prefabMat = prefab.fontSharedMaterial;
            if (!FontPair(skin, out var font, out var mat)) return;
            mat = OwnedMat(skin, mat, depth);
            if (font != null && label.font != font) label.font = font;
            if (mat != null && label.fontSharedMaterial != mat) label.fontSharedMaterial = mat;
        }
        private static bool FontPair(string skin, out TMP_FontAsset font, out Material mat)
        {
            font = prefabFont; mat = prefabMat;
            string fn = On ? MapCfg.PerSkin("GridLabelFont", skin) : null, mn = On ? MapCfg.PerSkin("GridLabelFontMaterial", skin) : null;
            if (fontInfoLogged.Add("state|" + skin + "|" + On + "|" + fn + "|" + mn))
                Util.Log("Karte: Stil '" + skin + "', Lesbarkeit an: " + On + ", Schrift-Wunsch '" + (fn ?? "-") + "' / '" + (mn ?? "-") + "', Spiel-Schrift '" + (prefabFont != null ? prefabFont.name : "-") + "' / '" + (prefabMat != null ? prefabMat.name : "-") + "'");
            if (!On) return mat != null;
            if (fn == null && mn == null) return mat != null;
            var f = fn == null ? prefabFont : FindFont(fn);
            var m = mn == null ? prefabMat : FindFontMat(mn);
            if (f == null || m == null) { Util.Log("Karte: Schrift '" + fn + "' / '" + mn + "' nicht gefunden, Spiel-Schrift bleibt"); return mat != null; }
            // A font only draws correctly with a material of ITS OWN atlas. Font set without a matching material
            // (e.g. GridLabelFont=FrankRuhlLibre-Black SDF, material left at the game's) = the letters are cut out of
            // the wrong atlas -> garbled blocks on the map. Then use the font's own material.
            var tex = m.HasProperty(ShaderUtilities.ID_MainTex) ? m.GetTexture(ShaderUtilities.ID_MainTex) : null;
            if (fontInfoLogged.Add(f.name + "|" + m.name))
                Util.Log("Karte: Planquadrat-Schrift '" + f.name + "' (wie Spiel: " + (f == prefabFont) + ", Atlas '" + (f.atlasTexture != null ? f.atlasTexture.name : "-") + "'), Material '" + m.name +
                         "' (Atlas '" + (tex != null ? tex.name : "-") + "', passt: " + (tex == f.atlasTexture) + "), Spiel-Schrift '" + (prefabFont != null ? prefabFont.name : "-") + "' / '" + (prefabMat != null ? prefabMat.name : "-") + "'");
            if (f.atlasTexture != null && tex != null && tex != f.atlasTexture)
            {
                if (fontMismatchLogged.Add(f.name + "|" + m.name)) Util.Log("Karte: Schrift '" + f.name + "' passt nicht zum Material '" + m.name + "', nehme das eigene Material der Schrift");
                m = f.material;
            }
            font = f; mat = m;
            return mat != null;
        }
        private static readonly HashSet<string> fontMismatchLogged = new HashSet<string>();
        private static readonly HashSet<string> fontInfoLogged = new HashSet<string>();
        private static TMP_FontAsset FindFont(string n)
        {
            var fonts = F<TMP_FontAsset[]>(Map, "fonts");
            if (fonts != null) foreach (var f in fonts) if (f != null && f.name == n) return f;
            return null;
        }
        private static Material FindFontMat(string n)
        {
            if (Map == null) return null;
            var m = Map.GetFontMaterial(n);
            if (m != null && m.name == n) return m;
            var all = F<Material[]>(Map, "fontMaterials");
            if (all != null) foreach (var x in all) if (x != null && x.name == n) return x;
            return null;
        }
        private static Material OwnedMat(string skin, Material baseMat, int depth)
        {
            if (baseMat == null) return null;
            bool need = On && (MapCfg.Bool("GridLabelFaceOverride", true) || OutlineOn(skin));
            if (!need && !ZDiffers(baseMat)) return baseMat;
            if (ownedGen != MapCfg.Generation || ownedSkin != skin || ownedBase != baseMat)
            {
                foreach (var p in ownedLabelMats) if (p.Value != null) UnityEngine.Object.Destroy(p.Value);
                ownedLabelMats.Clear();
                ownedGen = MapCfg.Generation; ownedSkin = skin; ownedBase = baseMat;
            }
            int key = depth < 0 ? -1 : depth;
            if (ownedLabelMats.TryGetValue(key, out var cached) && cached != null) return cached;
            var m = Copy(baseMat);
            m.name = baseMat.name + " (Toolkit d" + key + ")";
            if (On && MapCfg.Bool("GridLabelFaceOverride", true))
            {
                var face = MapCfg.SkinColor("GridLabelFaceColor.", skin) ?? MapCfg.Color("GridLabelFaceColor") ?? new Color32(255, 255, 255, 255);
                if (m.HasProperty("_FaceColor")) m.SetColor("_FaceColor", face);
                if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", Dilate(skin, depth));
            }
            if (On && OutlineOn(skin))
            {
                var oc = MapCfg.SkinColor("GridLabelOutlineColor.", skin) ?? MapCfg.Color("GridLabelOutlineColor") ?? new Color32(0xED, 0xE7, 0xD8, 0xC0);
                if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", oc);
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", OutlineWidth(skin, depth));
                m.EnableKeyword("OUTLINE_ON");
            }
            if (prefabMat != null && m.HasProperty("_ZTest") && prefabMat.HasProperty("_ZTest")) m.SetFloat("_ZTest", prefabMat.GetFloat("_ZTest"));   // no z-fighting with the chart
            ownedLabelMats[key] = m;
            return m;
        }
        private static bool ZDiffers(Material m) => prefabMat != null && m != prefabMat && m.HasProperty("_ZTest") && prefabMat.HasProperty("_ZTest") && !Mathf.Approximately(m.GetFloat("_ZTest"), prefabMat.GetFloat("_ZTest"));
        private static bool OutlineOn(string skin) { var s = skin != null ? MapCfg.Str("GridLabelOutline." + skin) : null; return s != null && bool.TryParse(s, out var b) ? b : MapCfg.Bool("GridLabelOutline", false); }
        private static float Dilate(string skin, int depth)
        {
            if (depth >= 0 && MapCfg.ByDepth("GridLabelFaceDilateByDepth").TryGetValue(depth.ToString(), out var d)) return Mathf.Clamp(d, -1, 1);
            return Mathf.Clamp(MapCfg.Float("GridLabelFaceDilate." + skin, MapCfg.Float("GridLabelFaceDilate", 0.02f)), -1, 1);
        }
        private static float OutlineWidth(string skin, int depth)
        {
            if (depth >= 0 && MapCfg.ByDepth("GridLabelOutlineWidthByDepth").TryGetValue(depth.ToString(), out var d)) return Mathf.Clamp01(d);
            return Mathf.Clamp01(MapCfg.Float("GridLabelOutlineWidth." + skin, MapCfg.Float("GridLabelOutlineWidth", 0.15f)));
        }
        private static void Normalize(TextMeshProUGUI label, TextMeshProUGUI prefab, int depth)
        {
            var rect = label.rectTransform; var pr = prefab.rectTransform;
            if (rect == null || pr == null) return;
            if (!geoCaptured) { geoCaptured = true; prefabScale = pr.localScale; prefabSize = pr.sizeDelta; }
            bool on = On && MapCfg.Bool("GridLabelNormalizeSize", false) && depth >= 0;
            if (!on)
            {
                if (!everNormalized) return;
                rect.localScale = prefabScale; rect.sizeDelta = prefabSize;
                return;
            }
            float baseSize = MapCfg.Float("GridLabelBaseFontSize", 32);
            float vanillaSize = prefab.fontSize / Mathf.Pow(4f, Mathf.Max(0, depth - 2));
            if (baseSize <= 0f || vanillaSize <= 0f) return;
            float scale = vanillaSize / baseSize;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            everNormalized = true;
            rect.sizeDelta = prefabSize / scale;
            label.fontSize = baseSize;
            rect.localScale = prefabScale * scale;
        }

        internal static void GridLineColor()
        {
            if (Grid == null || !On || Current == null) return;
            var want = MapCfg.SkinColor("GridLineColor.", Current);
            if (!want.HasValue) return;
            Traverse.Create(Grid).Field("originalGridColor").SetValue((Color)want.Value);
        }

        // ---------- extra skins ----------
        private static MapSkin Find(List<MapSkin> l, string n) { if (n != null) foreach (var s in l) if (s != null && s.name == n) return s; return null; }
        private static MapSkin Clone(MapSkin src)
        {
            var c = new MapSkin();
            foreach (var f in typeof(MapSkin).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) try { f.SetValue(c, f.GetValue(src)); } catch { }
            return c;
        }
        internal static void EnsureExtraSkins(List<MapSkin> skins)
        {
            if (skins == null || !On) return;
            if (IvoryOn && Find(skins, MapCfg.IvoryName) == null)
            {
                var b = Find(skins, MapCfg.Str("IvoryBaseSkin")) ?? Find(skins, "Paper-Like") ?? Find(skins, "Default");
                if (b != null) { var s = Clone(b); s.name = MapCfg.IvoryName; s.gridLabelColor = new Color32(0x2A, 0x26, 0x22, 0xFF); skins.Add(s); }
            }
            var cs = Find(skins, MapCfg.CustomName);
            if (cs == null)
            {
                var b = Find(skins, "Paper-Like") ?? Find(skins, "Default");
                if (b == null) return;
                cs = Clone(b); cs.name = MapCfg.CustomName; skins.Add(cs);
            }
            ApplyCustom(cs);
        }
        internal static void ApplyCustom(MapSkin s)
        {
            if (s == null) return;
            Color32 O(string w) { Color32 c = Custom(w); c.a = 255; return c; }
            s.gridLabelColor = O("Grid");
            s.customLabelColor1 = s.customLabelColor2 = s.customLabelColor3 = O("Place");
            s.neutralLabelColor = O("Neutral");
            s.alliesLabelColor = s.alliesPuppetLabelColor = O("Allies");
            s.axisLabelColor = s.axisPuppetLabelColor = O("Axis");
        }
        internal static List<string> ExtraNames()
        {
            var n = new List<string>();
            if (!On) return n;
            if (IvoryOn) n.Add(MapCfg.IvoryName);
            n.Add(MapCfg.CustomName);
            return n;
        }

        private static Dictionary<string, Color32> OwnColors(string what)
        {
            var d = MapCfg.ColorList(what == "land" ? "IvoryLandColors" : "IvorySeaColors");
            if (IsCustom(Current)) { Color32 p = Custom(what == "land" ? "Land" : "Sea"); p.a = 255; d["_BaseColor"] = p; if (what == "land") d["_SceneColor"] = p; }
            return d;
        }
        private static Dictionary<string, float> OwnFloats(string what) => MapCfg.FloatList(what == "land" ? "IvoryLandFloats" : "IvorySeaFloats");

        internal static void ApplyOwnMaterials()
        {
            if (Map == null) return;
            if (!OwnSkinNow) { ClearOwn(); ownFor = null; return; }
            var land = F<MeshRenderer>(Map, "mapLandRenderer");
            var sea = F<MeshRenderer>(Map, "mapSeaRenderer");
            if (ownLand != null && land != null && land.sharedMaterial == ownLand && ownFor == Current) return;
            ownFor = Current;
            ClearOwn();
            if (land != null && land.sharedMaterial != null)
            {
                ownLand = Tinted(land.sharedMaterial, "land");
                land.sharedMaterial = ownLand;
                Traverse.Create(Map).Field("landMaterial").SetValue(ownLand);
            }
            if (sea != null)
            {
                var arr = sea.sharedMaterials;
                if (arr != null && arr.Length > 0 && arr[0] != null)
                {
                    ownSea = Tinted(arr[0], "sea");
                    arr[0] = ownSea;
                    sea.sharedMaterials = arr;
                    Traverse.Create(Map).Field("seaMaterial").SetValue(ownSea);
                }
            }
        }
        internal static void RetintOwn()
        {
            if (!OwnSkinNow) return;
            if (ownLand != null) Tint(ownLand, "land");
            if (ownSea != null) Tint(ownSea, "sea");
        }
        private static void ClearOwn()
        {
            if (ownLand != null) UnityEngine.Object.Destroy(ownLand);
            if (ownSea != null) UnityEngine.Object.Destroy(ownSea);
            ownLand = ownSea = null;
        }
        private static Material Copy(Material src) { var m = new Material(src); m.renderQueue = src.renderQueue; m.shaderKeywords = src.shaderKeywords; return m; }
        private static Material Tinted(Material src, string what) { var m = Copy(src); m.name = src.name + " (Toolkit)"; Tint(m, what); return m; }
        private static void Tint(Material m, string what)
        {
            foreach (var p in OwnColors(what)) if (m.HasProperty(p.Key)) m.SetColor(p.Key, p.Value); else Util.Log("Karte: Material " + m.name + " hat keine Farbe " + p.Key);
            foreach (var p in OwnFloats(what)) if (m.HasProperty(p.Key)) m.SetFloat(p.Key, p.Value); else Util.Log("Karte: Material " + m.name + " hat keinen Wert " + p.Key);
        }

        // ---------- place and country names (game paints them only once) ----------
        internal static void RecordPlaceClasses(MapRenderer map)
        {
            placeClass.Clear();
            Util.Safe("Map.Places", () =>
            {
                var list = F<List<MapDecoration>>(map, "mapDecorations");
                var data = Traverse.Create(typeof(MapRenderer)).Field("gameData").GetValue<GameData>();
                if (list == null || data == null) return;
                var rows = data.GetDataRows("Graphics/Map Labels");
                if (rows == null || rows.Length > list.Count) return;
                int start = list.Count - rows.Length;
                for (int i = 0; i < rows.Length; i++)
                {
                    var l = list[start + i] as MapLabel;
                    if (l == null || l.Country != null) continue;
                    int c = (int)rows[i].GetParameter(10);
                    placeClass[l] = c == 2 || c == 3 ? c : 1;
                }
            });
        }
        private static string lastSkin;
        internal static void OnSkinChanged()
        {
            string now = Current;
            bool involved = IsCustom(now) || IsCustom(lastSkin) || ChangesPlaceNames(now) || ChangesPlaceNames(lastSkin);
            lastSkin = now;
            if (involved) RepaintNames();
        }
        internal static void RepaintNames()
        {
            Util.Safe("Map.Repaint", () =>
            {
                var skin = Map?.CurrentMapSkin;
                var list = F<List<MapDecoration>>(Map, "mapDecorations");
                if (skin == null || list == null) return;
                var aff = AccessTools.Method(typeof(MapLabel), "AllegianceOnAffiliationChanged");
                foreach (var d in list)
                {
                    var l = d as MapLabel;
                    if (l == null) continue;
                    if (l.Country != null) { aff?.Invoke(l, new object[] { l.Country }); continue; }
                    if (!placeClass.TryGetValue(l, out int c)) continue;
                    var t = l.GetComponent<TextMeshProUGUI>();
                    if (t != null) t.color = c == 3 ? skin.customLabelColor3 : c == 2 ? skin.customLabelColor2 : skin.customLabelColor1;
                }
            });
        }

        internal static void ReapplyAll()
        {
            Util.Safe("Map.Reapply", () =>
            {
                var skins = Skins(Map);
                if (skins != null) { EnsureExtraSkins(skins); ApplyPlaceColors(skins); ApplyLabelColors(skins); }
                ApplyOwnMaterials();
                RetintOwn();
                GridLineColor();
                RecolorGridLabels();
                RepaintNames();
            });
        }

        // ---------- report for the log ----------
        internal static void Report()
        {
            var sb = new StringBuilder("Karten-Bericht:\n");
            var skins = Skins(Map);
            if (skins == null) { Util.Log("Karten-Bericht: Karte noch nicht geladen (einmal Karte öffnen)."); return; }
            sb.Append("aktueller Stil: ").Append(Current).Append('\n');
            foreach (var s in skins)
            {
                if (s == null) continue;
                sb.Append("- ").Append(s.name).Append(": Raster-Text ").Append(MapCfg.Hex(s.gridLabelColor)).Append(", Orte ").Append(MapCfg.Hex(s.customLabelColor1))
                  .Append("/").Append(MapCfg.Hex(s.customLabelColor2)).Append("/").Append(MapCfg.Hex(s.customLabelColor3))
                  .Append(", neutral ").Append(MapCfg.Hex(s.neutralLabelColor)).Append(", Alliierte ").Append(MapCfg.Hex(s.alliesLabelColor)).Append(", Achse ").Append(MapCfg.Hex(s.axisLabelColor))
                  .Append(", Land ").Append(s.landMaterialPath).Append(", See ").Append(s.seaMaterialPath).Append('\n');
            }
            void Mat(string what, Material m)
            {
                if (m == null) return;
                sb.Append(what).Append(" Material ").Append(m.name).Append(" (").Append(m.shader != null ? m.shader.name : "?").Append("):");
                foreach (var p in new[] { "_BaseColor", "_SceneColor", "_Color", "_FaceColor", "_OutlineColor" }) if (m.HasProperty(p)) sb.Append(' ').Append(p).Append('=').Append(MapCfg.Hex((Color32)m.GetColor(p)));
                foreach (var p in new[] { "_GlobalColorIntensity", "_FaceDilate", "_OutlineWidth", "_ZTest" }) if (m.HasProperty(p)) sb.Append(' ').Append(p).Append('=').Append(m.GetFloat(p).ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            Mat("Land", F<MeshRenderer>(Map, "mapLandRenderer")?.sharedMaterial);
            var sea = F<MeshRenderer>(Map, "mapSeaRenderer")?.sharedMaterials;
            if (sea != null && sea.Length > 0) Mat("See", sea[0]);
            var prefab = F<TextMeshProUGUI>(Grid, "labelPrefab");
            if (prefab != null) Mat("Raster-Text", prefab.fontSharedMaterial);
            var fonts = F<TMP_FontAsset[]>(Map, "fonts");
            if (fonts != null) { sb.Append("Schriften:"); foreach (var f in fonts) if (f != null) sb.Append(" '").Append(f.name).Append('\''); sb.Append('\n'); }
            var fm = F<Material[]>(Map, "fontMaterials");
            if (fm != null) { sb.Append("Schrift-Materialien:"); foreach (var m in fm) if (m != null) sb.Append(" '").Append(m.name).Append('\''); sb.Append('\n'); }
            if (Grid != null) sb.Append("Raster-Ebene: ").Append(F<int>(Grid, "displayedDepth")).Append('\n');
            Util.Log(sb.ToString());
            ToolkitRunner.ShowNotice("Karten-Bericht ins Toolkit-Log geschrieben", 4f);
        }
    }

    // Host: polls the file, notices switch changes, repaints after colour edits, pixel measurement.
    internal class MapHost : MonoBehaviour
    {
        private static MapHost me;
        private bool lastOn, lastIvory;
        private int lastGen = -1;
        internal static bool RepaintPending;
        private float nextRepaint, nextSample;
        private bool sampling;
        private readonly HashSet<string> sampled = new HashSet<string>();
        private int sampledGen = -1;
        private static MethodInfo capture; private static bool captureLooked;

        internal static void Ensure()
        {
            if (me != null) return;
            var go = new GameObject("Toolkit Map");
            DontDestroyOnLoad(go);
            me = go.AddComponent<MapHost>();
            me.lastOn = MapSkins.On; me.lastIvory = MapSkins.IvoryOn;
        }

        private void Update()
        {
            MapCfg.Poll();
            if (MapSkins.Map == null) return;
            if (MapSkins.On != lastOn || MapSkins.IvoryOn != lastIvory || MapCfg.Generation != lastGen)
            {
                lastOn = MapSkins.On; lastIvory = MapSkins.IvoryOn; lastGen = MapCfg.Generation;
                MapSkins.ReapplyAll();
            }
            if (RepaintPending && Time.unscaledTime >= nextRepaint)
            {
                RepaintPending = false; nextRepaint = Time.unscaledTime + 0.1f;
                if (MapSkins.IsCustom(MapSkins.Current)) { MapSkins.RecolorGridLabels(); MapSkins.RepaintNames(); }
            }
            Util.Safe("Map.Pixel", Sample);
        }

        // ---------- pixel measurement of a real rendered grid label ----------
        private void Sample()
        {
            if (!Settings.B("MapPixelSample") || sampling || MapSkins.Grid == null) return;
            if (sampledGen != MapCfg.Generation) { sampledGen = MapCfg.Generation; sampled.Clear(); }
            if (Time.unscaledTime < nextSample) return;
            var cam = Traverse.Create(typeof(NavalGridRenderer)).Field("mainCamera").GetValue<MainCamera>();
            if (cam == null || cam.CurrentMode != CameraMode.Map) return;
            int depth = Traverse.Create(MapSkins.Grid).Field("displayedDepth").GetValue<int>();
            string key = MapSkins.Current + "|" + depth;
            if (sampled.Contains(key)) return;
            var c = cam.GetComponent<Camera>();
            var label = FindLabel(c);
            if (label == null) return;
            nextSample = Time.unscaledTime + Mathf.Max(2f, MapCfg.Float("PixelSampleSeconds", 6));
            sampled.Add(key);
            StartCoroutine(Capture(c, label, MapSkins.Current, depth));
        }
        private static TextMeshProUGUI FindLabel(Camera cam)
        {
            var sq = Traverse.Create(MapSkins.Grid).Field("squares").GetValue<Dictionary<string, NavalGridRendererSquare>>();
            if (sq == null) return null;
            foreach (var p in sq)
            {
                var s = p.Value;
                if (s == null || !s.IsVisible) continue;
                var l = s.Label;
                if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || string.IsNullOrEmpty(l.text)) continue;
                if (cam != null)
                {
                    var sp = cam.WorldToScreenPoint(l.transform.position);
                    if (sp.z <= 0f || sp.x < 48f || sp.y < 48f || sp.x > Screen.width - 48f || sp.y > Screen.height - 48f) continue;
                }
                return l;
            }
            return null;
        }
        private IEnumerator Capture(Camera cam, TextMeshProUGUI label, string skin, int depth)
        {
            sampling = true;
            yield return new WaitForEndOfFrame();
            Texture2D shot = null;
            try
            {
                if (!captureLooked)
                {
                    captureLooked = true;
                    var t = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule") ?? Type.GetType("UnityEngine.ScreenCapture, UnityEngine");
                    capture = t?.GetMethod("CaptureScreenshotAsTexture", Type.EmptyTypes);
                    if (capture == null) Util.Log("Karte: Bildschirm-Aufnahme nicht verfügbar, Pixel-Messung aus");
                }
                shot = capture?.Invoke(null, null) as Texture2D;
                if (shot != null && cam != null && label != null) Measure(shot, cam, label, skin, depth);
            }
            catch (Exception e) { Util.Log("Karte: Pixel-Messung fehlgeschlagen: " + e.Message); }
            finally { if (shot != null) Destroy(shot); sampling = false; }
        }
        private static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        private static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        private static float RelLum(Color c) => 0.2126f * Lin(c.r) + 0.7152f * Lin(c.g) + 0.0722f * Lin(c.b);
        private static float Ratio(float a, float b) => (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f);

        private static void Measure(Texture2D shot, Camera cam, TextMeshProUGUI label, string skin, int depth)
        {
            Bounds b = label.textBounds;
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f) b = label.bounds;
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f) b = new Bounds(Vector3.zero, new Vector3(label.preferredWidth, label.preferredHeight, 0f));
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var cc = b.center; var e = b.extents;
            foreach (var corner in new[] { new Vector3(cc.x - e.x, cc.y - e.y, cc.z), new Vector3(cc.x + e.x, cc.y - e.y, cc.z), new Vector3(cc.x + e.x, cc.y + e.y, cc.z), new Vector3(cc.x - e.x, cc.y + e.y, cc.z) })
            {
                var sp = cam.WorldToScreenPoint(label.transform.TransformPoint(corner));
                if (sp.z <= 0f) return;
                minX = Mathf.Min(minX, sp.x); minY = Mathf.Min(minY, sp.y); maxX = Mathf.Max(maxX, sp.x); maxY = Mathf.Max(maxY, sp.y);
            }
            float sx = Screen.width > 0 ? (float)shot.width / Screen.width : 1f, sy = Screen.height > 0 ? (float)shot.height / Screen.height : 1f;
            int x0 = Mathf.FloorToInt(minX * sx), y0 = Mathf.FloorToInt(minY * sy), x1 = Mathf.CeilToInt(maxX * sx), y1 = Mathf.CeilToInt(maxY * sy);
            int w = x1 - x0, h = y1 - y0;
            if (w < 4 || h < 4) return;
            int margin = Mathf.Clamp(Mathf.RoundToInt(h * 0.5f), 3, 24);
            int bx = Mathf.Clamp(x0 - margin, 0, shot.width - 1), by = Mathf.Clamp(y0 - margin, 0, shot.height - 1);
            int bw = Mathf.Clamp(x1 + margin, 0, shot.width) - bx, bh = Mathf.Clamp(y1 + margin, 0, shot.height) - by;
            if (bw < 4 || bh < 4) return;
            var block = shot.GetPixels(bx, by, bw, bh);
            var ring = new List<float>(); var ringC = new List<Color>(); var inner = new List<float>(); var innerC = new List<Color>();
            int ix0 = x0 - bx, iy0 = y0 - by, ix1 = ix0 + w, iy1 = iy0 + h;
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    var c = block[y * bw + x];
                    bool inside = x >= ix0 && x < ix1 && y >= iy0 && y < iy1;
                    if (inside) { inner.Add(Lum(c)); innerC.Add(c); } else { ring.Add(Lum(c)); ringC.Add(c); }
                }
            if (ring.Count < 8 || inner.Count < 8) return;
            var rs = new List<float>(ring); rs.Sort();
            float bg = rs[rs.Count / 2];
            Color bgC = ringC[0]; float bd = float.MaxValue;
            for (int i = 0; i < ringC.Count; i++) { float d = Mathf.Abs(ring[i] - bg); if (d < bd) { bd = d; bgC = ringC[i]; } }
            var sorted = new List<float>(inner); sorted.Sort();
            float p05 = sorted[Mathf.Clamp(Mathf.RoundToInt(0.05f * (sorted.Count - 1)), 0, sorted.Count - 1)];
            float p95 = sorted[Mathf.Clamp(Mathf.RoundToInt(0.95f * (sorted.Count - 1)), 0, sorted.Count - 1)];
            bool dark = (bg - p05) >= (p95 - bg);
            string inkFrom = "Messung";
            if (skin != null) { Color want = MapSkins.LabelColor(skin); if (want.a > 0.01f) { dark = Lum(want) < bg; inkFrom = "Einstellung"; } }
            float textL = dark ? p05 : p95;
            Color textC = innerC[0]; float ext = dark ? float.MaxValue : float.MinValue;
            foreach (var c in innerC) { float l = Lum(c); if (dark ? l < ext : l > ext) { ext = l; textC = c; } }
            int diff = 0, lighter = 0, darker = 0;
            foreach (var l in inner) { float d = l - bg; if (Mathf.Abs(d) > 0.04f) { diff++; if (d > 0) lighter++; else darker++; } }
            float cover = (float)diff / inner.Count;
            float eff = Ratio(textL, bg), peak = Ratio(RelLum(textC), RelLum(bgC));
            string verdict = eff < 1.5f ? "UNSICHTBAR" : eff < 2.2f ? "sehr schwer lesbar" : eff < 3f ? "grenzwertig" : cover < 0.04f ? "lesbar, aber sehr dünn" : "gut lesbar";
            var sb = new StringBuilder();
            sb.Append("Karte Pixel: Stil '").Append(skin).Append("' Ebene ").Append(depth).Append(" Text '").Append(label.text).Append("' ").Append(w).Append('x').Append(h).Append(" px")
              .Append(" | Hintergrund ").Append(MapCfg.Hex((Color32)bgC)).Append(" | Schrift ").Append(MapCfg.Hex((Color32)textC))
              .Append(" | Kontrast ").Append(eff.ToString("0.00", CultureInfo.InvariantCulture)).Append(" (Spitze ").Append(peak.ToString("0.00", CultureInfo.InvariantCulture)).Append(", Farbe aus ").Append(inkFrom).Append(')')
              .Append(" | Deckung ").Append((cover * 100f).ToString("0.0", CultureInfo.InvariantCulture)).Append("% (").Append(darker).Append(" dunkler, ").Append(lighter).Append(" heller)");
            if (darker > 8 && lighter > 8) sb.Append(" | Rand/Schein erkannt (").Append(dark ? "heller" : "dunkler").Append(" als Hintergrund)");
            sb.Append(" | Ergebnis: ").Append(verdict);
            Util.Log(sb.ToString());
        }
    }

    // ---------- patches ----------
    [HarmonyPatch(typeof(MapRenderer), "LoadMapSkins")]
    internal static class Map_SkinsPatch
    {
        private static void Postfix(MapRenderer __instance)
        {
            MapSkins.Map = __instance;
            MapHost.Ensure();
            Util.Safe("Map.Skins", () =>
            {
                var skins = MapSkins.Skins(__instance);
                if (skins == null) return;
                MapSkins.EnsureExtraSkins(skins);
                MapSkins.ApplyPlaceColors(skins);
                MapSkins.ApplyLabelColors(skins);
            });
        }
    }
    [HarmonyPatch(typeof(MapRenderer), "CurrentMapSkin", MethodType.Setter)]
    internal static class Map_CurrentSkinPatch
    {
        private static void Postfix(MapRenderer __instance)
        {
            MapSkins.Map = __instance;
            Util.Safe("Map.Current", () => { MapSkins.ApplyOwnMaterials(); MapSkins.GridLineColor(); MapSkins.OnSkinChanged(); });
        }
    }
    [HarmonyPatch(typeof(MapRenderer), "LoadMapLabels")]
    internal static class Map_LabelsPatch
    {
        private static void Postfix(MapRenderer __instance)
        {
            MapSkins.Map = __instance;
            MapSkins.RecordPlaceClasses(__instance);
            MapSkins.OnSkinChanged();
        }
    }
    [HarmonyPatch(typeof(NavalGridRenderer), "BuildSquares")]
    internal static class Map_BuildSquaresPatch
    {
        private static void Postfix(NavalGridRenderer __instance) { MapSkins.Grid = __instance; Util.Safe("Map.GridLine", MapSkins.GridLineColor); }
    }
    [HarmonyPatch(typeof(NavalGridRenderer), "CreateSquareRenderer")]
    internal static class Map_SquarePatch
    {
        private static void Postfix(NavalGridRenderer __instance, NavalGridRendererSquare __result)
        {
            MapSkins.Grid = __instance;
            if ((!MapSkins.On && !MapSkins.StyledOnce) || __result?.Label == null) return;
            Util.Safe("Map.Square", () => MapSkins.Style(__result.Label, MapSkins.Current, __result.Square == null ? -1 : __result.Square.Depth));
        }
    }
    [HarmonyPatch(typeof(VideoSettingsPanel), "FetchMapSkins")]
    internal static class Map_SettingsListPatch
    {
        private static void Postfix(VideoSettingsPanel __instance, ref string[] ___mapSkins, UnityEngine.UI.TextMeshProDropdown ___mapSkinDropdown)
        {
            var extra = MapSkins.ExtraNames();
            if (extra.Count == 0 || ___mapSkins == null || ___mapSkinDropdown == null) return;
            var opts = ___mapSkinDropdown.options;
            if (opts == null) return;
            var grown = new List<string>(___mapSkins);
            foreach (var n in extra)
            {
                if (grown.Contains(n)) continue;
                opts.Add(new UnityEngine.UI.TextMeshProDropdown.OptionData(n));
                grown.Add(n);
            }
            ___mapSkinDropdown.options = opts;
            ___mapSkins = grown.ToArray();
        }
    }

    // ---------- F10 tab "Karte" ----------
    internal static class MapTab
    {
        private static readonly string[] keys = { "Sea", "Land", "Grid", "Place", "Neutral", "Allies", "Axis" };
        private static readonly string[] names = { "Meer", "Land", "Raster-Beschriftung (AN15 ...)", "Meeres- und Ortsnamen", "Neutrale Länder", "Alliierte Länder", "Achsen-Länder" };

        public static void Draw()
        {
            Imgui.Label("Karten-Stil wählen: Einstellungen > Grafik > Kartenstil. Neu: „" + MapCfg.IvoryName + "“ und „" + MapCfg.CustomName + "“.");
            Imgui.Label("Farben für „" + MapCfg.CustomName + "“ (sofort sichtbar, wenn dieser Stil an ist):");
            for (int i = 0; i < keys.Length; i++)
            {
                Color32 c = MapSkins.Custom(keys[i]);
                Imgui.Label(names[i] + ":  " + MapCfg.Hex(c));
                Imgui.BeginH();
                byte r = Chan("R", c.r), g = Chan("G", c.g), b = Chan("B", c.b);
                Imgui.EndH();
                if (r != c.r || g != c.g || b != c.b)
                {
                    MapCfg.Write("Custom." + keys[i], MapCfg.Hex(new Color32(r, g, b, 255)));
                    Util.Safe("Map.Edit", () =>
                    {
                        var skins = MapSkins.Skins(MapSkins.Map);
                        if (skins == null) return;
                        foreach (var s in skins) if (s != null && MapSkins.IsCustom(s.name)) MapSkins.ApplyCustom(s);
                        MapSkins.ApplyLabelColors(skins);
                        if (MapSkins.IsCustom(MapSkins.Current)) { MapSkins.RetintOwn(); MapHost.RepaintPending = true; }
                    });
                }
            }
            Imgui.Space(8);
            Imgui.Label("Feine Einstellungen pro Karten-Stil: Datei " + MapCfg.File + " (wird im laufenden Spiel neu gelesen).");
            if (Imgui.Button("Karten-Bericht ins Log schreiben", 320)) Util.Safe("Map.Report", MapSkins.Report);
        }
        private static byte Chan(string n, byte v)
        {
            Imgui.Label(n + " " + v, 60);
            return (byte)Mathf.RoundToInt(Imgui.Slider(v, 0, 255, 140));
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/OrbitWater.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Globalization;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::PlayWay.Water;
    using global::UnityEngine;
    using global::UBOAT.Game.Core.Time;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Entities;
    using global::UBOAT.Game.Scene.Environment;
    using global::UBOAT.Game.Scene.Items;

    // Submerged, the far orbit camera stays below the real (wavy) sea surface, unless a periscope
    // is above water. The ceiling follows the waves smoothly and is released again when surfaced.
    internal sealed class OrbitSea
    {
        private Water sea;
        private WaterSample sample;
        private bool primed;
        public bool Ready => sample != null && sea != null;

        public void Bind(Weather w)
        {
            var s = w != null ? w.Sea : null;
            if (s == null) { Release(); return; }
            if (sample != null && sea == s) return;
            Release();
            sea = s;
            sample = new WaterSample(s, WaterSample.DisplacementMode.Height, 1f);
            primed = false;
        }

        public void Release()
        {
            if (sample != null) { try { sample.Stop(); } catch { } sample = null; }
            sea = null; primed = false;
        }

        public float HeightAt(Vector3 p, out bool found)
        {
            found = false;
            if (!Ready) return 0f;
            if (!primed) { sample.Start(p); primed = true; }
            found = true;
            return sample.GetAndResetFast(p, sea.Time);
        }
    }

    internal sealed class OrbitPeriscopes
    {
        private static readonly FieldInfo LastAbove = typeof(Periscope).GetField("lastTicksAboveWater", BindingFlags.Instance | BindingFlags.NonPublic);
        private Periscope[] scopes;
        private PlayerShip scanned;
        private float nextScan;
        public static bool Usable => LastAbove != null;
        public int Count => scopes == null ? 0 : scopes.Length;

        public bool AnyAboveWater(PlayerShip ship, GameTime time, float now, float grace)
        {
            if (ship == null || time == null || LastAbove == null) return false;
            if (scopes == null || scanned != ship || now >= nextScan) { nextScan = now + 4.3f; scanned = ship; scopes = ship.GetComponentsInChildren<Periscope>(true); }
            if (scopes.Length == 0) return false;
            long graceTicks = (long)(grace * 1e7f), t = time.UnscaledTicks;
            foreach (var p in scopes)
            {
                if (p == null) continue;
                long stamp;
                try { stamp = (long)LastAbove.GetValue(p); } catch { continue; }
                if (t - stamp <= graceTicks) return true;
            }
            return false;
        }

        public void Invalidate() { scopes = null; scanned = null; nextScan = 0f; }
    }

    [HarmonyPatch(typeof(SectionViewCameraController), "UpdateTransformNormalMode")]
    internal static class OrbitWater_Patch
    {
        private const float VanillaMax = 25f, MaxDt = 0.09f, MinLever = 3f, Eps = 0.05f;
        private const int OrbitPreset = 3;
        private static readonly OrbitSea Sea = new OrbitSea();
        private static readonly OrbitPeriscopes Scopes = new OrbitPeriscopes();
        private static float ceiling = float.NaN, nextLog;
        private static bool converged, clamping;
        private static int lastFrame = -1;

        private static bool On => Settings.B("CamOrbitUnderwater");
        private static bool Log => Settings.B("OrbitLog");

        private static void Prefix(SectionViewCameraController __instance, float ___verticalSpeed)
        {
            if (!On || float.IsNaN(ceiling) || __instance.DistancePresetIndex != OrbitPreset) return;
            float c = ceiling;
            if (Settings.B("OrbitAnticipate"))
            {
                float dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
                float d = ___verticalSpeed * -10f * dt;   // what this frame's mouse input will add
                if (d > 0f) c -= d;
            }
            var r = __instance.FarCameraRotation;
            if (r.y > c) { r.y = c; __instance.FarCameraRotation = r; }
        }

        private static void Postfix(SectionViewCameraController __instance, MainCamera ___mainCamera, PlayerShip ___playerShip, Weather ___weather, GameTime ___gameTime)
        {
            if (Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            float now = Time.unscaledTime;
            if (!On || !OrbitPeriscopes.Usable || ___mainCamera == null || ___playerShip == null || ___weather == null || ___gameTime == null) { Standdown(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
            if (dt <= 0f) return;
            if (__instance.DistancePresetIndex != OrbitPreset) { Release(dt); return; }
            Sea.Bind(___weather);
            if (!Sea.Ready) { Release(dt); return; }
            bool want = ___playerShip.Submerged && !Scopes.AnyAboveWater(___playerShip, ___gameTime, now, Settings.F("OrbitGrace"));
            if (!want) { Release(dt); return; }
            var cam = ___mainCamera.transform.position;
            float seaY = Sea.HeightAt(cam, out bool found);
            if (!found) { Standdown(); return; }
            float excess = cam.y - (seaY - Settings.F("OrbitMargin"));
            var pivot = __instance.Target;
            if (pivot == null) { Standdown(); return; }
            float dx = cam.x - pivot.position.x, dz = cam.z - pivot.position.z;
            float lever = Mathf.Max(MinLever, Mathf.Sqrt(dx * dx + dz * dz));
            float target = Mathf.Min(VanillaMax, __instance.FarCameraRotation.y - excess * (Mathf.Rad2Deg / lever) * Settings.F("OrbitGain"));
            if (float.IsNaN(ceiling))
            {
                ceiling = Mathf.Min(VanillaMax, __instance.FarCameraRotation.y);
                converged = false;
                if (Log) Util.Log("Orbit: greift ein, Decke=" + F(ceiling) + " Ziel=" + F(target) + " camY=" + F(cam.y) + " Meer=" + F(seaY) + " Hebel=" + F(lever) + " Sehrohre=" + Scopes.Count);
            }
            else
            {
                float step = Settings.F("OrbitTrack") * dt;
                if (target < ceiling)
                {
                    if (converged) ceiling = target;
                    else { ceiling = Mathf.Max(target, ceiling - step); if (ceiling <= target + Eps) converged = true; }
                }
                else ceiling = Mathf.Min(target, ceiling + step);
            }
            clamping = true;
            if (Log && now >= nextLog)
            {
                nextLog = now + 2f;
                Util.Log("Orbit: Decke=" + F(ceiling) + " Ziel=" + F(target) + " Winkel=" + F(__instance.FarCameraRotation.y) + " über Wasser=" + F(cam.y - seaY) + " Abstand=" + F(__instance.CameraDistance) + " eingerastet=" + converged);
            }
        }

        private static void Release(float dt)
        {
            if (float.IsNaN(ceiling)) return;
            ceiling += Settings.F("OrbitRelax") * dt;
            if (ceiling >= VanillaMax) { if (Log) Util.Log("Orbit: freigegeben"); ceiling = float.NaN; converged = false; }
            clamping = false;
        }

        private static void Standdown()
        {
            if (clamping && Log) Util.Log("Orbit: abgeschaltet");
            ceiling = float.NaN; converged = false; clamping = false;
        }

        private static string F(float f) => f.ToString("0.00", CultureInfo.InvariantCulture);
    }
}

// ====================================================================================================
// Abschnitt: Modules/QuietUI.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.IO;
    using global::System.Linq;
    using global::System.Reflection;
    using global::System.Text;
    using global::DWS.Common.InjectionFramework;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Audio;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Characters.Activators;
    using global::UBOAT.Game.Scene.Selectables;
    using global::UBOAT.Game.UI;
    using global::UBOAT.Game.UI.Banners;
    using global::UBOAT.Game.UI.Notifications;

    // Quiet interface: crew names / action icons / rest bars only on hover or selection,
    // a level per notification (Keep / Quiet / Off) in the file toolkit-notifications.txt,
    // the top banner and chime silenced, chosen tooltips blocked, port officer voices muted.
    // Presentation only: nothing of this reaches a savegame.
    internal enum QuietLevel { Keep = 0, Quiet = 1, Off = 2 }

    internal static class QuietCfg
    {
        public static bool On => Settings.B("QuietUI");
        public static string File => Path.Combine(Settings.Folder, "toolkit-notifications.txt");
        public static readonly Dictionary<string, QuietLevel> Levels = new Dictionary<string, QuietLevel>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> Exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> BlockedTooltips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static int Version;
        private static DateTime stamp;
        private static float nextPoll;

        public static readonly string[] DefaultQuiet = {
            "Undetected", "Hostility", "Alarm", "SilentRun", "NearbyContacts", "Buoyancy", "AirTanks", "AirTanksLow",
            "TravelMode", "ElectricEnginesPreferred", "DecreasedPerformance", "Dirt", "Hunger", "Stress",
            "LowSpareParts", "NoSpareParts", "DepletingResource", "MissingCook", "MissingWorkers", "InspectionReminder",
            "Overwork", "Personality", "Captives", "CrewSpecializationPointGained", "Vacation",
            "AwaitingMessages", "HighPriorityAwaitingMessages", "LowPriorityAwaitingMessages", "Intel", "CheckpointUnlocked",
            "HeadquartersTask", "FreeHeadquarterTaskSlot" };
        public static readonly string[] DefaultOff = { "TimeZoneChanged", "BackgroundLoading" };
        public static readonly string[] DefaultExceptions = { "ShoalAhead", "ShallowArea", "PortDefences", "AntiSubmarineNetCollision", "TriggeredMine", "HullCrush", "AircraftDetection" };
        private static readonly string[][] Groups = {
            new[] { "# Navigation und Gefahren (die sagt dir sonst nichts - am besten Keep lassen)", "ShoalAhead", "ShallowArea", "PortDefences", "AntiSubmarineNetCollision", "TriggeredMine", "HullCrush", "LowNavigationAccuracy", "Lost" },
            new[] { "# Entdeckung und Kampf", "AircraftDetection", "ShipDetection", "SmokeDetection", "PropellerDetection", "DirectDetection", "IndirectDetection", "TransmissionDetectedByEnemy", "Undetected", "Hostility", "Alarm", "SilentRun", "NearbyContacts" },
            new[] { "# Schäden und Geräte (alles, worauf du reagieren musst, bleibt Keep)", "Leak", "BrokenDevice", "Flaw", "FlawActivated", "FlawStatus", "DeviceStatus", "ToxicAir" },
            new[] { "# Boots-Zustand (das Symbol ist die Info, das Band die Störung - Quiet nimmt nur das Band)", "Buoyancy", "AirTanks", "AirTanksLow", "TravelMode", "ElectricEnginesPreferred", "DecreasedPerformance", "Dirt", "Hunger", "Stress", "LowSpareParts", "NoSpareParts", "DepletingResource", "MissingCook", "MissingWorkers", "InspectionReminder" },
            new[] { "# Crew", "Death", "Bleeding", "HeartAttack", "Disease", "SailorOffBoard", "CrewMembersHospitalized", "CrewMembersArrested", "SailorStatus", "Overwork", "Personality", "Captives", "CrewSpecializationPointGained", "Vacation" },
            new[] { "# Kampagne, Funk und Sonstiges", "RadioTransmission", "RadioTransmissionInterrupted", "MissionStatusUpdate", "Resupply", "ResupplyShipSank", "UpgradesInstallation", "Minelaying", "AwaitingMessages", "HighPriorityAwaitingMessages", "LowPriorityAwaitingMessages", "Intel", "CheckpointUnlocked", "HeadquartersTask", "FreeHeadquarterTaskSlot", "TimeZoneChanged", "BackgroundLoading" } };

        private static void Defaults()
        {
            Levels.Clear(); Exceptions.Clear(); BlockedTooltips.Clear();
            foreach (var n in DefaultQuiet) Levels[n] = QuietLevel.Quiet;
            foreach (var n in DefaultOff) Levels[n] = QuietLevel.Off;
            foreach (var n in DefaultExceptions) Exceptions.Add(n);
        }

        public static QuietLevel LevelFor(string className)
        {
            string key = className.EndsWith("Notification", StringComparison.Ordinal) && className.Length > 12 ? className.Substring(0, className.Length - 12) : className;
            if (!Levels.TryGetValue(key, out var l)) l = QuietLevel.Keep;
            if (l == QuietLevel.Keep && Settings.B("QuietBanner") && !Exceptions.Contains(key)) l = QuietLevel.Quiet;
            return l;
        }

        // Re-reads the file when it changed (about once per second).
        public static void Poll()
        {
            if (Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + 1f;
            try
            {
                if (!System.IO.File.Exists(File)) { Directory.CreateDirectory(Settings.Folder); System.IO.File.WriteAllText(File, Template(), new UTF8Encoding(false)); }
                var t = System.IO.File.GetLastWriteTimeUtc(File);
                if (t == stamp && Version > 0) return;
                stamp = t;
                Load();
            }
            catch (Exception e) { if (Version == 0) { Defaults(); Version++; } Util.Log("Ruhig: Datei nicht lesbar: " + e.Message); }
        }

        private static void Load()
        {
            Defaults();
            bool exceptionsSeen = false;
            foreach (var raw in System.IO.File.ReadAllLines(File))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                string key = line.Substring(0, c).Trim(), val = line.Substring(c + 1).Trim();
                if (key.StartsWith("Notification.", StringComparison.OrdinalIgnoreCase))
                {
                    string name = key.Substring(13).Trim();
                    if (name.Length > 0) Levels[name] = val.Equals("Quiet", StringComparison.OrdinalIgnoreCase) ? QuietLevel.Quiet : val.Equals("Off", StringComparison.OrdinalIgnoreCase) ? QuietLevel.Off : QuietLevel.Keep;
                }
                else if (key.Equals("PopupExceptions", StringComparison.OrdinalIgnoreCase))
                {
                    if (!exceptionsSeen) { Exceptions.Clear(); exceptionsSeen = true; }
                    foreach (var p in val.Split(',')) if (p.Trim().Length > 0) Exceptions.Add(p.Trim());
                }
                else if (key.Equals("BlockedTooltipKeys", StringComparison.OrdinalIgnoreCase))
                    foreach (var p in val.Split(',')) if (p.Trim().Length > 0) BlockedTooltips.Add(p.Trim());
            }
            Version++;
            int q = Levels.Count(kv => kv.Value == QuietLevel.Quiet), o = Levels.Count(kv => kv.Value == QuietLevel.Off);
            Util.Log("Ruhig: Meldungen " + q + " leise / " + o + " aus, " + Exceptions.Count + " Ausnahmen, " + BlockedTooltips.Count + " Tooltips gesperrt");
        }

        private static string Template()
        {
            var b = new StringBuilder();
            void L(string s) { b.Append(s).Append("\r\n"); }
            L("# UBOAT Toolkit - Meldungen und Tooltips (wird im laufenden Spiel nach ca. 1 Sekunde neu gelesen)");
            L("# Nur Anzeige: nichts davon kommt in einen Spielstand.");
            L("#");
            L("# Stufen pro Meldung:  Keep = wie im Spiel,  Quiet = Symbol bleibt, kein Band oben und kein Ton,  Off = gar nichts");
            L("# Schalter „Band oben stumm“ (F10) macht alle Keep-Meldungen leise, außer denen hier:");
            L("PopupExceptions: " + string.Join(", ", DefaultExceptions));
            L("");
            L("# Tooltips sperren: im F10-Menü „Tooltip-Schlüssel mitschreiben“ einschalten, spielen,");
            L("# dann toolkit-tooltip-keys.txt lesen und Schlüssel hier eintragen (mit Komma getrennt).");
            L("BlockedTooltipKeys:");
            L("");
            foreach (var g in Groups)
            {
                foreach (var e in g)
                {
                    if (e[0] == '#') { L(e); continue; }
                    string lv = DefaultOff.Contains(e) ? "Off" : DefaultQuiet.Contains(e) ? "Quiet" : "Keep";
                    L("Notification." + e + ": " + lv);
                }
                L("");
            }
            return b.ToString();
        }
    }

    // Runs the file poll and the tooltip key log flush. Registered in ToolkitRunner's frame loop.
    public class QuietUIHost : ModuleBase
    {
        public override void Frame() { if (QuietCfg.On) { QuietCfg.Poll(); QuietTooltips.Flush(); } }
    }

    [HarmonyPatch(typeof(Notification), "SetActive")]
    internal static class Quiet_NotificationPatch
    {
        private static readonly FieldInfo AudioTicks = AccessTools.Field(typeof(Notification), "nextAudioFxTicks");
        private static readonly Dictionary<Type, QuietLevel> cache = new Dictionary<Type, QuietLevel>();
        private static int cacheVersion = -1;
        private static int cacheBanner = -1;
        private static void Prefix(Notification __instance, ref bool active, ref string ribbonText)
        {
            if (!QuietCfg.On) return;
            try
            {
                QuietCfg.Poll();
                int banner = Settings.B("QuietBanner") ? 1 : 0;
                if (cacheVersion != QuietCfg.Version || cacheBanner != banner) { cache.Clear(); cacheVersion = QuietCfg.Version; cacheBanner = banner; }
                var t = __instance.GetType();
                if (!cache.TryGetValue(t, out var level)) cache[t] = level = QuietCfg.LevelFor(t.Name);
                if (level == QuietLevel.Keep) return;
                bool had = ribbonText != null;
                ribbonText = null;
                AudioTicks?.SetValue(__instance, long.MaxValue);
                if (level == QuietLevel.Off) active = false;
                if (Settings.B("QuietLog") && (had || level == QuietLevel.Off)) Util.Log("Ruhig: Meldung " + t.Name + " -> " + level);
            }
            catch (Exception e) { Util.Log("Ruhig: Meldungs-Filter: " + e.Message); }
        }
    }

    // The second banner route (save confirmation, experience, stress peak, map marker, save error).
    [HarmonyPatch(typeof(NotificationBarUI), "OpenNow")]
    internal static class Quiet_BarOpenPatch
    {
        private static bool Prefix() => !(QuietCfg.On && Settings.B("QuietDirectPopups"));
    }
    [HarmonyPatch(typeof(RecentNotificationsListUI), "OpenNow")]
    internal static class Quiet_RecentOpenPatch
    {
        private static bool Prefix() => !(QuietCfg.On && Settings.B("QuietDirectPopups"));
    }

    internal static class QuietTooltips
    {
        private static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<string> pending = new List<string>();
        private static bool warned;

        public static bool Block(string key, string source)
        {
            if (!QuietCfg.On) return false;
            bool log = Settings.B("QuietTooltipLog");
            if (string.IsNullOrEmpty(key)) { if (log) Record("(dynamisch, ohne Schlüssel)", source); return false; }
            if (log) Record(key, source);
            QuietCfg.Poll();
            return QuietCfg.BlockedTooltips.Contains(key);
        }
        private static void Record(string key, string source)
        {
            string line = key + "    [" + source + "]";
            lock (seen) { if (seen.Add(line)) pending.Add(line); }
        }
        public static void Flush()
        {
            string[] batch;
            lock (seen) { if (pending.Count == 0) return; batch = pending.ToArray(); pending.Clear(); }
            try { Directory.CreateDirectory(Settings.Folder); System.IO.File.AppendAllText(Path.Combine(Settings.Folder, "toolkit-tooltip-keys.txt"), string.Join("\r\n", batch) + "\r\n", new UTF8Encoding(false)); }
            catch (Exception e) { if (!warned) { warned = true; Util.Log("Ruhig: Tooltip-Liste nicht schreibbar: " + e.Message); } }
        }
    }
    [HarmonyPatch(typeof(Tooltip), "Open")]
    internal static class Quiet_TooltipPatch
    {
        private static bool Prefix(Tooltip __instance) { try { return !QuietTooltips.Block(__instance.LocalizationKey, "Tooltip"); } catch { return true; } }
    }
    [HarmonyPatch(typeof(InteriorCanvasTooltip), "Open")]
    internal static class Quiet_InteriorTooltipPatch
    {
        private static bool Prefix(InteriorCanvasTooltip __instance) { try { return !QuietTooltips.Block(__instance.LocalizationKey, "InteriorCanvasTooltip"); } catch { return true; } }
    }

    // Voices: light-switch callouts (Voice 158-160) and everyone who is not your own crew. Subtitles stay.
    [HarmonyPatch]
    internal static class Quiet_VoicePatch
    {
        private static readonly string[] LightCallouts = { "Voice 158", "Voice 159", "Voice 160" };
        private static MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(InteractiveCharacter)).FirstOrDefault(m => m.Name == "SayNow" && m.GetParameters().Length == 6 && m.GetParameters()[0].ParameterType == typeof(string));
        private static void Prefix(InteractiveCharacter __instance, string statementId)
        {
            try
            {
                if (__instance == null) return;
                bool light = Settings.B("MuteLightCallouts"), port = QuietCfg.On && Settings.B("QuietMutePort");
                if (!light && !port) return;
                string why = null;
                if (light && statementId != null && LightCallouts.Any(x => string.Equals(x, statementId, StringComparison.OrdinalIgnoreCase))) why = "Licht-Ansage";
                else if (port)
                {
                    var pc = __instance as PlayableCharacter;
                    if (pc == null) why = "keine Crew";
                    else if (pc.Data != null)
                        switch (pc.Data.CharacterType)
                        {
                            case PlayableCharacterData.Type.Sailor: case PlayableCharacterData.Type.Officer:
                            case PlayableCharacterData.Type.Skipper: case PlayableCharacterData.Type.Guest: break;
                            default: why = "Typ " + pc.Data.CharacterType; break;
                        }
                }
                if (why == null) return;
                DeepAudioSource own = __instance.SpeakAudioSource;
                if (own != null) own.volume = 0f;
                if (Settings.B("QuietLog")) Util.Log("Ruhig: stumm '" + statementId + "' von " + __instance.name + " (" + why + ")");
            }
            catch (Exception e) { Util.Log("Ruhig: Stimme: " + e.Message); }
        }
    }

    // Names, action icons, rest bars, event icons, timers above the crew: only on hover / selection.
    internal static class QuietNames
    {
        private static readonly FieldInfo LabelF = AccessTools.Field(typeof(BaseActivatorBanner), "label");
        private static readonly FieldInfo PlateF = AccessTools.Field(typeof(BaseActivatorBanner), "labelBackground");
        private static readonly FieldInfo HoveredF = AccessTools.Field(typeof(BaseActivatorBanner), "hovered");
        private static readonly FieldInfo ManagerF = AccessTools.Field(typeof(BaseActivatorBanner), "manager");
        private static readonly FieldInfo ActivatorF = AccessTools.Field(typeof(BaseActivatorBanner), "activator");
        private static readonly FieldInfo SelectionsF = AccessTools.Field(typeof(BaseActivatorBanner), "selections");
        private static readonly FieldInfo ProgressF = AccessTools.Field(typeof(CharacterActivatorBanner), "actionProgressBarParent");
        private static readonly FieldInfo EnergyF = AccessTools.Field(typeof(CharacterActivatorBanner), "energyBarParent");
        private static readonly FieldInfo ActionIconF = AccessTools.Field(typeof(CharacterActivatorBanner), "actionIcon");
        private static readonly FieldInfo EventIconF = AccessTools.Field(typeof(CharacterActivatorBanner), "eventIcon");
        private static readonly FieldInfo TimeLeftF = AccessTools.Field(typeof(CharacterActivatorBanner), "timeLeftLabel");
        private static SelectionManager selections;
        private static bool selectionsTried, reported;

        public static bool Ready => LabelF != null && PlateF != null && HoveredF != null && ManagerF != null && ActivatorF != null;
        public static bool ExtrasReady => HoveredF != null && ManagerF != null && ProgressF != null && EnergyF != null;

        private static void Report()
        {
            if (reported) return;
            reported = true;
            if (!Ready || !ExtrasReady) Util.Log("Ruhig: Namensschild-Felder fehlen (Spiel-Update?) - Namen/Symbole bleiben wie im Spiel");
        }

        private static bool WantVisible(BaseActivatorBanner banner, CharacterActivatorBanner character)
        {
            bool showAll = (ManagerF.GetValue(banner) as ActivatorBannersManager)?.ShowHiddenActivators ?? false;
            bool hovered = Settings.B("QuietShowHover") && (bool)HoveredF.GetValue(banner);
            bool selected = Settings.B("QuietShowSelected") && character != null && IsSelected(character);
            return banner.Display && (showAll || hovered || selected);
        }

        public static void Apply(BaseActivatorBanner banner)
        {
            if (banner == null || !QuietCfg.On) return;
            Report();
            if (!Ready) return;
            var ch = banner as CharacterActivatorBanner;
            if (ch != null) ApplyExtras(ch);
            if (ch != null ? !Settings.B("QuietNames") : !Settings.B("QuietDeviceNames")) return;
            var label = LabelF.GetValue(banner) as Behaviour;
            if (label == null) return;
            bool want = WantVisible(banner, ch);
            if (label.enabled != want) label.enabled = want;
            var plate = PlateF.GetValue(banner) as Behaviour;
            if (plate != null)
            {
                bool p = want && (ActivatorF.GetValue(banner) as CharacterActivator) != null;
                if (plate.enabled != p) plate.enabled = p;
            }
        }

        public static void ApplyExtras(CharacterActivatorBanner b)
        {
            if (b == null || !QuietCfg.On || !ExtrasReady) return;
            bool icons = Settings.B("QuietActionIcons"), energy = Settings.B("QuietEnergyBars"), events = Settings.B("QuietEventIcons"), timers = Settings.B("QuietTimers");
            if (!icons && !energy && !events && !timers) return;
            if (WantVisible(b, b)) return;
            if (icons) { Hide(ProgressF.GetValue(b) as GameObject); Hide((ActionIconF?.GetValue(b) as Component)?.gameObject); }
            if (energy) Hide((EnergyF.GetValue(b) as Component)?.gameObject);
            if (events) Hide((EventIconF?.GetValue(b) as Component)?.gameObject);
            if (timers) { var t = TimeLeftF?.GetValue(b) as Behaviour; if (t != null && t.enabled) t.enabled = false; }
        }

        private static void Hide(GameObject go) { if (go != null && go.activeSelf) go.SetActive(false); }

        private static bool IsSelected(CharacterActivatorBanner b)
        {
            var c = b.Character;
            if (c == null) return false;
            if (!selectionsTried)
            {
                selectionsTried = true;
                try { selections = SelectionsF?.GetValue(null) as SelectionManager ?? ScriptableObjectSingleton.LoadSingleton<SelectionManager>(); }
                catch (Exception e) { Util.Log("Ruhig: Auswahl nicht gefunden - ausgewählte Crew zeigt keinen Namen: " + e.Message); }
            }
            try { return selections != null && selections.HasSelection(c); } catch { return false; }
        }
    }

    [HarmonyPatch(typeof(BaseActivatorBanner), "ValidateVisibility")]
    internal static class Quiet_BannerVisibilityPatch
    {
        private static void Postfix(BaseActivatorBanner __instance) => QuietNames.Apply(__instance);
    }
    [HarmonyPatch(typeof(BaseActivatorBanner), "Selectable_HoverStop")]
    internal static class Quiet_BannerHoverStopPatch
    {
        private static void Postfix(BaseActivatorBanner __instance) => QuietNames.Apply(__instance);
    }
    [HarmonyPatch(typeof(CharacterActivatorBanner), "OnSetDisplayMode")]
    internal static class Quiet_BannerDisplayModePatch
    {
        private static void Postfix(CharacterActivatorBanner __instance) => QuietNames.ApplyExtras(__instance);
    }
}

// ====================================================================================================
// Abschnitt: Modules/StationControls.cs
// ====================================================================================================
namespace UboatToolkit.Modules.StationCtl
{
    using global::DWS.Common.Resources;
    using global::DWS.Common;
    using Debug = global::UnityEngine.Debug;
    using global::HarmonyLib;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Runtime.CompilerServices;
    using global::System;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Core.Input;
    using global::UBOAT.Game.Core.Serialization;
    using global::UBOAT.Game.Core;
    using global::UBOAT.Game.Scene.Camera.DeviceControllers;
    using global::UBOAT.Game.Scene.Camera.ExternalControllers;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Characters;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Items;
    using global::UBOAT.Game.Scene.Spaces;
    using global::UBOAT.Game.Scene;
    using global::UBOAT.Game.UI;
    using global::UnityEngine;

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
            if (!blocked && Keys.LoneTap(StationCfg.CursorToggleKey))
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

// ====================================================================================================
// Abschnitt: Modules/UiFixes.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Pooling;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Fix "Cannot set the parent of the GameObject 'Default Tooltip(Clone)' while activating or deactivating
    // the parent GameObject 'GUI'": the game re-parents a reused tooltip to the canvas every time it opens,
    // even when it already sits there. Unity refuses that while the GUI is being switched on/off.
    // Now the move is skipped when the tooltip is already under the canvas, and otherwise done one frame later.
    [HarmonyPatch(typeof(UBOAT.Game.UI.FloatingTooltip), "OpenInternal")]
    internal static class UiFixes_TooltipParent
    {
        private static readonly MethodInfo SetParent2 = AccessTools.Method(typeof(Transform), nameof(Transform.SetParent), new[] { typeof(Transform), typeof(bool) });
        private static readonly MethodInfo Ours = AccessTools.Method(typeof(UiFixes_TooltipParent), nameof(Set));

        public static void Set(Transform child, Transform parent, bool keep)
        {
            if (child == null) return;
            if (!Settings.B("FixTooltipParent")) { child.SetParent(parent, keep); return; }
            if (child.parent == parent) return;
            // Must happen right now: the game sizes and positions the tooltip on the next lines. Moving it a frame
            // later left an empty box (DoMe 2026-11-12, visibility panel). Only the redundant move is skipped.
            child.SetParent(parent, keep);
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
        {
            foreach (var c in code)
            {
                if ((c.opcode == OpCodes.Callvirt || c.opcode == OpCodes.Call) && c.operand as MethodInfo == SetParent2) { c.opcode = OpCodes.Call; c.operand = Ours; }
                yield return c;
            }
        }
    }

    // Fix 'Pooled instance of prefab "Character/Other/Newspaper Mirror" has enabled animators ...':
    // some props (newspaper stand) go back into the pool with their animator still running. The game notices
    // that only when it reuses them, logs the error and throws the object away. We do the same at the moment
    // it is handed back, without the error, so no broken object ever sits in the pool.
    [HarmonyPatch(typeof(GlobalPool), nameof(GlobalPool.FreeInstance))]
    internal static class UiFixes_PoolAnimators
    {
        private static readonly List<Animator> anims = new List<Animator>();
        private static readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();

        private static void Postfix(GlobalPool __instance, GameObject instance)
        {
            if (!Settings.B("FixPoolAnimators") || instance == null) return;
            var po = instance.GetComponent<PooledObject>();
            if (po == null) return;
            bool bad = false;
            instance.GetComponentsInChildren(true, anims);
            foreach (var a in anims) if (a != null && a.enabled) { bad = true; break; }
            if (!bad) { instance.GetComponentsInChildren(true, skins); foreach (var s in skins) if (s != null && s.enabled) { bad = true; break; } }
            anims.Clear(); skins.Clear();
            if (!bad) return;
            var pool = Traverse.Create(__instance).Method("GetPool", new[] { typeof(object) }).GetValue(po.PrefabPath);
            if (pool != null)
            {
                var tp = Traverse.Create(pool);
                if (tp.Property("Count").GetValue<int>() > 0 && (tp.Method("Peek").GetValue() as GameObject) == instance) tp.Method("Pop").GetValue();
            }
            Object.Destroy(instance);
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Pooling;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Fix NullReferenceException in FlamesRegistry.OnFlamesDetached / SpawnParticles: when a burning part is shot
    // off a ship (e.g. a gun "Detached"), the burning surface reports to a fire registry whose flame effect is
    // already gone. The game then throws every time. Now: nothing to move -> skip quietly.
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FlamesRegistry), nameof(UBOAT.Game.Scene.Effects.FlamesRegistry.OnFlamesDetached))]
    internal static class UiFixes_FlamesDetached
    {
        private static bool Prefix(UBOAT.Game.Scene.Effects.FlamesRegistry __instance, UBOAT.Game.Scene.Effects.FlammableSurface flames)
        {
            if (!Settings.B("FixFlames")) return true;
            return __instance != null && __instance.FlamesEffect != null && flames != null && flames.transform.parent != null;
        }

        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is System.NullReferenceException && Settings.B("FixFlames") ? null : __exception;
    }

    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Effects.FlamesRegistry), "SpawnParticles", new System.Type[0])]
    internal static class UiFixes_FlamesSpawn
    {
        private static bool Prefix(UBOAT.Game.Scene.Effects.FlamesRegistry __instance) =>
            !Settings.B("FixFlames") || (__instance != null && __instance.FlamesEffect != null);

        private static System.Exception Finalizer(System.Exception __exception) =>
            __exception is System.NullReferenceException && Settings.B("FixFlames") ? null : __exception;
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Pooling;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Diagnosis "empty box next to the eye" (DoMe 2026-11-12): every game tooltip that opens is checked 0.4 s later
    // and written to the log once per kind: who opened it, text, measured size, font, material and the alpha of box and
    // text. Shows whether the text is empty, invisible (alpha 0), unmeasured (font/material) or the box is something else.
    public class TooltipDiag : ModuleBase
    {
        private struct Pending { public UBOAT.Game.UI.FloatingTooltip T; public float At; public string From; }
        private static readonly List<Pending> pending = new List<Pending>();
        private static readonly HashSet<string> done = new HashSet<string>();
        internal static string Caller = "?";
        private static int count;
        private static readonly FieldInfo contentsF = AccessTools.Field(typeof(UBOAT.Game.UI.FloatingTooltip), "contents");
        private static readonly FieldInfo openF = AccessTools.Field(typeof(UBOAT.Game.UI.FloatingTooltip), "isOpen");

        internal static void Opened(object t)
        {
            var ft = t as UBOAT.Game.UI.FloatingTooltip;
            if (ft == null || !Settings.B("DiagTooltip") || count >= 40) return;
            pending.Add(new Pending { T = ft, At = Time.unscaledTime + 0.4f, From = Caller });
            Caller = "?";
        }

        public override void Frame()
        {
            if (pending.Count == 0) return;
            float now = Time.unscaledTime;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (now < p.At) continue;
                pending.RemoveAt(i);
                try { Check(p); } catch (System.Exception e) { Util.Log("Tooltip-Diagnose: " + e.Message); }
            }
        }

        private static void Check(Pending p)
        {
            if (p.T == null) return;
            var c = contentsF != null ? contentsF.GetValue(p.T) as TMPro.TextMeshProUGUI : null;
            var img = p.T.GetComponent<UnityEngine.UI.Image>();
            string text = c != null ? (c.text ?? "") : "<kein Text-Feld>";
            bool empty = c == null || text.Trim().Length == 0;
            string key = p.From + "|" + empty + "|" + (c != null && c.font != null ? c.font.name : "-");
            if (!done.Add(key) && p.From != "Auge" && !empty) return;
            count++;
            var sb = new System.Text.StringBuilder();
            sb.Append("Tooltip-Diagnose [").Append(p.From).Append("]: offen=").Append(openF != null ? openF.GetValue(p.T) : "?")
              .Append(", aktiv=").Append(p.T.gameObject.activeInHierarchy);
            var rt = p.T.transform as RectTransform;
            if (rt != null) sb.Append(", Kasten=").Append(rt.rect.width.ToString("0")).Append("x").Append(rt.rect.height.ToString("0"));
            if (img != null) sb.Append(", Kasten-Alpha=").Append(img.canvasRenderer.GetAlpha().ToString("0.00"));
            if (c != null)
            {
                string one = text.Replace("\n", " / ").Replace("\r", "");
                if (one.Length > 160) one = one.Substring(0, 160) + "...";
                sb.Append(", Text(").Append(text.Length).Append(" Zeichen)='").Append(one).Append("'")
                  .Append(", Text an=").Append(c.enabled).Append("/").Append(c.gameObject.activeInHierarchy)
                  .Append(", Text-Alpha=").Append(c.canvasRenderer.GetAlpha().ToString("0.00"))
                  .Append(", Farbe-A=").Append(c.color.a.ToString("0.00"))
                  .Append(", Größe=").Append(c.fontSize.ToString("0.0"))
                  .Append(", Schrift=").Append(c.font != null ? c.font.name : "KEINE")
                  .Append(", Material=").Append(c.fontSharedMaterial != null ? c.fontSharedMaterial.name : "KEINES")
                  .Append(", gemessen=").Append(c.preferredHeight.ToString("0"))
                  .Append(", Zeichen sichtbar=").Append(c.textInfo != null ? c.textInfo.characterCount : -1)
                  .Append(", Text-Rect=").Append(c.rectTransform.rect.width.ToString("0")).Append("x").Append(c.rectTransform.rect.height.ToString("0"));
            }
            Util.Log(sb.ToString());
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.FloatingTooltip), "OpenInternal")]
    internal static class TooltipDiag_Open
    {
        private static void Postfix(object __result) { try { TooltipDiag.Opened(__result); } catch { } }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.Resources.DetectabilityGUI), nameof(UBOAT.Game.UI.Resources.DetectabilityGUI.OnPointerEnter))]
    internal static class TooltipDiag_Eye
    {
        private static void Prefix() { TooltipDiag.Caller = "Auge"; }
        private static System.Exception Finalizer(System.Exception __exception)
        {
            if (__exception != null) Util.Log("Tooltip-Diagnose [Auge]: Spiel-Fehler beim Öffnen: " + __exception.GetType().Name + ": " + __exception.Message);
            return __exception;
        }
    }
}

namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::System.Reflection.Emit;
    using global::DWS.Common.Pooling;
    using global::HarmonyLib;
    using global::UnityEngine;

    // Empty tooltip box (DoMe 2026-11-13: eye icon, also on the stock IXC): a game "Tooltip" whose text is empty
    // (missing translation or update function returning "") still opened the box. Now: an empty text opens nothing,
    // and the object path + key are logged once so the missing text can be found.
    [HarmonyPatch(typeof(UBOAT.Game.UI.Tooltip), "Open")]
    internal static class UiFixes_EmptyTooltip
    {
        private static readonly FieldInfo localeF = AccessTools.Field(typeof(UBOAT.Game.UI.Tooltip), "locale");
        private static readonly HashSet<string> logged = new HashSet<string>();

        internal static string PathOf(Transform t)
        {
            string path = t != null ? t.name : "?";
            for (int i = 0; i < 4 && t != null && t.parent != null; i++) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }

        private static bool Prefix(UBOAT.Game.UI.Tooltip __instance, string ___localizationKey, System.Func<string> ___updateFunction)
        {
            try
            {
                string text = null, key = ___localizationKey;
                if (___updateFunction != null) { text = ___updateFunction(); if (text == null) return true; key = "(Funktion)"; }
                else if (key != null && localeF != null)
                {
                    var loc = localeF.GetValue(null);
                    string k = key.EndsWith("/") ? key + __instance.name : key;
                    text = loc != null ? Traverse.Create(loc).Method("get_Item", new[] { typeof(string) }, new object[] { k }).GetValue<string>() : null;
                    key = k;
                }
                else return true;
                string path = PathOf(__instance.transform);
                TooltipDiag.Caller = "Tooltip " + path + " key=" + key;
                if (text != null && text.Trim().Length > 0) return true;
                if (logged.Add(path + "|" + key)) Util.Log("Tooltip ohne Text unterdrückt: " + path + ", Schlüssel '" + key + "'");
                return !Settings.B("FixEmptyTooltip");
            }
            catch (System.Exception e) { Util.Log("Tooltip-Prüfung: " + e.Message); return true; }
        }
    }

    [HarmonyPatch(typeof(UBOAT.Game.UI.PlainTooltip), nameof(UBOAT.Game.UI.PlainTooltip.Open))]
    internal static class UiFixes_PlainTooltipDiag
    {
        private static readonly HashSet<string> logged = new HashSet<string>();
        private static void Postfix(UBOAT.Game.UI.PlainTooltip __instance, RectTransform target, string contents)
        {
            try
            {
                if (!Settings.B("DiagTooltip")) return;
                string path = UiFixes_EmptyTooltip.PathOf(target != null ? target.transform : __instance.transform);
                bool empty = contents == null || contents.Trim().Length == 0;
                if (logged.Add(path + "|" + empty))
                    Util.Log("Tooltip-Diagnose [Plain " + path + "]: Text(" + (contents == null ? -1 : contents.Length) + " Zeichen)='" + (contents ?? "").Replace("\n", " / ") + "'");
            }
            catch { }
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/VoiceLimit.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::HarmonyLib;
    using global::UnityEngine;

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
            var t = UboatToolkit.Util.FindType("UBOAT.Game.Scene.Tasks.SpeakController");
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

// ====================================================================================================
// Abschnitt: Modules/VoiceRepair.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System;
    using global::System.Collections.Generic;
    using global::System.Reflection;
    using global::DWS.Common.InjectionFramework;
    using global::HarmonyLib;

    // Voice language: at start, puts the chosen voice language back if the game reset it to the
    // interface language, clears the game's "apply default voice" flag, and repairs a "latest version"
    // entry that no longer exists (it would stamp new saves as unloadable). Writes settings.ubt only if needed.
    internal static class VoiceRepair
    {
        private static readonly string[] Langs = { null, "German", "English" };

        public static void Run()
        {
            if (!Settings.B("KeepVoiceLanguage")) return;
            Type st = UboatToolkit.Util.FindType("UBOAT.Game.Core.Data.UserSettings");
            if (st == null) { Util.Log("Sprache: UserSettings nicht gefunden"); return; }
            object settings;
            try { settings = ScriptableObjectSingleton.LoadSingleton(st); } catch (Exception e) { Util.Log("Sprache: Einstellungen nicht ladbar: " + e.Message); return; }
            if (settings == null) { Util.Log("Sprache: noch keine Einstellungen - nur die Sperre ist aktiv"); return; }
            bool changed = false;
            var flag = AccessTools.Field(st, "applyDefaultVoiceLanguage");
            if (flag != null && flag.FieldType == typeof(bool) && (bool)flag.GetValue(settings)) { flag.SetValue(settings, false); changed = true; Util.Log("Sprache: Spiel-Rücksetzung war vorgemerkt - gelöscht"); }
            object gameplay = AccessTools.Property(st, "GameplaySettings")?.GetValue(settings, null) ?? AccessTools.Field(st, "gameplaySettings")?.GetValue(settings);
            if (gameplay == null) { Util.Log("Sprache: Spiel-Einstellungen nicht erreichbar"); return; }
            var gt = gameplay.GetType();
            int idx = (int)Math.Round(Settings.F("VoiceLang"));
            string want = idx > 0 && idx < Langs.Length ? Langs[idx] : null;
            var voice = AccessTools.Field(gt, "voiceLanguage");
            if (voice != null && want != null)
            {
                string cur = voice.GetValue(gameplay) as string;
                if (!string.Equals(cur, want, StringComparison.OrdinalIgnoreCase)) { voice.SetValue(gameplay, want); changed = true; Util.Log("Sprache: Stimme war \"" + (cur ?? "leer") + "\" - zurück auf \"" + want + "\""); }
            }
            if (Settings.B("VoiceRepairVersion")) changed |= RepairVersion(st, gameplay, gt);
            if (!changed) return;
            var write = AccessTools.Method(st, "WriteToHDD");
            if (write != null) { write.Invoke(settings, null); Util.Log("Sprache: Einstellungen gespeichert"); }
        }

        private static bool RepairVersion(Type st, object gameplay, Type gt)
        {
            var vf = AccessTools.Field(gt, "latestVersion");
            if (vf == null) return false;
            string stored = vf.GetValue(gameplay) as string;
            var versions = Changelog(st);
            if (versions == null || versions.Count == 0) return false;
            if (!string.IsNullOrEmpty(stored) && versions.Contains(stored)) return false;
            string newest = versions[versions.Count - 1];
            vf.SetValue(gameplay, newest);
            Util.Log("Sprache: gespeicherte Version \"" + (stored ?? "leer") + "\" fehlt in der Versionsliste - auf \"" + newest + "\" gesetzt (sonst gelten neue Spielstände als nicht ladbar)");
            return true;
        }

        private static List<string> Changelog(Type st)
        {
            try
            {
                object gd = AccessTools.Field(st, "gameData")?.GetValue(null);
                if (gd == null) return null;
                var rows = AccessTools.Method(gd.GetType(), "GetDataRows", new[] { typeof(string) })?.Invoke(gd, new object[] { "General/Changelogs" }) as Array;
                if (rows == null || rows.Length == 0) return null;
                var result = new List<string>(rows.Length);
                MethodInfo gs = null;
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    if (gs == null) { gs = AccessTools.Method(row.GetType(), "GetString", new[] { typeof(int) }); if (gs == null) return null; }
                    if (gs.Invoke(row, new object[] { 0 }) is string v && v.Length > 0) result.Add(v);
                }
                return result;
            }
            catch (Exception e) { Util.Log("Sprache: Versionsliste nicht lesbar: " + e.Message); return null; }
        }
    }
}


// ====================================================================================================
// Abschnitt: Tauchknöpfe neben dem Fahrtregler (0.4.35)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UnityEngine.UI;

    // DoMe 2026-11-18: "lassen sich die tauchknöpfe duplizieren neben der geschwindigkeit?"
    // The six depth toggles of ShipControlUI (surface, deck awash, periscope, max safe depth, hydrophone check, dive
    // schedule) sit at the depth meter. This part draws six own buttons with the same pictures under the telegraph
    // container. A click only switches the game's own toggle, so the game's own code gives the order.
    // Scope: player HUD only.
    public class DiveButtonsCopy : ModuleBase
    {
        private static readonly string[] Fields = { "depthSurfaceToggle", "depthDeckAwashToggle", "depthPeriscopeToggle", "depthHullCrushToggle", "depthHydrophoneCheckToggle", "depthScheduleToggle" };
        private static readonly UBOAT.Game.Scene.Entities.DepthPreset[] Presets = { UBOAT.Game.Scene.Entities.DepthPreset.Surface, UBOAT.Game.Scene.Entities.DepthPreset.DeckAwash, UBOAT.Game.Scene.Entities.DepthPreset.Periscope, UBOAT.Game.Scene.Entities.DepthPreset.MaxSafeDepth, UBOAT.Game.Scene.Entities.DepthPreset.HydrophoneCheck, UBOAT.Game.Scene.Entities.DepthPreset.None };
        private GameObject root; private readonly Toggle[] orig = new Toggle[6]; private readonly RectTransform[] mine = new RectTransform[6];
        private readonly GameObject[] mark = new GameObject[6];
        private Transform depthC, teleC; private float next; private bool failed; private string lastPos = "";
        private RectTransform frame; private Transform bar;

        public override void Tick()
        {
            if (!Settings.B("DiveButtonsAtSpeed"))
            {
                if (root != null) { Object.Destroy(root); root = null; Util.Log("Tauchknöpfe: Kopie am Fahrtregler entfernt (Schalter aus)"); }
                failed = false; return;
            }
            if (root == null)
            {
                if (failed || Time.unscaledTime < next) return;
                next = Time.unscaledTime + 2f;
                try { Build(); } catch (System.Exception e) { failed = true; Util.Log("Tauchknöpfe: Kopie nicht gebaut, Fehler: " + e.Message); }
                return;
            }
            if (depthC == null || teleC == null || orig[0] == null) { Object.Destroy(root); root = null; return; }
            Vector3 add = new Vector3(Settings.F("DiveButtonsX"), Settings.F("DiveButtonsY"), 0f);
            for (int i = 0; i < 6; i++)
            {
                if (mine[i] == null || orig[i] == null) continue;
                mine[i].position = teleC.TransformPoint(depthC.InverseTransformPoint(orig[i].transform.position) + add);
                // the ring shows the boat's real order, not the game toggle (which is only kept current while the depth meter is open)
                var sh = Util.PlayerShip;
                bool lit = sh != null && (i == 5 ? sh.FollowDiveSchedule : (!sh.FollowDiveSchedule && sh.DepthPreset == Presets[i]));
                if (mark[i] != null && mark[i].activeSelf != lit) mark[i].SetActive(lit);
            }
            if (frame != null && bar != null) frame.position = teleC.TransformPoint(depthC.InverseTransformPoint(bar.position) + add);
            Vector3 o = depthC.InverseTransformPoint(orig[0].transform.position) + add;
            string p = Mathf.RoundToInt(o.x) + "/" + Mathf.RoundToInt(o.y);
            if (p != lastPos) { lastPos = p; Util.Log("Tauchknöpfe: oberster Knopf der Kopie steht " + p + " Bildpunkte (rechts/hoch) neben der Mitte des Fahrtreglers"); }
        }

        private static RectTransform Picture(Image src, Transform parent, Transform srcRoot, bool click)
        {
            var go = new GameObject("Bild", typeof(RectTransform));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            var srt = (RectTransform)src.transform;
            rt.sizeDelta = srt.rect.size; rt.pivot = srt.pivot;
            rt.localPosition = srcRoot.InverseTransformPoint(srt.position);
            var img = go.AddComponent<Image>();
            img.sprite = src.sprite; img.color = src.color; img.type = src.type; img.material = src.material;
            img.preserveAspect = src.preserveAspect; img.raycastTarget = click;
            img.pixelsPerUnitMultiplier = src.pixelsPerUnitMultiplier; img.fillCenter = src.fillCenter;
            rt.localScale = srt.localScale; rt.localRotation = srt.localRotation;
            return rt;
        }

        private void Build()
        {
            var ui = Object.FindObjectOfType<UBOAT.Game.UI.ShipControlUI>();
            if (ui == null) return;
            for (int i = 0; i < 6; i++)
            {
                orig[i] = AccessTools.Field(typeof(UBOAT.Game.UI.ShipControlUI), Fields[i])?.GetValue(ui) as Toggle;
                if (orig[i] == null) { failed = true; Util.Log("Tauchknöpfe: Knopf " + Fields[i] + " nicht gefunden, keine Kopie"); return; }
            }
            depthC = (AccessTools.Field(typeof(UBOAT.Game.UI.ShipControlUI), "depthMeterContainer")?.GetValue(ui) as GameObject)?.transform;
            teleC = (AccessTools.Field(typeof(UBOAT.Game.UI.ShipControlUI), "telegraphContainer")?.GetValue(ui) as GameObject)?.transform;
            if (depthC == null || teleC == null) { failed = true; Util.Log("Tauchknöpfe: Tiefenmesser oder Fahrtregler nicht gefunden, keine Kopie"); return; }
            root = new GameObject("UboatToolkit Tauchknöpfe Kopie", typeof(RectTransform));
            root.transform.SetParent(teleC, false);
            int pics = 0;
            // Frame of the bar: the pictures on the common parent of the six toggles, drawn behind the buttons.
            frame = null; bar = orig[0].transform.parent; int frames = 0;
            for (int i = 1; i < 6; i++) if (orig[i].transform.parent != bar) bar = null;
            if (bar != null)
            {
                var fgo = new GameObject("Rahmen", typeof(RectTransform));
                frame = (RectTransform)fgo.transform; frame.SetParent(root.transform, false);
                frame.sizeDelta = ((RectTransform)bar).rect.size; frame.pivot = ((RectTransform)bar).pivot;
                foreach (var src in bar.GetComponents<Image>()) { if (src.sprite == null && src.color.a < 0.01f) continue; Picture(src, frame, bar, false); frames++; }
                foreach (Transform c in bar)
                {
                    if (c.GetComponentInChildren<Toggle>(true) != null) continue;
                    foreach (var src in c.GetComponentsInChildren<Image>(true)) { if (!src.gameObject.activeInHierarchy || (src.sprite == null && src.color.a < 0.01f)) continue; Picture(src, frame, bar, false); frames++; }
                }
            }
            for (int i = 0; i < 6; i++)
            {
                int k = i;
                var go = new GameObject(Fields[i], typeof(RectTransform));
                mine[i] = (RectTransform)go.transform; mine[i].SetParent(root.transform, false);
                mine[i].sizeDelta = ((RectTransform)orig[i].transform).rect.size;
                mark[i] = null; bool first = true;
                foreach (var src in orig[i].GetComponentsInChildren<Image>(true))
                {
                    if (src.sprite == null && src.color.a < 0.01f) continue;
                    bool isMark = orig[i].graphic != null && src == orig[i].graphic;
                    if (first && isMark) continue;
                    var rt = Picture(src, mine[i], orig[i].transform, first);
                    if (first)
                    {
                        var b = rt.gameObject.AddComponent<Button>();
                        // same mouse-over and press look as the game's own toggle
                        b.transition = orig[i].transition; b.colors = orig[i].colors; b.spriteState = orig[i].spriteState;
                        b.targetGraphic = rt.GetComponent<Image>();
                        b.onClick.AddListener(() =>
                        {
                            if (orig[k] == null) return;
                            // 0.4.47 (DoMe: "seit dem ... sind diese wirkungslos"): 0.4.37 switched the game's toggle. While the
                            // depth meter is closed its toggle group does not switch the other toggles off, so two were on and
                            // the game took the old one (it asks them in a fixed order) -> no effect. Now the order goes
                            // straight to the boat, exactly what the game's own button handler does (ShipControlUI.OnDepthToggle),
                            // and the game sets its own toggles from the boat afterwards.
                            var ship = Util.PlayerShip; if (ship == null) return;
                            bool wasOn = k == 5 ? ship.FollowDiveSchedule : (!ship.FollowDiveSchedule && ship.DepthPreset == Presets[k]);
                            if (k == 5) ship.FollowDiveSchedule = !wasOn;
                            else
                            {
                                ship.FollowDiveSchedule = false;
                                Owners.Preset(ship, "Tauchknopf am Fahrtregler (du)", Owners.Player, wasOn ? UBOAT.Game.Scene.Entities.DepthPreset.None : Presets[k], false);
                            }
                            Util.Log("Tauchknöpfe: Kopie gedrückt, " + Fields[k] + " -> Befehl ans Boot: " + (k == 5 ? "Tauchplan " + (ship.FollowDiveSchedule ? "an" : "aus") : ship.DepthPreset.ToString()) + ", Zieltiefe " + ship.TargetDepth.ToString("0.0") + " m (nur eigenes Boot)");
                        });
                        first = false;
                    }
                    if (orig[i].graphic != null && src == orig[i].graphic) { mark[i] = rt.gameObject; rt.gameObject.SetActive(orig[i].isOn); }
                    pics++;
                }
            }
            lastPos = "";
            int rings = 0; for (int i = 0; i < 6; i++) if (mark[i] != null) rings++;
            Util.Log("Tauchknöpfe: Rahmen übernommen: " + (frames > 0 ? "ja" : "nein") + ", Mauszeiger-Effekt übernommen: ja (Art " + orig[0].transition + "), Ring für aktiven Knopf übernommen: " + rings + " von 6");
            Util.Log("Tauchknöpfe: 6 eigene Knöpfe am Fahrtregler gebaut (" + pics + " Bilder der Spielknöpfe, " + frames + " Bilder vom Rahmen der Leiste übernommen). Wirkt nur auf die Anzeige des eigenen Boots.");
        }
    }
}

// ====================================================================================================
// Abschnitt: Karten-Markierung ohne Schiff (0.4.47)
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::HarmonyLib;
    using global::UnityEngine;

    // Game log 2026-11-18: "Error in execution queue method EntityMarkersManager.DoLateUpdate: NullReferenceException at
    // EntityMarkerUI.UpdateMarker", 10 to 12 times a minute, and each one opened the developer console for a moment
    // (DoMe: "ploppt dauerhaft kurz die debug console auf"). A ship marker on the map/screen pointed at a ship that no
    // longer exists. The marker is hidden instead; when its ship is back the game shows it again. Player display only.
    [HarmonyPatch(typeof(UBOAT.Game.UI.Map.EntityMarkerUI), "UpdateMarker")]
    internal static class EntityMarkerGuard_Patch
    {
        private static int caught; private static float nextLog;
        private static System.Exception Finalizer(System.Exception __exception, UBOAT.Game.UI.Map.EntityMarkerUI __instance)
        {
            if (__exception == null) return null;
            if (!(__exception is System.NullReferenceException) && !(__exception is MissingReferenceException)) return __exception;
            caught++;
            string name = "?";
            try { if (__instance != null) { name = __instance.name; __instance.gameObject.SetActive(false); } } catch { }
            if (Time.unscaledTime > nextLog)
            {
                nextLog = Time.unscaledTime + 60f;
                string where = __exception.StackTrace ?? ""; int n = where.IndexOf('\n'); if (n > 0) where = where.Substring(0, n);
                Util.Log("Karten-Markierung: " + caught + " mal zeigte eine Markierung auf ein Schiff, das es nicht mehr gibt (zuletzt " + name + "). Markierung ausgeblendet, kein Fehler mehr im Spiel-Log. Stelle: " + where.Trim());
            }
            return null;
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/GpuAudit.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::UnityEngine;

    // 0.4.50 (DoMe 2026-11-18: "vollinvestigation was man alles auf die GPU auslagern kann"). Read-only count of what the
    // processor prepares for drawing, every 5 minutes, so that each later step has a number before and after.
    public class GpuAudit : ModuleBase
    {
        private float next = 60f;
        public override void Tick()
        {
            if (Time.unscaledTime < next || Util.PlayerShip == null) return;
            next = Time.unscaledTime + 300f;
            Util.Safe("GpuAudit", () =>
            {
                double t0 = Time.realtimeSinceStartupAsDouble;
                var mats = new HashSet<Material>(); var meshes = new Dictionary<Mesh, int>();
                int rend = 0, visible = 0, noInst = 0, rendNoInst = 0, repeat = 0, repeatNoInst = 0;
                foreach (var r in Object.FindObjectsOfType<MeshRenderer>())
                {
                    if (r == null || !r.enabled) continue;
                    rend++; if (r.isVisible) visible++;
                    var m = r.sharedMaterial; bool inst = m != null && m.enableInstancing;
                    if (m != null && mats.Add(m) && !inst) noInst++;
                    if (!inst) rendNoInst++;
                    var f = r.GetComponent<MeshFilter>(); var mesh = f != null ? f.sharedMesh : null;
                    if (mesh == null) continue;
                    int c; meshes.TryGetValue(mesh, out c); meshes[mesh] = c + 1;
                    if (c >= 1) { repeat++; if (!inst) repeatNoInst++; }
                }
                int skin = 0, skinVisible = 0, bones = 0;
                foreach (var s in Object.FindObjectsOfType<SkinnedMeshRenderer>())
                {
                    if (s == null || !s.enabled) continue;
                    skin++; if (s.isVisible) { skinVisible++; bones += s.bones != null ? s.bones.Length : 0; }
                }
                int ps = 0, playing = 0, alive = 0, meshMode = 0, meshNoInst = 0;
                foreach (var p in Object.FindObjectsOfType<ParticleSystem>())
                {
                    if (p == null) continue;
                    ps++; if (p.isPlaying) playing++; alive += p.particleCount;
                    var pr = p.GetComponent<ParticleSystemRenderer>();
                    if (pr != null && pr.renderMode == ParticleSystemRenderMode.Mesh) { meshMode++; if (!pr.enableGPUInstancing) meshNoInst++; }
                }
                int canvases = 0, graphics = 0;
                foreach (var c in Object.FindObjectsOfType<Canvas>()) if (c != null && c.isActiveAndEnabled) canvases++;
                foreach (var g in Object.FindObjectsOfType<UnityEngine.UI.Graphic>()) if (g != null && g.isActiveAndEnabled) graphics++;
                int markers = Object.FindObjectsOfType<UBOAT.Game.UI.Map.EntityMarkerUI>().Length;
                int ships = Object.FindObjectsOfType<UBOAT.Game.Scene.Entities.Ship>().Length;
                // water module (com.playway.water): values live in its settings objects, read by name
                var wsb = new System.Text.StringBuilder();
                foreach (var tn in new[] { "PlayWay.Water.WaterProjectSettings", "PlayWay.Water.WaterQualitySettings" })
                {
                    var tp = HarmonyLib.AccessTools.TypeByName(tn); if (tp == null) { wsb.Append(' ').Append(tn).Append(" fehlt;"); continue; }
                    var ip = HarmonyLib.AccessTools.Property(tp, "Instance"); object inst = ip != null ? ip.GetValue(null, null) : null;
                    if (inst == null) { wsb.Append(' ').Append(tn).Append(" ohne Instanz;"); continue; }
                    foreach (var pn in new[] { "PhysicsThreads", "PhysicsThreadsPriority", "AllowCpuFFT", "AllowFloatingPointMipMaps", "SinglePrecision", "CurrentQualityLevel", "MaxSpectrumResolution", "AllowHighPrecisionTextures", "TileSizeScale", "WavesMode" })
                    {
                        var pp = HarmonyLib.AccessTools.Property(tp, pn);
                        if (pp != null && pp.GetIndexParameters().Length == 0) wsb.Append(' ').Append(pn).Append('=').Append(System.Convert.ToString(pp.GetValue(inst, null), System.Globalization.CultureInfo.InvariantCulture)).Append(';');
                    }
                }
                Util.Log("Wasser-Modul (nur lesen):" + wsb);
                double ms = (Time.realtimeSinceStartupAsDouble - t0) * 1000.0;
                Util.Log("Zeichnen (Zählung, nur lesen): Schiffe " + ships + "; Teile " + rend + " (im Bild " + visible + "), davon ohne Gruppen-Zeichnen " + rendNoInst
                    + "; gleiche Teile mehrfach " + repeat + ", davon ohne Gruppen-Zeichnen " + repeatNoInst + "; Materialien " + mats.Count + ", ohne Gruppen-Zeichnen " + noInst
                    + "; Figuren-Modelle " + skin + " (im Bild " + skinVisible + ", Knochen " + bones + "); Partikel-Systeme " + ps + " (laufen " + playing + ", Partikel " + alive
                    + ", aus 3D-Modellen " + meshMode + ", davon ohne Gruppen-Zeichnen " + meshNoInst + "); Oberfläche: Flächen " + canvases + ", Elemente " + graphics
                    + ", Kontakt-Markierungen " + markers + "; Grafikkarte: " + SystemInfo.graphicsDeviceType + ", Gruppen-Zeichnen möglich " + SystemInfo.supportsInstancing
                    + ", Rechen-Shader " + SystemInfo.supportsComputeShaders + ", Zeichnen auf mehreren Kernen " + SystemInfo.graphicsMultiThreaded + " (" + SystemInfo.renderingThreadingMode + ")"
                    + ", Prozessor-Kerne " + SystemInfo.processorCount + "; Zählung dauerte " + ms.ToString("0") + " ms");
            });
        }
    }
}

// ====================================================================================================
// Abschnitt: Modules/ViewSavings.cs
// ====================================================================================================
namespace UboatToolkit.Modules
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Scene.Camera;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Core.Data;
    using global::UBOAT.Game.Scene;
    using global::DWS.Common.InjectionFramework;

    // 0.4.52 (DoMe 2026-11-18: "sachen die man aktuell nicht sieht braucht man ja nicht berechnen ... kartenmodus").
    // Picture only: nothing here changes what a ship does, sees or hits. One switch = one effect, all off by default,
    // everything is put back when the switch goes off or the map closes. One log line per minute with the counts.
    public class ViewSavings : ModuleBase
    {
        private const float PureMapHeight = 800f;   // same height the game uses for "map only" (free camera lock)
        private const float FarSpray = 2000f;        // the game's own border between detail steps Low and VeryLow

        private float nextFast, nextScan, nextLog; private int scanStep;
        private bool mapPaused, mapShadow, mapLod;
        private readonly List<ParticleSystem> paused = new List<ParticleSystem>();
        private readonly Dictionary<ParticleSystem, ParticleSystemCullingMode> cull = new Dictionary<ParticleSystem, ParticleSystemCullingMode>();
        private readonly HashSet<ParticleSystemRenderer> inst = new HashSet<ParticleSystemRenderer>();
        private readonly HashSet<Behaviour> sprayOff = new HashSet<Behaviour>();
        private MapCameraController mapCam; private UserSettings user;
        private static readonly System.Reflection.FieldInfo ShadowField = AccessTools.Field(typeof(MapCameraController), "shadowDistance");
        private static readonly System.Type BowType = AccessTools.TypeByName("PlayWay.Water.ShipBowWavesEmitter");
        private int nMapPaused, nCull, nInst, nSpray; private float mapSeconds;

        private static bool PureMap()
        {
            var cam = MainCamera.Instance;
            return cam != null && cam.CurrentMode == CameraMode.Map && cam.transform.position.y > PureMapHeight;
        }

        public override void Tick()
        {
            float now = Time.unscaledTime;
            if (now < nextFast) return;
            float step = now - (nextFast - 0.5f); nextFast = now + 0.5f;
            if (Util.PlayerShip == null) return;
            bool map = PureMap();
            if (map) mapSeconds += Mathf.Clamp(step, 0f, 2f);
            Util.Safe("Save.MapEffects", () => MapEffects(map && Settings.B("SaveMapEffects"), now));
            Util.Safe("Save.MapShadows", () => MapShadows(map && Settings.B("SaveMapShadows")));
            Util.Safe("Save.MapDetail", () => MapDetail(map && Settings.B("SaveMapDetail")));
            // 0.4.55: the four searches through the whole scene never run in the same picture; one of them every 3 s,
            // so each comes round every 12 s (a search costs more the more ships are loaded).
            if (now >= nextScan)
            {
                nextScan = now + 3f; scanStep = (scanStep + 1) % 4;
                if (scanStep == 0) Util.Safe("Save.Unseen", () => Unseen(Settings.B("SaveUnseenEffects")));
                else if (scanStep == 1) Util.Safe("Save.MeshGpu", () => MeshGpu(Settings.B("SaveMeshParticlesGpu")));
                else if (scanStep == 2) Util.Safe("Save.FarSpray", () => Spray(Settings.B("SaveFarSpray")));
                else Util.Safe("Save.Markers", () => Markers(Settings.B("SaveMarkersOwnCanvas")));
            }
            if (now >= nextLog)
            {
                nextLog = now + 60f;
                if (Settings.B("SaveMarkersOwnCanvas") || Settings.B("SaveWedgesCalm"))
                {
                    Util.Log("Sparen: Kontakt-Markierungen mit eigener Zeichenfläche " + markerCanvas.Count + "; Kontakt-Keile neu gebaut " + UboatToolkit.Patches.WedgeCalm_Patch.Ran + " mal, übersprungen " + UboatToolkit.Patches.WedgeCalm_Patch.Skipped + " mal in der letzten Minute");
                    UboatToolkit.Patches.WedgeCalm_Patch.Ran = 0; UboatToolkit.Patches.WedgeCalm_Patch.Skipped = 0;
                }
                if (Settings.B("SaveMapEffects") || Settings.B("SaveMapShadows") || Settings.B("SaveMapDetail") || Settings.B("SaveUnseenEffects") || Settings.B("SaveMeshParticlesGpu") || Settings.B("SaveFarSpray"))
                    Util.Log("Sparen: reine Karte " + mapSeconds.ToString("0") + " s in der letzten Minute; angehaltene Effekte auf der Karte " + nMapPaused + (mapPaused ? " (jetzt)" : "")
                        + ", Schatten auf der Karte " + (mapShadow ? "aus" : "normal") + ", Detail auf der Karte " + (mapLod ? "niedrig" : "normal")
                        + "; Effekte mit Anhalten außerhalb des Bildes " + nCull + "; Trümmer-Partikel gesammelt gezeichnet " + nInst + "; Gischt/Bugwelle ferner Schiffe aus " + nSpray);
                mapSeconds = 0f;
            }
        }

        // map only: every running smoke/fire/spray effect waits and goes on exactly where it stopped
        private float nextMapScan;
        private void MapEffects(bool on, float now)
        {
            if (on)
            {
                if (mapPaused && now < nextMapScan) return;
                nextMapScan = now + 15f; mapPaused = true;
                foreach (var p in Object.FindObjectsOfType<ParticleSystem>())
                {
                    if (p == null || !p.isPlaying || p.isPaused) continue;
                    p.Pause(false); paused.Add(p);
                }
                nMapPaused = paused.Count;
            }
            else if (mapPaused)
            {
                mapPaused = false; int back = 0;
                foreach (var p in paused) if (p != null && p.isPaused) { p.Play(false); back++; }
                Util.Log("Sparen: Karte verlassen, " + back + " von " + paused.Count + " angehaltenen Effekten laufen weiter");
                paused.Clear();
            }
        }

        private void MapShadows(bool on)
        {
            if (on == mapShadow || ShadowField == null) return;
            if (mapCam == null && MainCamera.Instance != null) mapCam = AccessTools.Field(typeof(MainCamera), "mapController").GetValue(MainCamera.Instance) as MapCameraController;
            if (mapCam == null) return;
            ShadowField.SetValue(mapCam, on ? 1f : 900f); mapShadow = on;
            Util.Log("Sparen: Schatten in der reinen Kartenansicht " + (on ? "aus (Reichweite 1 m)" : "wieder wie im Spiel (900 m)"));
        }

        private void MapDetail(bool on)
        {
            if (on == mapLod) return;
            if (user == null) user = ScriptableObjectSingleton.LoadSingleton<UserSettings>();
            if (user == null) return;
            if (on) user.OverrideLODBias(0.15f); else user.ResetLODBias();
            mapLod = on;
            Util.Log("Sparen: Detailstufe in der reinen Kartenansicht " + (on ? "niedrig (0,15)" : "wieder wie eingestellt (" + QualitySettings.lodBias.ToString("0.00") + ")"));
        }

        // 3D view: an effect outside the picture waits ("qualmt erst wieder, wenn du hinsiehst")
        private void Unseen(bool on)
        {
            if (on)
            {
                foreach (var p in Object.FindObjectsOfType<ParticleSystem>())
                {
                    if (p == null || cull.ContainsKey(p)) continue;
                    var m = p.main; cull[p] = m.cullingMode;
                    if (m.cullingMode != ParticleSystemCullingMode.Pause) m.cullingMode = ParticleSystemCullingMode.Pause;
                }
                if (cull.Count > 4000) { var dead = new List<ParticleSystem>(); foreach (var k in cull.Keys) if (k == null) dead.Add(k); foreach (var k in dead) cull.Remove(k); }
                nCull = cull.Count;
            }
            else if (cull.Count > 0)
            {
                foreach (var kv in cull) if (kv.Key != null) { var m = kv.Key.main; m.cullingMode = kv.Value; }
                Util.Log("Sparen: Anhalten außerhalb des Bildes aus, " + cull.Count + " Effekte wieder wie im Spiel");
                cull.Clear(); nCull = 0;
            }
        }

        // debris made of 3D models: the graphics card draws all pieces of one effect in one go
        private void MeshGpu(bool on)
        {
            if (on)
            {
                foreach (var r in Object.FindObjectsOfType<ParticleSystemRenderer>())
                    if (r != null && r.renderMode == ParticleSystemRenderMode.Mesh && !r.enableGPUInstancing) { r.enableGPUInstancing = true; inst.Add(r); }
                inst.RemoveWhere(x => x == null); nInst = inst.Count;
            }
            else if (inst.Count > 0)
            {
                foreach (var r in inst) if (r != null) r.enableGPUInstancing = false;
                inst.Clear(); nInst = 0;
            }
        }

        // Every contact marker gets its own small drawing surface. Moving one marker then no longer makes the processor
        // rebuild the picture of ALL markers; the graphics card just draws each small surface again.
        private readonly List<Component> markerCanvas = new List<Component>();
        private readonly HashSet<GameObject> markerDone = new HashSet<GameObject>();
        private static readonly System.Reflection.FieldInfo Rep3d = AccessTools.Field(typeof(UBOAT.Game.UI.Map.EntityMarkerUI), "representation3d");
        private void Markers(bool on)
        {
            if (!on)
            {
                if (markerCanvas.Count == 0) return;
                for (int i = markerCanvas.Count - 1; i >= 0; i--) if (markerCanvas[i] != null) Object.Destroy(markerCanvas[i]);   // raycasters were added last, so they go first
                Util.Log("Sparen: eigene Zeichenflächen der Kontakt-Markierungen entfernt (" + markerCanvas.Count + " Teile)");
                markerCanvas.Clear(); markerDone.Clear();
                return;
            }
            foreach (var m in Object.FindObjectsOfType<UBOAT.Game.UI.Map.EntityMarkerUI>())
            {
                if (m == null) continue;
                Own(m.gameObject);
                var r = Rep3d != null ? Rep3d.GetValue(m) as RectTransform : null;
                if (r != null) Own(r.gameObject);
            }
            markerCanvas.RemoveAll(x => x == null); markerDone.RemoveWhere(x => x == null);
        }
        private void Own(GameObject go)
        {
            if (go == null || !markerDone.Add(go) || go.GetComponent<Canvas>() != null) return;
            var parent = go.transform.parent != null ? go.transform.parent.GetComponentInParent<Canvas>() : null;
            if (parent == null) return;
            markerCanvas.Add(go.AddComponent<Canvas>());
            if (parent.GetComponent<UnityEngine.UI.GraphicRaycaster>() != null) markerCanvas.Add(go.AddComponent<UnityEngine.UI.GraphicRaycaster>());   // keeps hover and click
        }

        // spray control and bow wave of ships farther than 2 km (or everything while only the map is shown) rest
        private void Spray(bool on)
        {
            if (!on)
            {
                if (sprayOff.Count > 0) { foreach (var b in sprayOff) if (b != null) b.enabled = true; sprayOff.Clear(); nSpray = 0; }
                return;
            }
            var cam = MainCamera.Instance; if (cam == null) return;
            Vector3 c = cam.transform.position; bool map = PureMap();
            var own = Util.PlayerShip != null ? Util.PlayerShip.transform : null;
            var list = new List<Behaviour>();
            foreach (var s in Object.FindObjectsOfType<ShipEffectsController>()) list.Add(s);
            if (BowType != null) foreach (var o in Object.FindObjectsOfType(BowType)) { var b = o as Behaviour; if (b != null) list.Add(b); }
            foreach (var b in sprayOff) if (b != null && !b.enabled) list.Add(b);
            foreach (var b in list)
            {
                if (b == null || (own != null && b.transform.IsChildOf(own))) continue;
                bool far = map || (b.transform.position - c).sqrMagnitude > FarSpray * FarSpray;
                if (far && b.enabled) { b.enabled = false; sprayOff.Add(b); }
                else if (!far && !b.enabled && sprayOff.Contains(b)) { b.enabled = true; sprayOff.Remove(b); }
            }
            sprayOff.RemoveWhere(x => x == null); nSpray = sprayOff.Count;
        }
    }
}

// ====================================================================================================
// Abschnitt: Patches/WedgeCalm.cs
// ====================================================================================================
namespace UboatToolkit.Patches
{
    using global::System.Runtime.CompilerServices;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.UI;
    using global::UBOAT.Game.Scene.Camera;

    // 0.4.53: the game rebuilds every contact wedge (the pink bearing fans) in every picture, also when nothing moved.
    // With the switch on, a wedge that stands still is rebuilt 4 times per second only; as soon as it moves again or the
    // camera height changes, it is rebuilt in every picture as before. Picture only.
    [HarmonyPatch(typeof(DynamicContactVisualizationUI), "LateUpdate")]
    internal static class WedgeCalm_Patch
    {
        private class State { public bool Still; public float At, CamY; }
        private static readonly ConditionalWeakTable<DynamicContactVisualizationUI, State> states = new ConditionalWeakTable<DynamicContactVisualizationUI, State>();
        public static int Ran, Skipped;

        private static bool Prefix(DynamicContactVisualizationUI __instance, float ___displayedBearing, float ___displayedDistance, float ___displayedSpreadAngle, ref float ___displayTime, out Vector3 __state)
        {
            __state = new Vector3(___displayedBearing, ___displayedDistance, ___displayedSpreadAngle);
            if (!Settings.B("SaveWedgesCalm")) return true;
            var st = states.GetOrCreateValue(__instance);
            var cam = MainCamera.Instance; float y = cam != null ? cam.transform.position.y : 0f;
            if (st.Still && ___displayTime > 3f && Time.unscaledTime - st.At < 0.25f && Mathf.Abs(y - st.CamY) <= 0.005f * Mathf.Max(1f, Mathf.Abs(st.CamY)))
            {
                ___displayTime += Time.unscaledDeltaTime; Skipped++;
                __state.x = float.NaN;
                return false;
            }
            return true;
        }

        private static void Postfix(DynamicContactVisualizationUI __instance, float ___displayedBearing, float ___displayedDistance, float ___displayedSpreadAngle, Vector3 __state)
        {
            if (float.IsNaN(__state.x) || !Settings.B("SaveWedgesCalm")) return;
            Ran++;
            var st = states.GetOrCreateValue(__instance);
            var cam = MainCamera.Instance;
            st.Still = Mathf.Abs(Mathf.DeltaAngle(___displayedBearing, __state.x)) < 0.01f && Mathf.Abs(___displayedDistance - __state.y) < 0.002f * Mathf.Max(1f, __state.y) && Mathf.Abs(___displayedSpreadAngle - __state.z) < 0.0001f;
            st.At = Time.unscaledTime; st.CamY = cam != null ? cam.transform.position.y : 0f;
        }
    }
}

// ====================================================================================================
// Abschnitt: Patches/RouteSearchCount.cs
// ====================================================================================================
namespace UboatToolkit.Patches
{
    using global::System.Collections.Generic;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Sandbox;

    // 0.4.54 (DoMe 2026-11-18: "nutzen wir NavMeshQuery / asynchrone Wege?"). The game searches every ship and convoy
    // route in one piece on the main core. This only counts how often and how long, once per minute, nothing is changed.
    [HarmonyPatch]
    internal static class RouteSearchCount_Patch
    {
        private static int calls; private static double sum, longest; private static float nextLog;

        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var m in AccessTools.GetDeclaredMethods(typeof(WorldNavMesh))) if (m.Name == "FindPath") yield return m;
        }

        private static void Prefix(out double __state) { __state = Time.realtimeSinceStartupAsDouble; }

        private static void Postfix(double __state)
        {
            double ms = (Time.realtimeSinceStartupAsDouble - __state) * 1000.0;
            calls++; sum += ms; if (ms > longest) longest = ms;
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 60f;
            Util.Log("Routen-Suche der Schiffe (Zählung, nur lesen): " + calls + " mal in der letzten Minute, zusammen " + sum.ToString("0") + " ms auf dem Hauptkern, längste einzelne " + longest.ToString("0.0") + " ms");
            calls = 0; sum = 0; longest = 0;
        }
    }
}

// ====================================================================================================
// Abschnitt: Patches/SaveCpu.cs
// ====================================================================================================
namespace UboatToolkit.Patches
{
    using global::System.Runtime.CompilerServices;
    using global::HarmonyLib;
    using global::UnityEngine;
    using global::UBOAT.Game.Core;
    using global::UBOAT.Game.Scene.Effects;
    using global::UBOAT.Game.Scene.Entities;

    // 0.4.55: the game forces a memory clean-up whenever its memory grew by 100 MB (crew models of a ship coming
    // close, new terrain). That stops the picture for up to 39 ms. With the switch on this happens only while a save
    // is loading; the game's own clean-up in small steps (3 ms slices) keeps running.
    [HarmonyPatch(typeof(GameState), nameof(GameState.CollectMemory))]
    internal static class NoForcedGc_Patch
    {
        public static int Skipped; private static float nextLog;
        private static bool Prefix()
        {
            if (!Settings.B("SaveNoForcedGc") || UboatToolkit.Modules.SaveTools_LoadSpeedPatch.IsLoading || UboatToolkit.Modules.OfficerWatch.Loading()) return true;
            Skipped++;
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 60f;
                Util.Log("Sparen: erzwungenes Speicher-Aufräumen des Spiels " + Skipped + " mal ausgelassen (Speicher jetzt " + (System.GC.GetTotalMemory(false) / 1048576L) + " MB)");
            }
            return false;
        }
    }

    // 0.4.55: the game writes the spray strength of every ship in every physics step, also when the ship keeps its
    // speed. With the switch on it is written only when the speed step changed by more than 1 %.
    [HarmonyPatch(typeof(ShipEffectsController), "FixedUpdate")]
    internal static class SprayOnChange_Patch
    {
        private class Last { public float V = -1f; }
        private static readonly ConditionalWeakTable<ShipEffectsController, Last> last = new ConditionalWeakTable<ShipEffectsController, Last>();
        public static int Ran, Skipped; private static float nextLog;
        private static bool Prefix(ShipEffectsController __instance, Ship ___ship, float ___maxVelocity)
        {
            if (!Settings.B("SaveSprayOnChange") || ___ship == null || ___maxVelocity <= 0f) return true;
            float v = ___ship.AverageVelocity / ___maxVelocity;
            if (v > 1f) v = 1f; else if (v < 0.15f) v = 0f;
            var l = last.GetOrCreateValue(__instance);
            bool skip = Mathf.Abs(v - l.V) < 0.01f && !(v == 0f && l.V != 0f) && !(v == 1f && l.V != 1f);
            if (skip) Skipped++; else { Ran++; l.V = v; }
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 60f;
                Util.Log("Sparen: Gischt-Stärke gesetzt " + Ran + " mal, ausgelassen " + Skipped + " mal in der letzten Minute");
                Ran = 0; Skipped = 0;
            }
            return !skip;
        }
    }
}

// ====================================================================================================
// Abschnitt: Patches/BuoyancyEarly.cs
// ====================================================================================================
namespace UboatToolkit.Patches
{
    using global::HarmonyLib;
    using global::UnityEngine;

    // 0.4.56: the game hands the buoyancy sum of every ship to the helper cores, but the helpers only start when the
    // main core begins to wait for the first result. With the switch on the helpers are woken after every 4th ship,
    // so they calculate while the main core still prepares the other ships. Same numbers, same order of results.
    [HarmonyPatch]
    internal static class BuoyancyEarly_Patch
    {
        private static int since, kicks, jobs; private static float nextLog;
        private static System.Reflection.MethodBase target;
        private static bool Prepare()
        {
            var t = AccessTools.TypeByName("PlayWay.Water.Extensions.NeuralNetworkJobsRunner");
            target = t != null ? AccessTools.Method(t, "Start") : null;
            if (target == null) Util.Log("Auftrieb früher starten: Stelle im Wasser-Modul nicht gefunden, Schalter ohne Wirkung");
            return target != null;
        }
        private static System.Reflection.MethodBase TargetMethod() => target;

        private static void Postfix()
        {
            if (!Settings.B("SaveBuoyancyEarly")) return;
            jobs++;
            if (++since >= 4) { since = 0; kicks++; Unity.Jobs.JobHandle.ScheduleBatchedJobs(); }
            if (Time.unscaledTime < nextLog) return;
            nextLog = Time.unscaledTime + 60f;
            Util.Log("Sparen: Auftrieb früher gestartet, " + jobs + " Schiffs-Rechnungen in der letzten Minute, Helfer-Kerne " + kicks + " mal vorzeitig geweckt");
            jobs = 0; kicks = 0;
        }
    }
}

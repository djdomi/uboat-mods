using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Scene.Camera;
using UBOAT.Game.Scene.Camera.ExternalControllers;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene;

namespace UboatToolkit.Modules
{
    // Course and depth keys: arrows turn 90° (Shift 45°, Ctrl 5°, Ctrl+Shift 1°), Down = turn about,
    // Up = rudder amidships, the turn is ordered when the modifiers are let go. With a modifier,
    // releasing F/R changes depth by 20/5/1 m; Ctrl+F+R holds the current depth; Ctrl+P periscope
    // depth (Ctrl+Shift+P exactly 20 m); Ctrl+C max safe depth; Ctrl+CapsLock locks course keys.
    // The compass port/starboard buttons follow the same step size.
    internal static class CourseCfg
    {
        public static bool On => Settings.B("CourseKeys");
        public static KeyCode K(string key, KeyCode def) => Keys.Code(key, def);
        public static bool Held(KeyCode k)
        {
            if (k == KeyCode.None) return false;
            if (Input.GetKey(k)) return true;
            if (k == KeyCode.LeftControl || k == KeyCode.RightControl) return Keys.Ctrl;
            if (k == KeyCode.LeftShift || k == KeyCode.RightShift) return Keys.Shift;
            if (k == KeyCode.LeftAlt || k == KeyCode.RightAlt) return Keys.Alt;
            return false;
        }
        public static bool Down(KeyCode k) => k != KeyCode.None && Input.GetKeyDown(k);
        public static bool Up(KeyCode k) => k != KeyCode.None && Input.GetKeyUp(k);
        public static KeyCode Fine => K("KeyCourseFine", KeyCode.LeftControl);
        public static KeyCode Coarse => K("KeyCourseCoarse", KeyCode.LeftShift);
        public static bool AnyModifier() => Held(Fine) || Held(Coarse);
        public static float Step() { bool f = Held(Fine), c = Held(Coarse); return f && c ? 1f : c ? 45f : f ? 5f : 90f; }
        public static void Log(string m) { if (Settings.B("CourseLog")) Util.Log("Kurs/Tiefe: " + m); }

        private static readonly FieldInfo GameInputF = AccessTools.Field(typeof(MainCamera), "gameInput");
        private static UBOAT.Game.Core.Input.GameInput gameInput;
        public static bool InputBlocked()
        {
            if (gameInput == null && GameInputF != null) gameInput = GameInputF.GetValue(null) as UBOAT.Game.Core.Input.GameInput;
            return gameInput != null && gameInput.HasActiveBlockades();
        }
        public static bool InFreeCam() { var c = MainCamera.Instance; return c != null && c.CurrentMode == CameraMode.FreeFly; }
        public static bool AtScope() { var c = MainCamera.Instance; return c != null && c.GenericController != null && c.GenericController.ExternalController is PeriscopeController; }

        public static readonly MethodInfo SetTargetCourse = AccessTools.Method(typeof(PlayerShip), "SetTargetCourse", new[] { typeof(float) });
        public static readonly MethodInfo SetRudderAmidships = AccessTools.Method(typeof(PlayerShip), "SetRudderAmidships");
        public static readonly MethodInfo SetTargetDepth = AccessTools.Method(typeof(PlayerShip), "SetTargetDepth", new[] { typeof(float), typeof(bool), typeof(bool) });
        public static readonly MethodInfo SetDepthPreset = AccessTools.Method(typeof(PlayerShip), "SetDepthPreset", new[] { typeof(DepthPreset), typeof(bool) });
        public static bool Ready => SetTargetCourse != null && SetRudderAmidships != null && SetTargetDepth != null && SetDepthPreset != null;
    }

    [HarmonyPatch(typeof(PlayerShip), "Update")]
    internal static class CourseKeys_Patch
    {
        private static float pendingCourse, pendingDepth;
        private static bool blocker, chordUsed, errorLogged;

        private static void Postfix(PlayerShip __instance)
        {
            if (!CourseCfg.On || !CourseCfg.Ready) return;
            try
            {
                if (CourseCfg.InputBlocked()) return;
                bool atScope = false;
                if (Settings.B("CourseBlockScopes")) { if (CourseCfg.InFreeCam()) return; atScope = CourseCfg.AtScope(); }
                bool fine = CourseCfg.Held(CourseCfg.Fine), coarse = CourseCfg.Held(CourseCfg.Coarse);
                Course(__instance, fine, coarse, atScope);
                if (!atScope) Depth(__instance, fine, coarse);
            }
            catch (Exception e) { if (!errorLogged) { errorLogged = true; Util.Log("Kurs/Tiefe: Tasten fehlgeschlagen (einmalig): " + e); } }
        }

        // At a scope only course keys on the arrow keys still work (they clash with no station control).
        private static bool CourseDown(KeyCode k, bool atScope) =>
            (!atScope || k == KeyCode.LeftArrow || k == KeyCode.RightArrow || k == KeyCode.UpArrow || k == KeyCode.DownArrow) && CourseCfg.Down(k);

        private static void Course(PlayerShip ship, bool fine, bool coarse, bool atScope)
        {
            if (!atScope && fine && CourseCfg.Down(CourseCfg.K("KeyCourseBlocker", KeyCode.CapsLock)))
            {
                blocker = !blocker;
                Util.Log("Kurs/Tiefe: Kurs-Sperre " + (blocker ? "an" : "aus"));
            }
            bool blocked = ship.Docked || blocker;
            float step = fine && coarse ? 1f : coarse ? 45f : fine ? 5f : 90f;
            if (CourseDown(CourseCfg.K("KeyCoursePort", KeyCode.LeftArrow), atScope)) pendingCourse -= step;
            else if (CourseDown(CourseCfg.K("KeyCourseStarboard", KeyCode.RightArrow), atScope)) pendingCourse += step;
            else if (CourseDown(CourseCfg.K("KeyCourseAbout", KeyCode.DownArrow), atScope)) pendingCourse += 180f;
            if (CourseDown(CourseCfg.K("KeyCourseAmidships", KeyCode.UpArrow), atScope) && !blocked) CourseCfg.SetRudderAmidships.Invoke(ship, null);
            if (!fine && !coarse && pendingCourse != 0f && !blocked)
            {
                CourseCfg.SetTargetCourse.Invoke(ship, new object[] { pendingCourse });
                CourseCfg.Log("Kurs geändert um " + pendingCourse + "°");
                pendingCourse = 0f;
            }
        }

        private static void Preset(PlayerShip s, DepthPreset p, bool instant) => CourseCfg.SetDepthPreset.Invoke(s, new object[] { p, instant });
        private static void SetDepth(PlayerShip s, float d, bool instant) => CourseCfg.SetTargetDepth.Invoke(s, new object[] { d, false, instant });

        private static void Depth(PlayerShip ship, bool fine, bool coarse)
        {
            var deeper = CourseCfg.K("KeyCourseDeeper", KeyCode.F);
            var shallower = CourseCfg.K("KeyCourseShallower", KeyCode.R);
            bool dHeld = CourseCfg.Held(deeper), sHeld = CourseCfg.Held(shallower);
            if (chordUsed && !dHeld && !sHeld) { chordUsed = false; pendingDepth = 0f; }
            if (fine && dHeld && CourseCfg.Down(shallower))
            {
                chordUsed = true;
                float held = ship.Depth + ship.VerticalOriginOffset;
                Preset(ship, DepthPreset.None, true); SetDepth(ship, held, true);
                CourseCfg.Log("Tiefe gehalten bei " + held.ToString("F1") + " m");
                return;
            }
            if (fine && CourseCfg.Down(CourseCfg.K("KeyCoursePeriscope", KeyCode.P)))
            {
                if (coarse) { Preset(ship, DepthPreset.None, true); SetDepth(ship, 20f, true); CourseCfg.Log("Zieltiefe 20 m"); }
                else { Preset(ship, DepthPreset.Periscope, false); CourseCfg.Log("Sehrohrtiefe"); }
                return;
            }
            if (fine && CourseCfg.Down(CourseCfg.K("KeyCourseMaxDepth", KeyCode.C)))
            {
                Preset(ship, DepthPreset.MaxSafeDepth, false);
                CourseCfg.Log("größte sichere Tiefe");
                return;
            }
            if (!chordUsed)
            {
                float step = fine && coarse ? 1f : fine ? 5f : coarse ? 20f : 0f;
                if (CourseCfg.Up(deeper) && !sHeld) pendingDepth += step;
                else if (CourseCfg.Up(shallower) && !dHeld) pendingDepth -= step;
            }
            if (!fine && !coarse && pendingDepth != 0f)
            {
                float d = Mathf.Max(ship.TargetDepth + pendingDepth, 0f);
                Preset(ship, DepthPreset.None, true); SetDepth(ship, d, true);
                CourseCfg.Log("Tiefe auf " + d.ToString("F1") + " m");
                pendingDepth = 0f;
            }
        }
    }

    // A compass preset chosen while a modifier is held stays silent (no voice line per step).
    [HarmonyPatch(typeof(PlayerShip), "SetCompassPreset")]
    internal static class CourseKeys_SilentPreset
    {
        private static void Prefix(ref bool silent) { if (CourseCfg.On && CourseCfg.AnyModifier()) silent = true; }
    }

    // The compass port/starboard buttons turn by the modifier step (90/45/5/1°).
    [HarmonyPatch(typeof(PlayerShip), "ValidateCompassPreset")]
    internal static class CourseKeys_CompassStep
    {
        private static bool Prefix(ref CompassPreset ___compassPreset, PlayerShip __instance)
        {
            if (!CourseCfg.On || CourseCfg.SetTargetCourse == null) return true;
            try
            {
                if (___compassPreset == CompassPreset.CourseChangePort) { CourseCfg.SetTargetCourse.Invoke(__instance, new object[] { -CourseCfg.Step() }); return false; }
                if (___compassPreset == CompassPreset.CourseChangeStarboard) { CourseCfg.SetTargetCourse.Invoke(__instance, new object[] { CourseCfg.Step() }); return false; }
            }
            catch (Exception e) { Util.Log("Kurs/Tiefe: Kompass-Schritt fehlgeschlagen, Spielwert: " + e.Message); }
            return true;
        }
    }
}

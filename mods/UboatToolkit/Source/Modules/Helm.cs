using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Sandbox.HQ;
using UBOAT.Game.Scene.Entities;

namespace UboatToolkit.Modules
{
    // Course and depth on keys (Tasten tab): turn by 1/5/45/180 degrees, rudder amidships,
    // 5/20 m deeper or shallower, periscope depth, max safe depth, hold current depth.
    public class Helm : ModuleBase
    {
        private static readonly System.Reflection.MethodInfo course = AccessTools.Method(typeof(PlayerShip), "SetTargetCourse", new[] { typeof(float) });
        private static readonly System.Reflection.MethodInfo amid = AccessTools.Method(typeof(PlayerShip), "SetRudderAmidships");
        private static readonly string[] turnKeys = { "KeyTurnL1", "KeyTurnR1", "KeyTurnL5", "KeyTurnR5", "KeyTurnL45", "KeyTurnR45", "KeyTurnAbout" };
        private static readonly float[] turns = { -1, 1, -5, 5, -45, 45, 180 };
        private static readonly string[] depthKeys = { "KeyDeeper5", "KeyShallower5", "KeyDeeper20", "KeyShallower20" };
        private static readonly float[] steps = { 5, -5, 20, -20 };

        // 0.4.11 (DoMe: rudder dial stuck at 35 right, boat turns on the spot). The game's rudder dial sets the rudder
        // by hand ("Locked") and deletes the route; the helmsman then no longer steers until a new course is ordered.
        private float nextHelmLine; private int helmEmptyLines;
        private int lastFn = -1; private float deadSince, nextDeadNote;
        private int lastLock = int.MinValue; private float lockSince, nextNote, nextLine;
        public override void Tick()
        {
            var ship = Util.PlayerShip;
            if (!Util.ShipReady || ship.Rudders == null || ship.Rudders.Length == 0 || ship.Rudders[0] == null) return;
            var r = ship.Rudders[0];
            HelmCalm_Patch.Tick();
            // 0.4.12: rudder out of order = nobody at the helm (game rule with expanded schedules)
            if (!r.IsFunctional && Settings.B("CheatRudderNoHelmsman")) { foreach (var x in ship.Rudders) if (x != null) x.IsFunctional = true; }
            if (UnityEngine.Time.unscaledTime >= nextHelmLine)
            {
                nextHelmLine = UnityEngine.Time.unscaledTime + 60f;
                Util.Safe("Helm.Status", () =>
                {
                    foreach (var h in Util.Find<UBOAT.Game.Scene.Items.HelmsmanStation>())
                    {
                        if (h == null) continue;
                        var a = h.GetComponent<UBOAT.Game.Scene.Characters.Activators.Activator>();
                        int users = a != null ? a.Users.Count : -1;
                        if (users <= 0) helmEmptyLines++; else helmEmptyLines = 0;
                        Util.Log("Steuerstand: " + (users > 0 ? "besetzt" : "LEER (seit " + helmEmptyLines + " Minuten-Zeilen)") + ", Leistung " + h.Performance.Value.ToString("0.##") + ", Ruder in Betrieb: " + (r.IsFunctional ? "ja" : "nein") + ", Zeitraffer x" + UnityEngine.Time.timeScale.ToString("0"));
                    }
                });
            }
            int fn = r.IsFunctional ? 1 : 0;
            if (fn != lastFn)
            {
                lastFn = fn; deadSince = UnityEngine.Time.unscaledTime;
                string perf = "?"; try { foreach (var h in Util.Find<UBOAT.Game.Scene.Items.HelmsmanStation>()) if (h != null) perf = h.Performance.Value.ToString("0.##") + ", Leute am Stand " + (h.GetComponent<UBOAT.Game.Scene.Characters.Activators.Activator>()?.Users.Count ?? -1); } catch { }
                Util.Log(fn == 1 ? "Ruder: in Betrieb (Steuerstand Leistung " + perf + ")"
                    : "Ruder: AUSSER BETRIEB, bleibt stehen auf " + Side(UnityEngine.Mathf.RoundToInt(r.TargetTurn * -35f)) + ". Grund im Spiel: Steuerstand unbesetzt (Leistung " + perf + ")");
            }
            if (fn == 0 && Settings.B("RudderLockNote") && UnityEngine.Time.unscaledTime - deadSince > 10f && UnityEngine.Time.unscaledTime >= nextDeadNote)
            {
                nextDeadNote = UnityEngine.Time.unscaledTime + 60f;
                ToolkitRunner.ShowNotice("Ruder reagiert nicht: niemand am Steuerstand. Einen Mann ans Steuer schicken.", 8f);
            }
            int deg = r.Locked ? UnityEngine.Mathf.RoundToInt(r.TargetTurn * -35f) : int.MinValue;
            float now = UnityEngine.Time.unscaledTime;
            if (deg != lastLock)
            {
                lastLock = deg; lockSince = now; nextLine = now + 60f;
                string pads = ""; try { pads = string.Join(", ", System.Array.FindAll(UnityEngine.Input.GetJoystickNames(), n => !string.IsNullOrEmpty(n))); } catch { }
                Util.Log(deg == int.MinValue ? "Ruder: wieder frei, Steuermann steuert"
                    : "Ruder: von Hand festgestellt auf " + Side(deg) + " (Ruder-Rad des Spiels, Route gelöscht). Toolkit setzt das Ruder nie fest. Gamepad/Joystick: " + (pads.Length == 0 ? "keiner" : pads));
            }
            if (deg == int.MinValue) return;
            if (now >= nextLine) { nextLine = now + 60f; Util.Log("Ruder: steht seit " + (now - lockSince).ToString("0") + " s fest auf " + Side(deg) + ", Ruder funktioniert: " + (r.IsFunctional ? "ja" : "nein")); }
            if (Settings.B("RudderLockNote") && UnityEngine.Mathf.Abs(deg) >= 20 && now - lockSince > 20f && now >= nextNote)
            {
                nextNote = now + 120f;
                ToolkitRunner.ShowNotice("Ruder steht fest auf " + Side(deg) + ". Neuer Kurs oder Taste Ruder mittschiffs löst es.", 8f);
            }
        }
        private static string Side(int deg) { return deg == 0 ? "0° (mittschiffs)" : System.Math.Abs(deg) + "° " + (deg > 0 ? "rechts" : "links"); }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            for (int i = 0; i < turnKeys.Length; i++)
                if (Keys.Down(turnKeys[i])) { float d = turns[i]; Util.Safe("Helm.Turn", () => course?.Invoke(ship, new object[] { d })); return; }
            if (Keys.Down("KeyAmidships")) { Util.Safe("Helm.Amid", () => amid?.Invoke(ship, null)); return; }
            for (int i = 0; i < depthKeys.Length; i++)
                if (Keys.Down(depthKeys[i]))
                {
                    float s = steps[i];
                    Util.Safe("Helm.Depth", () => { float t = UnityEngine.Mathf.Max(0f, ship.TargetDepth + s); ship.SetDepthPreset(DepthPreset.None, true); ship.SetTargetDepth(t, false, true); });
                    return;
                }
            if (Keys.Down("KeyPeriscopeDepth")) { Util.Safe("Helm.Scope", () => ship.SetDepthPreset(DepthPreset.Periscope, false)); return; }
            if (Keys.Down("KeyMaxDepth")) { Util.Safe("Helm.Max", () => ship.SetDepthPreset(DepthPreset.MaxSafeDepth, false)); return; }
            if (Keys.Down("KeyHoldDepth")) Util.Safe("Helm.Hold", () => { float t = ship.Depth + ship.VerticalOriginOffset; ship.SetDepthPreset(DepthPreset.None, true); ship.SetTargetDepth(UnityEngine.Mathf.Max(0f, t), false, true); });
        }
    }

    // Wolfpack: if HQ is busy sending at that moment, try again later instead of dropping the wolfpack.
    [HarmonyPatch(typeof(Headquarters.WolfpackInitializer), "StartWolfpack")]
    internal static class Helm_WolfpackRetryPatch
    {
        private static readonly Dictionary<object, int> tries = new Dictionary<object, int>();
        private static bool Prefix(Headquarters.WolfpackInitializer __instance, Headquarters ___headquarters, ref float __result)
        {
            if (!Settings.B("FixWolfpackRetry") || ___headquarters == null) return true;
            var e = Traverse.Create(___headquarters).Field("sandboxEntity").GetValue<SandboxEntity>();
            if (e == null || e.Transmission == null) { tries.Remove(__instance); return true; }
            tries.TryGetValue(__instance, out int n);
            if (n >= 20) { tries.Remove(__instance); return true; }
            if (tries.Count > 200) tries.Clear();
            tries[__instance] = n + 1;
            __result = 30f;   // ask again in 30 s
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    // 0.4.16 (measured 2026-11-18 on DoMe's boat: the helmsman comes and goes every ~12 s, "Path to target is invalid:
    // Slot, Work, Helmsman Station on Conning Tower Interior"). The game has two helm places: control room and conning
    // tower. HelmsmanStation.ValidateSlotsState moves the helm to the tower whenever somebody RESERVES the attack
    // periscope (or the control room is flooded) and back when the reservation ends, switching the other place off.
    // Every change throws the helmsman out; with expanded schedules the rudder is dead while he walks.
    // Now the place changes at most every x seconds (flooding always switches at once), and every change is logged
    // with the name of the man at the attack periscope.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Items.HelmsmanStation), "ValidateSlotsState")]
    internal static class HelmCalm_Patch
    {
        private static readonly AccessTools.FieldRef<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot> scope =
            AccessTools.FieldRefAccess<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot>("attackPeriscopeInteractableSlot");
        private static readonly AccessTools.FieldRef<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]> tower =
            AccessTools.FieldRefAccess<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]>("conningTowerSlots");
        private static readonly AccessTools.FieldRef<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]> room =
            AccessTools.FieldRefAccess<UBOAT.Game.Scene.Items.HelmsmanStation, UBOAT.Game.Scene.Interactions.InteractableSlot[]>("controlRoomSlots");
        private static readonly System.Reflection.MethodInfo validate = AccessTools.Method(typeof(UBOAT.Game.Scene.Items.HelmsmanStation), "ValidateSlotsState");

        private static float lastSwitch = -999f, nextLine;
        private static UBOAT.Game.Scene.Items.HelmsmanStation pending;
        internal static int Switches, Held, Calls;
        private static bool bypass;

        private static bool Prefix(UBOAT.Game.Scene.Items.HelmsmanStation __instance)
        {
            Calls++;
            if (bypass) return true;
            try
            {
                var t = tower(__instance); var r = room(__instance); var s = scope(__instance);
                if (t == null || r == null || t.Length == 0 || r.Length == 0 || t[0] == null) return true;
                bool flooded = true;
                for (int i = 0; i < r.Length; i++) if (r[i] != null && !r[i].IsBelowWater) { flooded = false; break; }
                var who = s != null ? s.ReservingCharacter : null;
                bool wantTower = flooded || who != null;
                bool isTower = t[0].gameObject.activeSelf;
                if (wantTower == isTower) return true;            // nothing changes, let the game tidy up
                float now = UnityEngine.Time.unscaledTime;
                float min = Settings.B("HelmCalm") ? Settings.F("HelmCalmSeconds") : 0f;
                if (!flooded && now - lastSwitch < min) { Held++; pending = __instance; return false; }
                lastSwitch = now; Switches++; pending = null;
                if (Switches <= 60)
                    Util.Log("Steuerstand: Platz wechselt in " + (wantTower ? "den Turm" : "die Zentrale") + ". Grund: " +
                        (flooded ? "Zentrale unter Wasser" : who != null ? "Angriffssehrohr reserviert von " + who.name : "Angriffssehrohr wieder frei"));
            }
            catch (System.Exception e) { Util.Log("Steuerstand: Platz-Prüfung Fehler " + e.GetType().Name); }
            return true;
        }

        // called once per second from Helm.Tick: carry out a change that was held back, and write the minute line
        internal static void Tick()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (pending != null && now - lastSwitch >= (Settings.B("HelmCalm") ? Settings.F("HelmCalmSeconds") : 0f))
            {
                var p = pending; pending = null;
                if (p) Util.Safe("Helm.Calm", () => { bypass = true; try { validate.Invoke(p, null); } finally { bypass = false; } lastSwitch = now; Switches++; });
            }
            if (now >= nextLine)
            {
                nextLine = now + 60f;
                if (Calls > 0) Util.Log("Steuerstand: Platz geprüft " + Calls + " mal, gewechselt " + Switches + " mal, zurückgehalten " + Held + " mal (höchstens alle " + Settings.F("HelmCalmSeconds").ToString("0") + " s)");
                Calls = 0; Switches = 0; Held = 0;
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using DWS.Common.InjectionFramework;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Environment.Terrain;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Own depth gauge + depth keys + keel-clearance autopilot (DoMe 2026-11-12).
    //  - Gauge: scale up to "DepthGaugeMax" m (no 260 m limit), current depth (white), target (yellow),
    //    sea floor (brown), history of the last minutes (dots). Click or mouse wheel on it sets the target.
    //  - Keys: "deeper"/"shallower" key alone = DepthStep m, with Shift x10, with Alt x100. Same for the wheel.
    //  - Autopilot: keeps "AutoDepthClearance" m of water under the keel, looks ahead along the course
    //    and rises early. Mode "Aufsetzen": sinks onto the sea floor and switches on silent running.
    public class DepthGauge : ModuleBase
    {
        private const float W = 170f, H = 320f;
        private static readonly List<float> history = new List<float>();
        private float nextSample, nextPilot, lastWarn;
        private static string pilotText = "";
        private static float lastBed = float.NaN;

        private static Rect Box() => new Rect(Mathf.Clamp(Screen.width * Settings.F("DepthGaugeXPos") / 100f, 0f, Screen.width - W), Mathf.Clamp(Screen.height * Settings.F("DepthGaugeY") / 100f, 0f, Screen.height - H), W, H);
        private static Rect Scale() { var r = Box(); return new Rect(r.x + 44, r.y + 26, 40, r.height - 70); }
        // Auto zoom: in shallow water the scale only goes a bit below the sea floor / the boat, in deep water up to the setting.
        private static float Max
        {
            get
            {
                float cap = Mathf.Max(30f, Settings.F("DepthGaugeMax"));
                var ship = Util.PlayerShip;
                if (ship == null || float.IsNaN(lastBed) || !Settings.B("DepthGaugeAutoZoom")) return cap;
                float need = Mathf.Max(lastBed, Mathf.Max(Current(ship), ship.TargetDepth)) * 1.25f;
                float step = need <= 50f ? 10f : need <= 200f ? 25f : 50f;
                return Mathf.Clamp(Mathf.Ceil(need / step) * step, 30f, cap);
            }
        }
        private static bool dragging; private static Vector2 dragOff; private static float lastClick = -1f;

        private static float Step()
        {
            float s = Mathf.Max(0.1f, Settings.F("DepthStep"));
            return Keys.Alt ? s * 100f : Keys.Shift ? s * 10f : s;
        }

        // Keel depth in "target depth" units: the game's target depth refers to the boat's origin, not the keel.
        private static float KeelOffset(PlayerShip ship) => ship.BottomDepth - (ship.Depth + ship.VerticalOriginOffset);
        private static float Current(PlayerShip ship) => Mathf.Max(0f, ship.Depth + ship.VerticalOriginOffset);

        private static void SetTarget(PlayerShip ship, float depth)
        {
            float t = Mathf.Max(0f, depth);
            ship.SetDepthPreset(DepthPreset.None, true);
            ship.SetTargetDepth(t, true, false);
        }

        public override void Frame()
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;

            if (Settings.B("DepthHotkeys"))
            {
                bool deeper = Input.GetKeyDown(Keys.Code("KeyDepthDeeper", KeyCode.PageDown));
                bool higher = Input.GetKeyDown(Keys.Code("KeyDepthShallower", KeyCode.PageUp));
                if ((deeper || higher) && !Keys.Ctrl)
                {
                    float s = Step() * (deeper ? 1f : -1f);
                    Util.Safe("Depth.Key", () => { SetTarget(ship, ship.TargetDepth + s); ToolkitRunner.ShowNotice("Zieltiefe " + ship.TargetDepth.ToString("F0") + " m", 2f); });
                }
            }
            if (Keys.Down("KeyAutoDepth")) { var o = Settings.Get("AutoDepth"); if (o != null) { o.Value = o.Value > 0 ? 0 : 1; ToolkitRunner.ShowNotice("Bodenabstand-Autopilot " + (o.Value > 0 ? "AN" : "AUS"), 3f); } }
            if (Keys.Down("KeyAutoDepthBottom")) { var o = Settings.Get("AutoDepthBottom"); if (o != null) { o.Value = o.Value > 0 ? 0 : 1; ToolkitRunner.ShowNotice(o.Value > 0 ? "Aufsetzen auf den Grund" : "Aufsetzen beendet", 3f); } }

            if (Settings.B("DepthGauge"))
            {
                // Drag the box by its title line; the position is saved in percent of the screen.
                {
                    var bx = Box(); var mp = Input.mousePosition; var pp = new Vector2(mp.x, Screen.height - mp.y);
                    if (Input.GetMouseButtonDown(0) && new Rect(bx.x, bx.y, bx.width, 22).Contains(pp)) { dragging = true; dragOff = pp - bx.position; }
                    if (dragging && Input.GetMouseButton(0))
                    {
                        var np = pp - dragOff;
                        Settings.Set("DepthGaugeXPos", Mathf.Clamp(np.x / Screen.width * 100f, 0f, 100f));
                        Settings.Set("DepthGaugeY", Mathf.Clamp(np.y / Screen.height * 100f, 0f, 100f));
                    }
                    if (dragging && !Input.GetMouseButton(0)) { dragging = false; Util.Safe("Depth.SavePos", Settings.Save); }
                    if (dragging) return;
                }
                var sc = Scale();
                var m = Input.mousePosition; var p = new Vector2(m.x, Screen.height - m.y);
                var hit = new Rect(sc.x - 30, sc.y, sc.width + 60, sc.height);
                if (hit.Contains(p))
                {
                    // Double click only, so a stray click never sends the boat down.
                    if (Input.GetMouseButtonDown(0))
                    {
                        float now = Time.unscaledTime;
                        if (now - lastClick < 0.4f)
                        {
                            float d = (p.y - sc.y) / sc.height * Max;
                            Util.Safe("Depth.Click", () => SetTarget(ship, Mathf.Round(d)));
                            ToolkitRunner.ShowNotice("Zieltiefe " + Mathf.Round(d).ToString("F0") + " m", 2f);
                            lastClick = -1f;
                        }
                        else lastClick = now;
                    }
                    float wheel = Input.mouseScrollDelta.y;
                    if (Mathf.Abs(wheel) > 0.01f) Util.Safe("Depth.Wheel", () => SetTarget(ship, ship.TargetDepth - Mathf.Sign(wheel) * Step()));
                }
            }
        }

        public override void Tick() { }

        // Runs from Frame-like timing via the GUI pass; sampling + autopilot every 0.5 s real time.
        internal void Update(PlayerShip ship)
        {
            float now = Time.unscaledTime;
            if (now >= nextSample)
            {
                nextSample = now + Mathf.Max(0.2f, Settings.F("DepthGaugeSeconds") / 120f);
                history.Add(Current(ship));
                if (history.Count > 120) history.RemoveAt(0);
            }
            if (now < nextPilot) return;
            nextPilot = now + 0.5f;
            lastBed = Util.SafeGet(() => Seabed(ship, 0f), float.NaN);
            if (!Settings.B("AutoDepth")) { pilotText = ""; return; }

            float keel = KeelOffset(ship);
            if (Settings.B("AutoDepthBottom"))
            {
                if (float.IsNaN(lastBed)) { pilotText = "Aufsetzen: Grund unbekannt"; return; }
                float t = lastBed - keel - 0.3f;
                if (Mathf.Abs(ship.TargetDepth - t) > 0.5f) SetTarget(ship, t);
                Util.Safe("Depth.Silent", () =>
                {
                    var src = InjectionFramework.Instance.GetInstance<UBOAT.Game.Scene.Tasks.ISilentRunningController>();
                    if (src != null && !src.IsSilentRunningEnabled && ship.BottomDepth > lastBed - 5f) src.SetSilentRunningEnabled(true, true);
                });
                pilotText = "Aufsetzen auf " + lastBed.ToString("F0") + " m";
                return;
            }

            // Shallowest sea floor now and ahead along the course.
            float look = Mathf.Max(0f, Settings.F("AutoDepthLookAhead"));
            float bed = lastBed;
            foreach (float dist in new[] { look * 0.25f, look * 0.5f, look })
            {
                float b = Util.SafeGet(() => Seabed(ship, dist), float.NaN);
                if (!float.IsNaN(b)) bed = float.IsNaN(bed) ? b : Mathf.Min(bed, b);
            }
            if (float.IsNaN(bed)) { pilotText = "Autopilot: Grund unbekannt"; return; }

            float target = bed - Mathf.Max(0.5f, Settings.F("AutoDepthClearance")) - keel;
            float maxD = Settings.F("AutoDepthMax");
            float minD = Settings.F("AutoDepthMin");
            if (target > maxD) target = maxD;
            if (target < minD)
            {
                target = minD;
                if (now - lastWarn > 30f)
                {
                    lastWarn = now;
                    Game.StopTimeCompression("Zu flach für den Bodenabstand! Grund bei " + bed.ToString("F0") + " m");
                }
            }
            if (Mathf.Abs(ship.TargetDepth - target) > 0.5f) SetTarget(ship, target);
            pilotText = "Autopilot: Ziel " + target.ToString("F0") + " m, Grund " + bed.ToString("F0") + " m";
        }

        // Sea floor depth (m below surface) at "ahead" metres in front of the boat. Precise loaded terrain first,
        // otherwise the game's coarse world elevation map (km grid).
        private static float Seabed(PlayerShip ship, float ahead)
        {
            var fwd = ship.transform.forward; fwd.y = 0f; if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward; fwd.Normalize();
            var pos = ship.transform.position + fwd * ahead;
            var tc = Util.Find<TerrainController>(30f).FirstOrDefault();
            if (tc != null && tc.MainGrid != null)
            {
                float h = tc.MainGrid.GetInterpolatedHeight(pos.x, pos.z, float.NaN);
                if (!float.IsNaN(h)) return -h;
            }
            var wp = Game.Wolfpack;
            if (wp == null) return float.NaN;
            var km = wp.Position + new Vector2(fwd.x, fwd.z) * (ahead / 1000f);
            float e = WorldMaps.ElevationMap.GetAtWorldSpace(km);
            return e < 0f ? -e : 0f;
        }

        private static DepthGauge instance;
        public DepthGauge() { instance = this; }

        public static void Draw()
        {
            var ship = Util.PlayerShip;
            if (ship == null || instance == null) return;
            Util.Safe("Depth.Update", () => instance.Update(ship));
            if (!Settings.B("DepthGauge")) return;

            var r = Box(); var sc = Scale(); float max = Max;
            Imgui.Backdrop(r, 0.75f);
            Imgui.TextAt(new Rect(r.x + 6, r.y + 4, r.width - 12, 20), "Tiefe " + Current(ship).ToString("F0") + " m  Ziel " + ship.TargetDepth.ToString("F0"));
            Imgui.Fill(sc, new Color(0.10f, 0.22f, 0.35f, 0.9f));
            float Y(float d) => sc.y + Mathf.Clamp01(d / max) * sc.height;

            if (!float.IsNaN(lastBed) && lastBed < max)
                Imgui.Fill(new Rect(sc.x, Y(lastBed), sc.width, sc.yMax - Y(lastBed)), new Color(0.45f, 0.32f, 0.18f, 0.95f));
            float tick = max <= 60 ? 10 : max <= 150 ? 25 : max <= 300 ? 50 : max <= 1000 ? 100 : 500;
            for (float d = 0; d <= max + 0.1f; d += tick)
            {
                Imgui.Fill(new Rect(sc.x - 6, Y(d), 6, 1), Color.gray);
                Imgui.TextAt(new Rect(r.x + 4, Y(d) - 9, 40, 18), d.ToString("F0"));
            }
            for (int i = 0; i < history.Count; i++)
            {
                float x = sc.xMax + 4 + i * ((r.xMax - sc.xMax - 8) / 120f);
                Imgui.Fill(new Rect(x, Y(history[i]) - 1, 2, 2), new Color(0.6f, 0.9f, 1f, 0.9f));
            }
            Imgui.Fill(new Rect(sc.x - 4, Y(ship.TargetDepth) - 1, sc.width + 8, 3), Color.yellow);
            Imgui.Fill(new Rect(sc.x, Y(Current(ship)) - 2, sc.width, 4), Color.white);
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 42, r.width - 12, 20), float.IsNaN(lastBed) ? "Grund: ?" : "Grund " + lastBed.ToString("F0") + " m, frei " + (lastBed - ship.BottomDepth).ToString("F0") + " m");
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 22, r.width - 12, 20), pilotText);
        }
    }
}

using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Thruster display like the Toolkit's depth gauge: two bars (bow, stern), middle = off, left = push to port, right = to
    // starboard. Header: mode (Drehen = turn on the spot, Seitwärts = whole boat sideways, Einzeln = F10 per thruster) and source.
    // Hook in ToolkitRunner OnGUI, next to DepthGauge.Draw():  UboatToolkit.Modules.Warthog.WarthogThrusterGauge.Draw();
    public static class WarthogThrusterGauge
    {
        const float W = 230f, H = 98f;
        internal static float Cmd;            // turning part -1..1 (kept for other modules)
        internal static float Bow, Stern;     // -1..1 each, + = starboard; set by WarthogHull every frame
        internal static string Mode = "";     // "Drehen", "Seitwärts", "Einzeln"
        internal static string Source = "";   // "Taste", "Ruder", "Kurs halten", "F10"

        public static void Draw()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship) || !Settings.B("WhThrustGauge") || !Settings.B("WhThrusters")) return;
            var r = new Rect(Mathf.Clamp(Screen.width * Settings.F("WhThrustGaugeX") / 100f, 0f, Screen.width - W),
                             Mathf.Clamp(Screen.height * Settings.F("WhThrustGaugeY") / 100f, 0f, Screen.height - H), W, H);
            Imgui.Backdrop(r, 0.75f);
            float yaw = ship.RigidBody != null ? ship.transform.InverseTransformDirection(ship.RigidBody.angularVelocity).y * Mathf.Rad2Deg : 0f;
            bool off = Mathf.Abs(Bow) < 0.01f && Mathf.Abs(Stern) < 0.01f;
            Imgui.TextAt(new Rect(r.x + 6, r.y + 4, r.width - 12, 20), "Strahlruder  " + (off ? "aus" : Mode + (Source != "" ? " (" + Source + ")" : "")));
            DrawBar(new Rect(r.x + 50, r.y + 28, r.width - 80, 12), "Bug", Bow, r.x);
            DrawBar(new Rect(r.x + 50, r.y + 50, r.width - 80, 12), "Heck", Stern, r.x);
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 24, r.width - 12, 20), "Drehung " + yaw.ToString("+0.0;-0.0;0.0") + " °/s");
        }

        private static void DrawBar(Rect bar, string label, float v, float x0)
        {
            Imgui.TextAt(new Rect(x0 + 6, bar.y - 3, 44, 18), label);
            Imgui.Fill(bar, new Color(0.10f, 0.22f, 0.35f, 0.9f));
            float mid = bar.x + bar.width / 2f;
            float len = Mathf.Clamp(v, -1f, 1f) * bar.width / 2f;
            var col = Mathf.Abs(v) > 0.6f ? new Color(1f, 0.75f, 0.2f, 0.95f) : new Color(0.6f, 0.9f, 1f, 0.95f);
            if (len < 0) Imgui.Fill(new Rect(mid + len, bar.y, -len, bar.height), col); else if (len > 0) Imgui.Fill(new Rect(mid, bar.y, len, bar.height), col);
            Imgui.Fill(new Rect(mid - 1, bar.y - 2, 2, bar.height + 4), Color.white);
            Imgui.TextAt(new Rect(bar.xMax + 4, bar.y - 3, 26, 18), v > 0.01f ? "Stb" : v < -0.01f ? "Bb" : "");
        }
    }
}

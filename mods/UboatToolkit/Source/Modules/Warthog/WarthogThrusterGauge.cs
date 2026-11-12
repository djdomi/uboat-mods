using UnityEngine;

namespace UboatToolkit.Modules.Warthog
{
    // Thruster display like the Toolkit's depth gauge: one bar, middle = off, left = bow to port (Z), right = to starboard (X).
    // Shows what the thrusters push right now (keys, rudder help or heading hold), the boat's turn rate and the power draw.
    // Hook in ToolkitRunner OnGUI, next to DepthGauge.Draw():  UboatToolkit.Modules.Warthog.WarthogThrusterGauge.Draw();
    public static class WarthogThrusterGauge
    {
        const float W = 230f, H = 78f;
        internal static float Cmd;          // -1..1, set by WarthogHull every frame
        internal static string Source = ""; // "Taste", "Ruder", "Kurs halten"

        public static void Draw()
        {
            var ship = Util.PlayerShip;
            if (ship == null || !WarthogSettings.IsWarthog(ship) || !Settings.B("WhThrustGauge") || !Settings.B("WhThrusters")) return;
            var r = new Rect(Mathf.Clamp(Screen.width * Settings.F("WhThrustGaugeX") / 100f, 0f, Screen.width - W),
                             Mathf.Clamp(Screen.height * Settings.F("WhThrustGaugeY") / 100f, 0f, Screen.height - H), W, H);
            Imgui.Backdrop(r, 0.75f);
            float yaw = ship.RigidBody != null ? ship.transform.InverseTransformDirection(ship.RigidBody.angularVelocity).y * Mathf.Rad2Deg : 0f;
            Imgui.TextAt(new Rect(r.x + 6, r.y + 4, r.width - 12, 20), "Strahlruder  " + (Mathf.Abs(Cmd) < 0.01f ? "aus" : Mathf.RoundToInt(Mathf.Abs(Cmd) * 100f) + " % " + (Cmd < 0 ? "Bb" : "Stb") + (Source != "" ? " (" + Source + ")" : "")));
            // bar
            var bar = new Rect(r.x + 26, r.y + 28, r.width - 52, 14);
            Imgui.Fill(bar, new Color(0.10f, 0.22f, 0.35f, 0.9f));
            float mid = bar.x + bar.width / 2f;
            float len = Mathf.Clamp(Cmd, -1f, 1f) * bar.width / 2f;
            var col = Mathf.Abs(Cmd) > 0.6f ? new Color(1f, 0.75f, 0.2f, 0.95f) : new Color(0.6f, 0.9f, 1f, 0.95f);
            if (len < 0) Imgui.Fill(new Rect(mid + len, bar.y, -len, bar.height), col); else if (len > 0) Imgui.Fill(new Rect(mid, bar.y, len, bar.height), col);
            Imgui.Fill(new Rect(mid - 1, bar.y - 3, 2, bar.height + 6), Color.white);
            Imgui.TextAt(new Rect(r.x + 6, bar.y - 2, 20, 18), "Z");
            Imgui.TextAt(new Rect(bar.xMax + 6, bar.y - 2, 20, 18), "X");
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 24, r.width - 12, 20), "Drehung " + yaw.ToString("+0.0;-0.0;0.0") + " °/s");
        }
    }
}

using System.Collections.Generic;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // DoMe 2026-11-18: "ein Dial ... mit einem realen Sonar + Radar, so wie man es kennt, mit diesem laufenden Zeiger".
    // Round scope, bow up. A sweep line runs around; a contact lights up when the line passes it and fades after.
    // Shown is ONLY what our own boat really has as a contact right now (the game's own observation list of the
    // player boat): sonar/ping = green dot at its distance, radar = amber dot, hydrophone only = green mark on the
    // outer ring (bearing known, distance not). Nothing is read from enemies' sensors and nothing is changed in the game.
    // The window can be dragged with the mouse; the place is stored.
    public static class SonarScope
    {
        private struct Blip { public float Bearing, Frac; public int Kind; }   // Kind 0 sonar/ping, 1 radar, 2 hydrophone (bearing only)
        private static readonly List<Blip> blips = new List<Blip>();
        private static float nextScan, nextLog;
        private static bool dragging; private static Vector2 grab;
        private static int sonarN, radarN, hydroN;

        private static void Scan(PlayerShip ship, float rangeM)
        {
            blips.Clear(); sonarN = radarN = hydroN = 0;
            float head = ship.transform.eulerAngles.y;
            var list = ship.GetObservationsDirect();
            if (list == null) return;
            bool ping = ActiveSonar.Active && Settings.B("ActiveSonar");
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                if (o == null || !o.IsValid || o.Entity == null) continue;
                Vector3 to = o.Entity.transform.position - ship.transform.position;
                float d = new Vector2(to.x, to.z).magnitude;
                float rel = Mathf.DeltaAngle(head, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                bool sonar = o.IsObservedVia(DirectObservationType.Sonar) || (ping && d <= Settings.F("SonarRange") && ActiveSonar.InArc(ship, o.Entity));
                bool radar = o.IsObservedVia(DirectObservationType.Radar);
                bool hydro = o.IsObservedVia(DirectObservationType.Hydrophone);
                if (!sonar && !radar && !hydro) continue;      // seen by eye only: not a scope contact
                int kind = sonar ? 0 : radar ? 1 : 2;
                if (kind == 0) sonarN++; else if (kind == 1) radarN++; else hydroN++;
                blips.Add(new Blip { Bearing = rel, Frac = kind == 2 ? 1f : Mathf.Clamp01(d / rangeM), Kind = kind });
            }
        }

        public static void Draw()
        {
            if (!Settings.B("SonarScope")) return;
            var ship = Util.PlayerShip;
            if (ship == null) return;
            float size = Mathf.Clamp(Settings.F("SonarScopeSize"), 120f, 600f);
            float w = size + 16f, h = size + 44f;
            var r = new Rect(Mathf.Clamp(Screen.width * Settings.F("SonarScopeX") / 100f, 0f, Screen.width - w),
                             Mathf.Clamp(Screen.height * Settings.F("SonarScopeY") / 100f, 0f, Screen.height - h), w, h);

            // drag with the left mouse button
            var mouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (Input.GetMouseButtonDown(0) && r.Contains(mouse)) { dragging = true; grab = mouse - new Vector2(r.x, r.y); }
            if (dragging)
            {
                if (Input.GetMouseButton(0))
                {
                    r.x = Mathf.Clamp(mouse.x - grab.x, 0f, Screen.width - w); r.y = Mathf.Clamp(mouse.y - grab.y, 0f, Screen.height - h);
                    Settings.Set("SonarScopeX", r.x * 100f / Screen.width); Settings.Set("SonarScopeY", r.y * 100f / Screen.height);
                }
                else { dragging = false; Settings.Save(); Util.Log("Sonar-Anzeige verschoben nach " + Settings.F("SonarScopeX").ToString("0") + " % / " + Settings.F("SonarScopeY").ToString("0") + " %"); }
            }
            if (Imgui.EventKind() != 7) return;                 // draw only in the repaint pass

            float rangeM = Mathf.Max(500f, Settings.F("SonarScopeKm") * 1000f);
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 0.5f; Util.Safe("SonarScope.Scan", () => Scan(ship, rangeM)); }
            if (Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + 60f;
                Util.Log("Sonar-Anzeige (nur eigene Kontakte): Sonar/Ping " + sonarN + ", Radar " + radarN + ", nur Horcher " + hydroN + ", Bereich " + (rangeM / 1000f).ToString("0.#") + " km");
            }

            Imgui.Backdrop(r, 0.8f);
            float rad = size / 2f;
            var c = new Vector2(r.x + w / 2f, r.y + 24f + rad);
            var green = new Color(0.35f, 1f, 0.45f, 1f);
            Imgui.TextAt(new Rect(r.x + 6, r.y + 3, w - 12, 20), "Sonar/Radar  " + (rangeM / 1000f).ToString("0.#") + " km  Bug oben");

            // range rings (dots) and cross
            for (int ring = 1; ring <= 3; ring++)
            {
                float rr = rad * ring / 3f; int n = 24 * ring;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    Imgui.Fill(new Rect(c.x + Mathf.Sin(a) * rr - 1f, c.y - Mathf.Cos(a) * rr - 1f, 2f, 2f), new Color(0.2f, 0.6f, 0.3f, 0.55f));
                }
            }
            Imgui.Fill(new Rect(c.x - rad, c.y, rad * 2f, 1f), new Color(0.2f, 0.6f, 0.3f, 0.3f));
            Imgui.Fill(new Rect(c.x, c.y - rad, 1f, rad * 2f), new Color(0.2f, 0.6f, 0.3f, 0.3f));
            Imgui.Fill(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), Color.white);                       // own boat

            // sweep line with a short fading tail
            float period = Mathf.Clamp(Settings.F("SonarScopeSweep"), 1f, 30f);
            float sweep = Time.unscaledTime * 360f / period % 360f;
            for (int tail = 0; tail < 6; tail++)
            {
                float a = (sweep - tail * 2.5f) * Mathf.Deg2Rad, alpha = 0.9f - tail * 0.15f;
                int steps = Mathf.RoundToInt(rad / 4f);
                for (int s = 1; s <= steps; s++)
                {
                    float rr = rad * s / steps;
                    Imgui.Fill(new Rect(c.x + Mathf.Sin(a) * rr - 1f, c.y - Mathf.Cos(a) * rr - 1f, 2f, 2f), new Color(green.r, green.g, green.b, alpha));
                }
            }

            // contacts: brightest right after the line has passed, then fading until it comes round again
            for (int i = 0; i < blips.Count; i++)
            {
                var b = blips[i];
                float since = Mathf.Repeat(sweep - b.Bearing, 360f) / 360f;      // 0 = just swept, 1 = a full turn ago
                float alpha = Mathf.Lerp(1f, 0.15f, since);
                float a = b.Bearing * Mathf.Deg2Rad, rr = rad * b.Frac;
                var col = b.Kind == 1 ? new Color(1f, 0.75f, 0.2f, alpha) : new Color(green.r, green.g, green.b, alpha);
                float sz = b.Kind == 2 ? 4f : 6f;
                Imgui.Fill(new Rect(c.x + Mathf.Sin(a) * rr - sz / 2f, c.y - Mathf.Cos(a) * rr - sz / 2f, sz, sz), col);
            }
            Imgui.TextAt(new Rect(r.x + 6, r.yMax - 20, w - 12, 20), "grün Sonar " + sonarN + "   gelb Radar " + radarN + "   Rand Horcher " + hydroN);
        }
    }
}

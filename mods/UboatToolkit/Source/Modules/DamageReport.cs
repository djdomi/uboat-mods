using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UBOAT.Game.Scene.Utilities;
using UBOAT.Game.UI;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // "What hit me": every damage to your boat is explained - what it was (shell, torpedo, depth charge,
    // bomb, mine, fire, collision, water pressure), who caused it (ship / aircraft, distance, bearing),
    // where it hit (compartment), how strong (hull and crew damage) and the boat's state at that moment
    // (depth, surfaced, speed). Shown as a game notification, kept in the F10 tab "Schaden" and written to
    // UboatToolkit/schadensbericht.txt. Repeats of the same cause within a few seconds are merged.
    public class DamageReport : ModuleBase
    {
        internal class Entry { public float Time; public string Clock; public string Text; public string Key; public int Count = 1; public float Hull, Crew; }
        internal static readonly List<Entry> History = new List<Entry>();
        private static bool subscribed;

        public override void Tick()
        {
            if (subscribed) return;
            DamageUtility.DamageApplied += OnDamage;
            subscribed = true;
        }

        private static void OnDamage(DamageEvent e)
        {
            try
            {
                if (!Settings.B("DamageReport") || e == null) return;
                var ship = Util.PlayerShip;
                if (ship == null || e.DamagedEntities == null || Array.IndexOf(e.DamagedEntities, ship) < 0) return;
                if (e.Damage <= 0f && e.CrewDamage <= 0f) return;

                string what = Cause(e);
                string who = Attacker(e, ship);
                string where = Where(ship, e.Position);
                string key = what + "|" + who + "|" + where;
                var last = History.Count > 0 ? History[History.Count - 1] : null;
                if (last != null && last.Key == key && Time.unscaledTime - last.Time < Mathf.Max(1f, Settings.F("DamageReportMerge")))
                {
                    last.Count++; last.Hull += e.Damage; last.Crew += e.CrewDamage; last.Time = Time.unscaledTime;
                    return;
                }
                string state = "Boot: " + (ship.Submerged ? Mathf.RoundToInt(ship.Depth) + " m tief" : "aufgetaucht") +
                               ", " + (ship.AverageVelocity * 1.943844f).ToString("0.0") + " kn";
                var en = new Entry
                {
                    Time = Time.unscaledTime, Clock = DateTime.Now.ToString("HH:mm:ss"), Key = key, Hull = e.Damage, Crew = e.CrewDamage,
                    Text = what + (who.Length > 0 ? " von " + who : "") + " - Treffer: " + where + ". " + Why(e, ship) + " " + state + "."
                };
                History.Add(en);
                if (e.AppliedBy != null && e.AppliedBy != ship && e.DamageType != DamageType.Fire && e.DamageType != DamageType.WaterPressure)
                    UnderFire.Report(e.AppliedBy, "Treffer: " + what);
                if (Settings.B("DamageReportPause"))
                {
                    var gt = UnityEngine.Object.FindObjectOfType<UBOAT.Game.Core.Time.GameTime>();
                    if (gt != null && !gt.Paused && gt.IsPauseAllowed) gt.Paused = true;
                }
                if (History.Count > 200) History.RemoveAt(0);
                Write(en);
                if (Settings.B("DamageReportNotify"))
                {
                    var bar = UnityEngine.Object.FindObjectOfType<NotificationBarUI>();
                    string shortText = what + (who.Length > 0 ? " von " + who : "") + " - " + where;
                    if (bar != null) bar.OpenNow("Icons/Notification Bar/Notification - 40 - Danger", shortText);
                    else UboatToolkit.ToolkitRunner.ShowNotice(shortText, 6f);
                }
            }
            catch (Exception ex) { Util.Log("Schadensbericht: " + ex.Message); }
        }

        private static string Cause(DamageEvent e)
        {
            string src = e.DamageSource != null ? Clean(e.DamageSource.name).ToLowerInvariant() : "";
            switch (e.DamageType)
            {
                case DamageType.Projectile: return src.Contains("hedgehog") ? "Wasserbomben-Werfer (Igel)" : (e.AppliedBy is Aircraft ? "Bordwaffen-Beschuss eines Flugzeugs" : "Granate");
                case DamageType.Torpedo: return "Torpedo";
                case DamageType.Mine: return "Mine";
                case DamageType.Explosives: return src.Contains("depth") || src.Contains("charge") ? "Wasserbombe" : "Sprengladung";
                case DamageType.Bomb: return e.AppliedBy is Aircraft ? "Fliegerbombe / Wasserbombe vom Flugzeug" : "Bombe";
                case DamageType.Fire: return "Feuer an Bord";
                case DamageType.Collision: return "Zusammenstoß";
                case DamageType.WaterPressure: return "Wasserdruck";
                default: return "Ereignis aus der Mission";
            }
        }

        private static string Why(DamageEvent e, PlayerShip ship)
        {
            switch (e.DamageType)
            {
                case DamageType.WaterPressure: return "Grund: zu tief für den Rumpf (" + Mathf.RoundToInt(ship.Depth) + " m).";
                case DamageType.Collision: return "Grund: Berührung mit " + (e.AppliedBy != null && e.AppliedBy != ship ? Clean(e.AppliedBy.name) : "Grund/Hindernis") + ".";
                case DamageType.Fire: return "Grund: Brand in diesem Raum.";
                case DamageType.Mine: return "Grund: in ein Minenfeld gefahren.";
                default:
                    if (e.AppliedBy == null || e.AppliedBy == ship) return "";
                    float d = Vector3.Distance(e.AppliedBy.transform.position, e.Position);
                    return "Detonation " + d.ToString("0") + " m vom Schützen entfernt, Schaden Rumpf " + e.Damage.ToString("0") +
                           (e.CrewDamage > 0f ? ", Crew " + e.CrewDamage.ToString("0") : "") + ".";
            }
        }

        private static string Attacker(DamageEvent e, PlayerShip ship)
        {
            var a = e.AppliedBy;
            if (a == null || a == ship) return "";
            Vector3 to = a.transform.position - ship.transform.position;
            float km = new Vector2(to.x, to.z).magnitude / 1000f;
            float abs = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f;
            float rel = (abs - ship.transform.eulerAngles.y + 360f) % 360f;
            string kind = a is Aircraft ? "Flugzeug" : (a is Ship ? "Schiff" : "Gegner");
            return kind + " \"" + Clean(a.name) + "\" (" + km.ToString("0.0") + " km, Peilung " + Mathf.RoundToInt(abs).ToString("000") +
                   "°, " + Side(rel) + ")";
        }

        private static string Side(float rel)
        {
            if (rel < 22.5f || rel >= 337.5f) return "voraus";
            if (rel < 67.5f) return "Steuerbord voraus";
            if (rel < 112.5f) return "Steuerbord querab";
            if (rel < 157.5f) return "Steuerbord achtern";
            if (rel < 202.5f) return "achteraus";
            if (rel < 247.5f) return "Backbord achtern";
            if (rel < 292.5f) return "Backbord querab";
            return "Backbord voraus";
        }

        private static string Where(PlayerShip ship, Vector3 pos)
        {
            Compartment best = null; float bestD = float.MaxValue;
            foreach (var c in Util.Find<Compartment>())
            {
                if (c == null) continue;
                if (c.Contains(pos)) { best = c; break; }
                float d = (c.transform.position - pos).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            Vector3 local = ship.transform.InverseTransformPoint(pos);
            string part = local.z > 15f ? "Bug" : (local.z < -15f ? "Heck" : "Mitte");
            string side = local.x > 1f ? "Steuerbord" : (local.x < -1f ? "Backbord" : "mittig");
            string height = local.y > 2f ? ", über Wasser/Turm" : "";
            return (best != null ? German(Clean(best.name)) + " (" : "") + part + ", " + side + height + (best != null ? ")" : "");
        }

        private static readonly string[][] Words =
        {
            new[] { "Bow Torpedo Room", "Bug-Torpedoraum" }, new[] { "Stern Torpedo Room", "Heck-Torpedoraum" },
            new[] { "Torpedo Room", "Torpedoraum" }, new[] { "Control Room", "Zentrale" }, new[] { "Control", "Zentrale" },
            new[] { "Conning Tower", "Turm" }, new[] { "Diesel Engine", "Dieselraum" }, new[] { "Diesel", "Dieselraum" },
            new[] { "Electric Engine", "E-Maschinenraum" }, new[] { "Electric", "E-Maschinenraum" }, new[] { "Engine", "Maschinenraum" },
            new[] { "Galley", "Kombüse" }, new[] { "Officers", "Offiziersraum" }, new[] { "Petty Officers", "Unteroffiziersraum" },
            new[] { "Radio", "Funkraum" }, new[] { "Sonar", "Horchraum" }, new[] { "Captain", "Kommandantenraum" },
            new[] { "Quarters", "Quartier" }, new[] { "Deck", "Deck" }, new[] { "Exterior", "außen" }
        };

        private static string German(string n)
        {
            foreach (var w in Words) if (n.IndexOf(w[0], StringComparison.OrdinalIgnoreCase) >= 0) return w[1];
            return n;
        }

        private static string Clean(string n) => string.IsNullOrEmpty(n) ? "?" : n.Replace("(Clone)", "").Trim();

        private static void Write(Entry en)
        {
            try
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(Path.Combine(Settings.Folder, "schadensbericht.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd ") + en.Clock + "  " + en.Text + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        // F10 tab "Schaden": newest first.
        public static void Draw()
        {
            Imgui.Label("Schadensbericht: was hat uns getroffen, wo und warum (neueste oben). Datei: UboatToolkit/schadensbericht.txt");
            if (Imgui.Button("Liste leeren", 140)) History.Clear();
            Imgui.Space(4);
            UnderFire.DrawList();
            Imgui.Space(6);
            if (History.Count == 0) { Imgui.Label("Noch kein Schaden in dieser Sitzung."); return; }
            for (int i = History.Count - 1; i >= 0 && i >= History.Count - 50; i--)
            {
                var h = History[i];
                Imgui.Label(h.Clock + "  " + h.Text + (h.Count > 1 ? "  (x" + h.Count + ", zusammen Rumpf " + h.Hull.ToString("0") + ", Crew " + h.Crew.ToString("0") + ")" : ""), 660);
            }
        }
    }
}

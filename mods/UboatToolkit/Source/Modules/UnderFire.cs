using System;
using System.Collections.Generic;
using HarmonyLib;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // "Herr Kaleun, was tun wir?": the lookouts notice that we are being shot at (enemy gun aims at us,
    // or damage from an enemy). The game pauses and asks what to do, with a weighed recommendation:
    // aircraft -> crash dive if the water is deep enough, else AA fire back; merchant -> fire back with
    // the deck gun; warship -> crash dive if deep enough, else evade; already submerged -> evade.
    // Modes: 0 off, 1 ask (default), 2 do the recommendation automatically.
    // Ask without pause: only an icon on the right edge. "Ignore x minutes": messages are only collected.
    public static class UnderFire
    {
        internal enum Act { Dive, Evade, FireBack, Nothing }
        internal class Alert { public Entity Attacker; public string Text; public Act Advice; public string Why; public string Clock; public int Count = 1; }

        internal static readonly List<Alert> Collected = new List<Alert>();
        private static readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        private static Alert current;          // waiting for an answer
        private static bool windowOpen, pausedByUs;
        private static float ignoreUntil;
        private static int ignoredCount;
        private static GameTime gameTime;
        internal static float ManGunsUntil;

        private static readonly string[] ActName = { "Alarmtauchen", "Ausweichen (Heck zum Gegner)", "Zurückschießen", "Nichts tun" };

        // Called by the gun patch and by the damage report.
        internal static void Report(Entity attacker, string what)
        {
            try
            {
                int mode = Mathf.RoundToInt(Settings.F("UnderFireMode"));
                var ship = Util.PlayerShip;
                if (mode <= 0 || ship == null || attacker == null || attacker == ship) return;
                if (Settings.B("UnderFireNeedSighting") && ship.GetObservation(attacker) == null && !(attacker is Aircraft)) return;
                int id = attacker.GetInstanceID();
                float now = Time.unscaledTime;
                if (lastSeen.TryGetValue(id, out float t) && now - t < Mathf.Max(5f, Settings.F("UnderFireCooldown")))
                {
                    if (current != null && current.Attacker == attacker) current.Count++;
                    return;
                }
                lastSeen[id] = now;

                var a = new Alert { Attacker = attacker, Clock = DateTime.Now.ToString("HH:mm:ss") };
                a.Advice = Advise(ship, attacker, out a.Why);
                Vector3 to = attacker.transform.position - ship.transform.position;
                float km = new Vector2(to.x, to.z).magnitude / 1000f;
                float brg = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg + 360f) % 360f;
                a.Text = what + ": " + (attacker is Aircraft ? "Flugzeug" : "Schiff") + " \"" + attacker.name.Replace("(Clone)", "").Trim() +
                         "\", " + km.ToString("0.0") + " km, Peilung " + Mathf.RoundToInt(brg).ToString("000") + "°";
                Collected.Add(a);
                if (Collected.Count > 100) Collected.RemoveAt(0);
                Util.Log("Beschuss: " + a.Text + " | Vorschlag: " + ActName[(int)a.Advice]);

                if (now < ignoreUntil) { ignoredCount++; return; }   // ignored: only collect
                if (mode >= 2) { Do(a.Advice, a); return; }
                if (current != null && windowOpen) { current = a; return; }  // window already open: show the newest
                current = a;
                if (Settings.B("UnderFirePause")) Open(true);
            }
            catch (Exception ex) { Util.Log("Beschuss: " + ex.Message); }
        }

        private static Act Advise(PlayerShip ship, Entity at, out string why)
        {
            float floor = SeaFloor.SeabedDepthUnderShip();
            bool deep = float.IsNaN(floor) || floor > 60f;
            string depthTxt = float.IsNaN(floor) ? "Wassertiefe unbekannt" : "Wassertiefe " + Mathf.RoundToInt(floor) + " m";
            if (ship.SubmergedOrGoingToSubmerge) { why = "Wir sind schon unter Wasser - wegdrehen und schnell weg."; return Act.Evade; }
            if (at is Aircraft)
            {
                if (deep) { why = "Flugzeug - Tauchen ist am sichersten (" + depthTxt + ")."; return Act.Dive; }
                why = "Flugzeug, aber zu flach zum Tauchen (" + depthTxt + ") - Flak schießt zurück."; return Act.FireBack;
            }
            string cat = (at.Category ?? "").ToLowerInvariant();
            bool merchant = cat.Contains("freighter") || cat.Contains("tanker") || cat.Contains("coaster") || cat.Contains("fishing");
            if (merchant && HasDeckGun()) { why = "Handelsschiff - unser Deckgeschütz ist stärker, zurückschießen."; return Act.FireBack; }
            if (deep) { why = "Kriegsschiff - tauchen (" + depthTxt + ")."; return Act.Dive; }
            why = "Kriegsschiff und zu flach zum Tauchen (" + depthTxt + ") - Heck zum Gegner und volle Fahrt."; return Act.Evade;
        }

        private static bool HasDeckGun()
        {
            foreach (var g in Util.Find<Gun>()) if (g != null && g.isActiveAndEnabled) return true;
            return false;
        }

        internal static void Do(Act act, Alert a)
        {
            var ship = Util.PlayerShip;
            if (ship == null) return;
            try
            {
                switch (act)
                {
                    case Act.Dive:
                        ship.StartAlarm("Crash Diving", true);
                        ship.FollowDiveSchedule = false;
                        CourseCfg.SetDepthPreset?.Invoke(ship, new object[] { DepthPreset.MaxSafeDepth, false });
                        ship.OrderEngineGear(5, false);
                        break;
                    case Act.Evade:
                        if (a?.Attacker != null && CourseCfg.SetTargetCourse != null)
                        {
                            var route = ship.MobileSandboxGroup.Route;
                            Vector2 dir = route.Count > 0 ? (route[0] - ship.MobileSandboxGroup.Position).normalized : route.TargetDirection;
                            float baseAng = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                            Vector3 away = ship.transform.position - a.Attacker.transform.position;
                            float want = Mathf.Atan2(away.z, away.x) * Mathf.Rad2Deg;
                            CourseCfg.SetTargetCourse.Invoke(ship, new object[] { Mathf.DeltaAngle(want, baseAng) });
                        }
                        ship.StartAlarm("Alarm", true);
                        ship.OrderEngineGear(5, false);
                        break;
                    case Act.FireBack:
                        ship.StartAlarm("Alarm", true);
                        ManGunsUntil = Time.unscaledTime + 120f;
                        if (a?.Attacker != null)
                            foreach (var g in Util.Find<Gun>()) if (g != null) Util.Safe("Beschuss.Ziel", () => g.TargetOverride = a.Attacker);
                        break;
                }
                UboatToolkit.ToolkitRunner.ShowNotice("Befehl: " + ActName[(int)act], 5f);
            }
            catch (Exception ex) { Util.Log("Beschuss-Befehl: " + ex.Message); }
        }

        private static void Open(bool pause)
        {
            windowOpen = true;
            if (pause)
            {
                if (gameTime == null) gameTime = UnityEngine.Object.FindObjectOfType<GameTime>();
                if (gameTime != null && !gameTime.Paused && gameTime.IsPauseAllowed) { gameTime.Paused = true; pausedByUs = true; }
            }
        }

        private static void Close()
        {
            windowOpen = false; current = null;
            if (pausedByUs && gameTime != null) gameTime.Paused = false;
            pausedByUs = false;
        }

        // Drawn every frame from the toolkit GUI (also when F10 is closed).
        public static void Draw()
        {
            if (Mathf.RoundToInt(Settings.F("UnderFireMode")) != 1) { if (windowOpen) Close(); return; }
            float now = Time.unscaledTime;
            if (windowOpen && current != null)
            {
                Cursor.visible = true; Cursor.lockState = CursorLockMode.None;
                var r = new Rect(Screen.width / 2f - 260, Screen.height / 2f - 150, 520, 300);
                Imgui.Box(r, "Herr Kaleun, was tun wir?");
                Imgui.BeginArea(new Rect(r.x + 12, r.y + 28, r.width - 24, r.height - 36));
                Imgui.Label(current.Clock + "  " + current.Text + (current.Count > 1 ? "  (x" + current.Count + ")" : ""), 490);
                Imgui.Label("Vorschlag: " + ActName[(int)current.Advice] + ". " + current.Why, 490);
                Imgui.Space(6);
                for (int i = 0; i < ActName.Length; i++)
                    if (Imgui.Button((i == (int)current.Advice ? "★ " : "") + ActName[i], 490)) { var a = current; Close(); if (i != (int)Act.Nothing) Do((Act)i, a); Imgui.EndArea(); return; }
                int min = Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin")));
                if (Imgui.Button(min + " Minuten ignorieren (Meldungen werden gesammelt)", 490)) { ignoreUntil = now + min * 60f; ignoredCount = 0; Close(); }
                Imgui.EndArea();
                return;
            }
            // Icon on the right edge: open question (no pause) or ignore time with collected messages.
            string label = null;
            if (now < ignoreUntil)
            {
                label = "⚠ ignoriert noch " + Mathf.CeilToInt((ignoreUntil - now) / 60f) + " min\n" + ignoredCount + " Meldungen gesammelt";
            }
            else if (current != null) label = "⚠ Beschuss!\nVorschlag: " + ActName[(int)current.Advice];
            if (label == null) return;
            Imgui.BeginArea(new Rect(Screen.width - 230, Screen.height / 2f - 30, 220, 60));
            if (Imgui.Button(label, 215))
            {
                if (now < ignoreUntil) { ignoreUntil = 0f; ignoredCount = 0; if (Collected.Count > 0) current = Collected[Collected.Count - 1]; }
                if (current != null) Open(false);
            }
            Imgui.EndArea();
        }

        // F10 tab "Schaden": list of collected under-fire messages.
        public static void DrawList()
        {
            Imgui.Label("Beschuss-Meldungen (neueste oben):");
            if (Collected.Count == 0) { Imgui.Label("Keine."); return; }
            for (int i = Collected.Count - 1; i >= 0 && i >= Collected.Count - 30; i--)
            {
                var c = Collected[i];
                Imgui.Label(c.Clock + "  " + c.Text + " - Vorschlag: " + ActName[(int)c.Advice], 660);
            }
        }
    }

    // Lookouts: an enemy gun fires at our boat.
    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class UnderFire_GunPatch
    {
        private static void Postfix(Gun __instance, Entity target)
        {
            if (target == null || !(target is PlayerShip) || Util.IsPlayers(__instance)) return;
            var owner = __instance.GetComponentInParent<Entity>();
            if (owner != null) UnderFire.Report(owner, "Wir werden beschossen");
        }
    }
}

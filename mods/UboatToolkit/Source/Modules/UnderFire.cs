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
                // More than 2 messages within 30 s: no pause (only the box at the edge).
                float t = Time.unscaledTime;
                recent.Add(t); recent.RemoveAll(x => t - x > 30f);
                Open(Settings.B("UnderFirePause") && recent.Count <= 2);
            }
            catch (Exception ex) { Util.Log("Beschuss: " + ex.Message); }
        }

        private static Act Advise(PlayerShip ship, Entity at, out string why)
        {
            float floor = SeaFloor.SeabedDepthUnderShip();
            bool deep = float.IsNaN(floor) || floor > 60f;
            float minFloor = float.IsNaN(floor) ? SeaFloor.MapDepthUnderShip() : float.NaN;
            string depthTxt = (!float.IsNaN(floor) ? "Wassertiefe " + Mathf.RoundToInt(floor) + " m"
                              : !float.IsNaN(minFloor) ? "Wassertiefe ca. " + Mathf.RoundToInt(minFloor) + " m"
                              : "Wassertiefe unbekannt") + (ship.SubmergedOrGoingToSubmerge ? ", wir auf " + Mathf.RoundToInt(Mathf.Max(0f, ship.Depth)) + " m" : ", wir sind aufgetaucht");
            if (float.IsNaN(floor) && !float.IsNaN(minFloor)) deep = minFloor > 60f;
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

        private static readonly System.Collections.Generic.List<float> recent = new System.Collections.Generic.List<float>();
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
        // Same choices as before, but as a box at the right edge (not in the middle); click or key Alt+1..4, Alt+5 = ignore.
        private static readonly string[] ActKeys = { "KeyUnderFire1", "KeyUnderFire2", "KeyUnderFire3", "KeyUnderFire4" };
        public static void Draw()
        {
            if (Mathf.RoundToInt(Settings.F("UnderFireMode")) != 1) { if (windowOpen) Close(); return; }
            float now = Time.unscaledTime;
            if (current != null && now >= ignoreUntil)
            {
                for (int i = 0; i < ActKeys.Length; i++)
                    if (Keys.Down(ActKeys[i])) { var a = current; Close(); current = null; if (i != (int)Act.Nothing) Do((Act)i, a); return; }
                if (Keys.Down("KeyUnderFireIgnore"))
                {
                    int min = Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin")));
                    ignoreUntil = now + min * 60f; ignoredCount = 0; Close(); current = null;
                    UboatToolkit.ToolkitRunner.ShowNotice("Beschuss-Meldungen " + min + " min ignoriert", 3f);
                    return;
                }
            }
            string label = null;
            if (now < ignoreUntil)
                label = "⚠ Beschuss ignoriert noch " + Mathf.CeilToInt((ignoreUntil - now) / 60f) + " min, " + ignoredCount + " Meldungen gesammelt";
            if (label != null)
            {
                Imgui.BeginArea(new Rect(Screen.width - 330, Screen.height / 2f - 30, 320, 60));
                if (Imgui.Button(label, 315)) { ignoreUntil = 0f; ignoredCount = 0; if (Collected.Count > 0) current = Collected[Collected.Count - 1]; }
                Imgui.EndArea();
                return;
            }
            if (current == null) return;
            // Small box at the right edge (game keeps running): click or key.
            Imgui.BeginArea(new Rect(Screen.width - 330, Screen.height / 2f - 110, 320, 230));
            Imgui.Label("⚠ " + current.Clock + " " + current.Text + (current.Count > 1 ? " (x" + current.Count + ")" : ""), 315);
            for (int i = 0; i < ActName.Length; i++)
                if (Imgui.Button("[" + KeyText(ActKeys[i]) + "] " + ActName[i] + (i == (int)current.Advice ? " ★" : ""), 315))
                { var a = current; Close(); current = null; if (i != (int)Act.Nothing) Do((Act)i, a); Imgui.EndArea(); return; }
            if (Imgui.Button("[" + KeyText("KeyUnderFireIgnore") + "] " + Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin"))) + " min ignorieren", 315))
            { ignoreUntil = now + Mathf.Max(1, Mathf.RoundToInt(Settings.F("UnderFireIgnoreMin"))) * 60f; ignoredCount = 0; Close(); current = null; }
            Imgui.EndArea();
        }
        private static string KeyText(string k) { var o = Settings.Get(k); return o == null || string.IsNullOrEmpty(o.Text) ? "-" : o.Text; }

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

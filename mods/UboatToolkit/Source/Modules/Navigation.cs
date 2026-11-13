using HarmonyLib;
using UBOAT.Game.Core.Time;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // QoL #32: anti-grounding. The game measures the distance to the coast. If the boat gets
    // closer to land fast enough that it would reach it within N real seconds, time compression
    // is switched off (only when the boat is really heading towards land, not along the coast).
    // Cheat #33: speed multiplier (propeller force and expected speed per gear).
    public class Navigation : ModuleBase
    {
        private SandboxPlayerWolfpack wolfpack;
        private TimeCompressionController tcc;
        private GameTime gameTime;
        private float lastD = -1f, lastT, rate, nextCheck, quietUntil, limitedAt;
        private bool limited;
        private static readonly object BlockKey = new object();

        public override void Frame()
        {
            if (!Settings.B("AntiGrounding") && limited && tcc != null) { Util.Safe("Nav.Unlimit", () => tcc.RemoveTimeCompressionBlockade(BlockKey)); limited = false; }
            if (!Settings.B("AntiGrounding") || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            if (wolfpack == null) wolfpack = Traverse.Create(typeof(UBOAT.Game.UI.Notifications.ShallowAreaNotification)).Field("wolfpack").GetValue<SandboxPlayerWolfpack>();
            if (tcc == null) tcc = Traverse.Create(typeof(UBOAT.Game.Scene.Tasks.DisallowTimeCompressionDuringAircraftAttack)).Field("timeCompressionController").GetValue<TimeCompressionController>();
            if (gameTime == null) gameTime = Object.FindObjectOfType<GameTime>();
            if (wolfpack == null || tcc == null || gameTime == null) return;

            float d = wolfpack.LandDistance, now = Time.unscaledTime;
            if (lastD < 0f) { lastD = d; lastT = now; return; }
            if (d != lastD)
            {
                float dt = Mathf.Max(0.05f, now - lastT);
                float r = (lastD - d) / dt;                 // closing speed in map units per real second
                rate = rate <= 0f ? r : Mathf.Lerp(rate, r, 0.5f);
                lastD = d; lastT = now;
            }
            else if (now - lastT > 5f) rate = 0f;            // nothing changes: not closing in

            // Lift our own speed limit again once land is far enough away or we stopped closing in.
            if (limited && (d > Settings.F("AntiGroundingKm") * 1.5f || (rate <= 0f && now - limitedAt > 10f)))
            { Util.Safe("Nav.Unlimit", () => tcc.RemoveTimeCompressionBlockade(BlockKey)); limited = false; }

            if (!tcc.TimeCompression || gameTime.ArtificialTimeScale <= 1.5f || rate <= 0f) return;
            // LandDistance is in km. At high time compression the boat covers hundreds of km per real
            // minute, so the "seconds" rule alone fired far out at sea. Only warn when land is really near.
            if (d > Settings.F("AntiGroundingKm") || now < quietUntil) return;
            if (d / rate < Settings.F("AntiGroundingSeconds"))
            {
                rate = 0f;
                quietUntil = now + 30f;   // no repeat right away
                float cap = Settings.F("AntiGroundingMaxX");
                if (cap <= 1f) { tcc.TimeCompression = false; ToolkitRunner.ShowNotice("Land voraus! Zeitraffer gestoppt."); }
                else
                {
                    // Step down instead of off: the game's own limit mechanism (like near harbours), max x cap.
                    Util.Safe("Nav.Limit", () => tcc.SetTimeCompressionBlockade(BlockKey, cap));
                    limited = true; limitedAt = now;
                    ToolkitRunner.ShowNotice("Land voraus! Zeitraffer auf x" + cap.ToString("F0") + " begrenzt.");
                }
            }
        }

        public override void Tick()
        {
            float f = Mathf.Max(1f, Settings.F("CheatSpeed"));
            var ship = Util.PlayerShip;
            foreach (var p in Util.Find<Propeller>())
            {
                if (ship == null || !p.transform.IsChildOf(ship.transform)) continue;   // only OUR boat, never enemies
                float orig = Util.Original<float>(p, "maxPower");
                Util.SetField(p, "maxPower", orig * f * f);   // drag grows with speed², so force x f² ≈ speed x f
            }
            foreach (var e in Util.Find<PlayerShipEngine>())
            {
                Scale(e, "expectedVelocityPerGear", f);
                Scale(e, "expectedVelocityPerGearUnderwater", f);
            }
        }

        private static void Scale(object obj, string field, float f)
        {
            var orig = Util.Original<float[]>(obj, field);
            var cur = Traverse.Create(obj).Field(field).GetValue<float[]>();
            if (orig == null || cur == null) return;
            for (int i = 0; i < cur.Length && i < orig.Length; i++) cur[i] = orig[i] * f;
        }
    }
}

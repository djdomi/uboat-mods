using HarmonyLib;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // DoMe 2026-11-18: "vlt kann es ja detektieren, wenn er Schnorchel im Wasser ist, WENN ER AKTIV IST? aber NUR dann?"
    // Only while the snorkel of OUR boat is ordered up, has been up for 20 s, the boat lies at its ordered depth
    // (within 1 m) and the snorkel head is under water for 5 s: the ordered depth is raised by half a metre.
    // In every other case the depth is not touched. Never shallower than 2 m.
    public class SnorkelGuard : ModuleBase
    {
        private float upSince = -1f, wetSince = -1f, nextStep, nextLine;
        // DoMe 2026-11-18: "Schnorchel liefert immer +10 was benötigt wird". While the snorkel of OUR boat really works
        // (ordered up and performing), the air in the boat gains at least +x: whatever is used, plus x on top.
        // Never lowers anything; snorkel down or not working = our value is 0.
        private float airLogAt; private float lastAdd = -1f;
        private static float factorCached; private static float factorAt = -999f;
        private static float DisplayFactor()
        {
            if (factorCached > 0f && Time.unscaledTime - factorAt < 60f) return factorCached;
            factorAt = Time.unscaledTime; float f = 6000f;
            try
            {
                foreach (var g in Resources.FindObjectsOfTypeAll<UBOAT.Game.UI.Resources.ResourceGUI>())
                {
                    if (g == null) continue;
                    var t = Traverse.Create(g);
                    var res = t.Field("resource").GetValue() as UBOAT.Game.Core.Data.Resource;
                    if (res == null || res.Name != "Oxygen") continue;
                    float v = t.Field("tooltipGainMultiplier").GetValue<float>();
                    if (v > 0f) { f = v; break; }
                }
            }
            catch { }
            return factorCached = f;
        }
        private void Air(PlayableSubmarine sub, Snorkel sn)
        {
            var oxygen = sub.GetResource("Oxygen");
            if (oxygen == null) return;
            var m = Util.OneDelta(oxygen.Gain, "Toolkit Schnorchel");
            // 0.4.25: the setting is the number the game SHOWS for the air ("+10 per min"). The game's own value is that
            // number divided by the display factor of its air gauge (6000 unless the gauge says otherwise).
            float shown = Settings.F("SnorkelAirPlus"), factor = DisplayFactor();
            float plus = shown / factor, add = 0f;
            bool works = sn != null && sn.ShouldMastBeRaised && sn.Performance.Value > 0.05f;
            if (plus > 0f && works)
            {
                // the game may scale the sum (factors on the gain), so the needed value is solved from two samples
                float v0 = oxygen.Gain.GetValueWithModifierOverride(m, 0f), v1 = oxygen.Gain.GetValueWithModifierOverride(m, 1f);
                if (v0 < plus && v1 - v0 > 1e-6f) add = (plus - v0) / (v1 - v0);
            }
            if (Mathf.Abs(m.Value - add) > 1e-4f) m.Value = add;
            if ((add > 0f) != (lastAdd > 0f) || (add > 0f && Time.unscaledTime > airLogAt))
            {
                airLogAt = Time.unscaledTime + 60f;
                Util.Log(add > 0f ? "Schnorchel-Luft: Verbrauch gedeckt und +" + shown.ToString("0.#") + " dazu, wie im Spiel angezeigt (Anzeige-Faktor " + factor.ToString("0") + "; unser Zusatz angezeigt +" + (add * factor).ToString("0.#") + ", Luft-Bilanz jetzt angezeigt " + (oxygen.Gain.Value * factor).ToString("+0.#;-0.#;0") + " pro Minute, Luft im Boot " + (oxygen.Amount / Mathf.Max(0.0001f, (float)oxygen.Capacity) * 100f).ToString("0") + " %)"
                                  : "Schnorchel-Luft: kein Zusatz (Schnorchel " + (sn == null ? "nicht an Bord" : works ? "liefert selbst genug" : "arbeitet nicht") + ")");
            }
            lastAdd = add;
        }
        public override void Tick()
        {
            var sub = Util.PlayerShip as PlayableSubmarine;
            if (sub == null) return;
            Snorkel sn = null;
            foreach (var x in Util.Find<Snorkel>()) if (x != null && Util.IsPlayers(x)) { sn = x; break; }
            Util.Safe("Snorkel.Air", () => Air(sub, sn));
            if (!Settings.B("SnorkelKeepHead")) { upSince = wetSince = -1f; return; }
            if (sn == null || !sn.ShouldMastBeRaised || !sub.SubmergedOrGoingToSubmerge) { upSince = wetSince = -1f; return; }
            float now = Time.time;
            if (upSince < 0f) upSince = now;
            var tip = Traverse.Create(sn).Field("tip").GetValue<Transform>();
            if (tip == null) return;
            // water level from the game's own deck depth (same basis as its depth meter)
            float water = sub.transform.position.y - sub.VerticalOriginOffset + sub.DeckDepthUnclamped;
            float head = water - tip.position.y;                      // + = head under water
            bool atDepth = Mathf.Abs(sub.DeckDepthUnclamped - sub.TargetDepth) < 1f;
            if (Time.unscaledTime > nextLine)
            {
                nextLine = Time.unscaledTime + 60f;
                Util.Log("Schnorchel aktiv: Kopf " + (head > 0f ? head.ToString("0.0") + " m UNTER Wasser" : (-head).ToString("0.0") + " m über Wasser") + ", Deck " + sub.DeckDepthUnclamped.ToString("0.0") + " m, Zieltiefe " + sub.TargetDepth.ToString("0.0") + " m");
            }
            if (head <= 0.2f || !atDepth || now - upSince < 20f) { wetSince = -1f; return; }
            if (wetSince < 0f) wetSince = now;
            if (now - wetSince < 5f || now < nextStep) return;
            float old = sub.TargetDepth, want = Mathf.Max(2f, old - 0.5f);
            if (want >= old - 0.01f) return;
            nextStep = now + 5f;
            if (!Owners.Depth(Util.PlayerShip, "Schnorchel-Wächter", Owners.Safety, want, true, false)) return;
            Util.Log("Schnorchel: Kopf " + head.ToString("0.0") + " m unter Wasser, Zieltiefe " + old.ToString("0.0") + " -> " + want.ToString("0.0") + " m");
            ToolkitRunner.ShowNotice("Schnorchel-Kopf unter Wasser: Zieltiefe " + want.ToString("0.0") + " m", 4f);
        }
    }
}

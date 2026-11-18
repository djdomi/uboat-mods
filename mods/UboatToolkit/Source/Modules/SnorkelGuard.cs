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
        private void Air(PlayableSubmarine sub, Snorkel sn)
        {
            var oxygen = sub.GetResource("Oxygen");
            if (oxygen == null) return;
            var m = Util.OneDelta(oxygen.Gain, "Toolkit Schnorchel");
            float plus = Settings.F("SnorkelAirPlus"), add = 0f;
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
                Util.Log(add > 0f ? "Schnorchel-Luft: Verbrauch gedeckt und +" + plus.ToString("0.#") + " dazu (unser Zusatz " + add.ToString("0.##") + ", Luft-Bilanz jetzt " + oxygen.Gain.Value.ToString("0.##") + ")"
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
            sub.SetDepthPreset(DepthPreset.None, true);
            sub.SetTargetDepth(want, true, false);
            Util.Log("Schnorchel: Kopf " + head.ToString("0.0") + " m unter Wasser, Zieltiefe " + old.ToString("0.0") + " -> " + want.ToString("0.0") + " m");
            ToolkitRunner.ShowNotice("Schnorchel-Kopf unter Wasser: Zieltiefe " + want.ToString("0.0") + " m", 4f);
        }
    }
}

using UnityEngine;

namespace UboatToolkit.Modules
{
    // Automatic detail (setting ExplosionAuto): at low FPS Unity runs MORE physics steps per frame to catch up
    // (FixedUpdate of the contact handling, ships, rudders ...), which makes the next frame even slower.
    // Below half the target FPS the catch-up per frame is limited (max. 0.1 s game time per frame); above, the game value is back.
    public class AutoDetail : ModuleBase
    {
        private float original = -1f;
        private bool limited;

        public override void Frame()
        {
            if (original < 0f) original = Time.maximumDeltaTime;
            bool want = Settings.B("ExplosionAuto") && ExplosionLimit_Patch.Fps() < Settings.F("ExplosionTargetFps") * 0.5f;
            if (want == limited) return;
            limited = want;
            Time.maximumDeltaTime = want ? Mathf.Min(original, 0.1f) : original;
            Util.Log("Auto-Detail: Physik-Aufholen " + (want ? "begrenzt (0,1 s pro Bild)" : "normal (" + original.ToString("0.00") + " s)"));
        }
    }
}

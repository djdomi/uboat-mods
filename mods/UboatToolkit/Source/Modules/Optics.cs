using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // QoL #18 more zoom, #19 calm picture, #20 periscope extends higher (QoL up to 12 m, cheat up to 100 m).
    // Binoculars use the same game class as the periscopes, so they get zoom + calm picture too.
    public class Optics : ModuleBase
    {
        public override void Tick()
        {
            float zoom = Mathf.Max(1f, Settings.F("OpticsZoom"));
            float calm = Mathf.Clamp01(Settings.F("OpticsStabilization"));
            float height = Mathf.Max(Mathf.Min(Settings.F("PeriscopeHeight"), 12f), Settings.F("CheatPeriscopeHeight"));

            foreach (var p in Util.Find<Periscope>())
            {
                ApplyFov(p, "fovPresets", "fovSensitivities", zoom);
                ApplyFov(p, "fovPresetsRealistic", "fovSensitivitiesRealistic", zoom);
                ApplyCalm(p, "fovStabilization", calm);
                ApplyCalm(p, "fovStabilizationRealistic", calm);

                var length = Traverse.Create(p).Field("length").GetValue<Parameter>();
                if (length != null)
                {
                    float game = length.GetValueExcludingModifier(Util.ModifierName);
                    Util.SetParameter(length, height > game ? height : 0f);
                }
            }
        }

        // Only the strongest zoom step gets stronger; the wide view stays as it is.
        private static void ApplyFov(Periscope p, string fovField, string sensField, float zoom)
        {
            var orig = Util.Original<float[]>(p, fovField);
            var cur = Traverse.Create(p).Field(fovField).GetValue<float[]>();
            if (orig == null || cur == null || cur.Length != orig.Length || cur.Length == 0) return;
            int last = cur.Length - 1;
            cur[last] = orig[last] / zoom;

            var origSens = Util.Original<float[]>(p, sensField);
            var sens = Traverse.Create(p).Field(sensField).GetValue<float[]>();
            if (origSens != null && sens != null && sens.Length == origSens.Length && sens.Length > last)
                sens[last] = origSens[last] / zoom; // slower mouse when zoomed in further
        }

        private static void ApplyCalm(Periscope p, string field, float calm)
        {
            var orig = Util.Original<float[]>(p, field);
            var cur = Traverse.Create(p).Field(field).GetValue<float[]>();
            if (orig == null || cur == null || cur.Length != orig.Length) return;
            for (int i = 0; i < cur.Length; i++) cur[i] = Mathf.Lerp(orig[i], 1f, calm);
        }
    }
}

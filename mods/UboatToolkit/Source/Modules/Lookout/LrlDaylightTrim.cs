// Ausguck (Teil des UBOAT Toolkit): LrlDaylightTrim. Schalter: F10 > QoL > "Ausguck".
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlDaylightTrim
    {
        internal static float Weight(float visibility)
        {
            if (!LrlSettings.DaylightTrimEnabled)
                return 0f;
            if (float.IsNaN(visibility) || float.IsInfinity(visibility))
                return 0f;

            var start = LrlSettings.DaylightTrimStartVisibility;
            var full = LrlSettings.DaylightTrimFullVisibility;
            if (full <= start)
                return visibility >= full ? 1f : 0f;

            return Mathf.Clamp01((visibility - start) / (full - start));
        }

        internal static float RangeFactor(float visibility)
        {
            return Mathf.Lerp(1f, LrlSettings.DaylightRangeScale, Weight(visibility));
        }

        internal static float SmokeFactor(float visibility)
        {
            return Mathf.Lerp(1f, LrlSettings.DaylightSmokeScale, Weight(visibility));
        }
    }
}

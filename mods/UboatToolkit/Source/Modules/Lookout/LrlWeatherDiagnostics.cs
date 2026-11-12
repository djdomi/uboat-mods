// Ausguck (Teil des UBOAT Toolkit): LrlWeatherDiagnostics. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UBOAT.Game.Scene.Environment;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlWeatherDiagnostics
    {
        private static readonly StringBuilder LineBuilder = new StringBuilder();

        private static readonly FieldInfo Total = Field("totalVisibilityModifier");
        private static readonly FieldInfo TotalLit = Field("totalVisibilityModifierIfArtificiallyIlluminated");
        private static readonly FieldInfo Sun = Field("sunPositionVisibilityFactor");
        private static readonly FieldInfo Moon = Field("moonStateVisibilityFactor");
        private static readonly FieldInfo Cloud = Field("cloudinessVisibilityFactor");
        private static readonly FieldInfo Fog = Field("fogVisibilityFactor");
        private static readonly FieldInfo Rain = Field("rainVisibilityFactor");
        private static readonly FieldInfo Lightings = Field("lightingsVisibilityFactor");
        private static readonly FieldInfo Radar = Field("radarDetectabilityFactors");
        private static readonly FieldInfo Hydro = Field("passiveHydrophoneDetectabilityFactor");
        private static readonly FieldInfo MinFog = Field("minFogDensity");

        private static bool _warned;

        private static FieldInfo Field(string name)
        {
            return AccessTools.Field(typeof(Weather), name);
        }

        private static float Read(FieldInfo f, Weather w)
        {
            if (f == null || w == null)
                return float.NaN;

            var boxed = f.GetValue(w);
            return boxed is float ? (float)boxed : float.NaN;
        }

        internal static void Log(Weather weather)
        {
            if (weather == null)
                return;

            try
            {
                var total = Read(Total, weather);
                var sun = Read(Sun, weather);
                var moon = Read(Moon, weather);
                var cloud = Read(Cloud, weather);
                var fog = Read(Fog, weather);
                var rain = Read(Rain, weather);
                var lightings = Read(Lightings, weather);

                var sb = LineBuilder;
                sb.Length = 0;
                sb.Append("weather  visual=").Append(total.ToString("0.0000"));
                sb.Append(" [sun=").Append(sun.ToString("0.000"));
                sb.Append(" moon=").Append(moon.ToString("0.000"));
                sb.Append(" cloud=").Append(cloud.ToString("0.000"));
                sb.Append(" fog=").Append(fog.ToString("0.000"));
                sb.Append(" rain=").Append(rain.ToString("0.000"));
                sb.Append(" lightings=").Append(lightings.ToString("0.000")).Append(']');
                sb.Append("  lit=").Append(Read(TotalLit, weather).ToString("0.0000"));
                sb.Append("  radar=").Append(Read(Radar, weather).ToString("0.000"));
                sb.Append("  hydrophone=").Append(Read(Hydro, weather).ToString("0.000"));
                sb.Append("  minFogDensity=").Append(Read(MinFog, weather).ToString("0.00000"));
                LrlLog.Info(sb.ToString());

                var frc = fog * rain * cloud;
                var lightingsOverFrc = frc > 0f ? lightings / frc : float.NaN;

                sb.Length = 0;
                sb.Append("weather  fog*rain*cloud=").Append(frc.ToString("0.00000"));
                sb.Append("  lightings/frc=").Append(lightingsOverFrc.ToString("0.000"));
                sb.Append("  visual^2=").Append((total * total).ToString("0.00000"));
                sb.Append("  squaring costs x").Append((total > 0f ? 1f / total : float.NaN).ToString("0.00"));
                sb.Append("  (radius uses visual^2; hydrophone and radar use theirs once)");

                var dark = weather.IsDarkWeather;
                sb.Append("  darkness[isDark=").Append(dark ? "yes" : "no");
                sb.Append(" visual<0.4=").Append(total < 0.4f ? "yes" : "no");
                sb.Append(" -> newContactsSilent=")
                  .Append(dark && total < 0.4f
                      ? (LrlSettings.Enabled && LrlSettings.ReportDarkContacts ? "no (patched)" : "YES")
                      : "no")
                  .Append(']');

                LrlLog.Info(sb.ToString());
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("weather diagnostics failed", ex);
                }
            }
        }
    }
}

using HarmonyLib;
using UnityEngine;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Characters;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules
{
    // Cheats for the player's boat (single player). Every cheat is off by default.
    public class Cheats : ModuleBase
    {
        private PlayerCrew crew;
        private PlayerCareer career;

        private static readonly string[] SensorKeys = { "CheatRangeLookout", "CheatRangeHydro", "CheatRangeSonar", "CheatRangeRadar", "CheatRangeRadarWarn" };
        private static readonly string[] sensorNow = new string[5];
        private static readonly float[] sensorNear = new float[5];
        private static float nextSensorInfo, nextSensorLine;
        private static bool sensorHooked;
        private static string hydroParts;
        // every part that goes into a range value: base parts (+) and factors (x), by name
        private int capped;
        private static string Parts(Parameter p)
        {
            if (p == null) return "-";
            var sb = new System.Text.StringBuilder();
            try
            {
                foreach (var m in p.DeltaModifiers) if (m != null) sb.Append(" +").Append(m.Name).Append("=").Append(m.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                foreach (var m in p.ScaleModifiers) if (m != null) sb.Append(" x").Append(m.Name).Append("=").Append(m.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(" => ").Append(p.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (System.Exception e) { sb.Append(" (nicht lesbar: ").Append(e.Message).Append(")"); }
            return sb.ToString();
        }
        private static int SensorKind(UBOAT.Game.Scene.Items.BaseDetector d)
        {
            if (d is UBOAT.Game.Scene.Items.Hydrophone) return 1;
            if (d is UBOAT.Game.Scene.Items.Sonar) return 2;
            if (d is UBOAT.Game.Scene.Items.Radar) return 3;
            if (d is UBOAT.Game.Scene.Items.RadarDetector) return 4;
            return 0;
        }

        public override void Tick()
        {
            var ship = Util.PlayerShip;

            // 0.4.4: one range factor per sensor type, player only. Enemies always get factor 1 (game range).
            bool sensorLog = Time.unscaledTime >= nextSensorInfo;
            if (sensorLog) { nextSensorInfo = Time.unscaledTime + 1f; for (int i = 0; i < SensorKeys.Length; i++) sensorNow[i] = null; }
            foreach (var d in Util.Find<UBOAT.Game.Scene.Items.BaseDetector>())
            {
                bool mine = Util.IsPlayers(d);
                int kind = SensorKind(d);
                float m = mine ? Mathf.Max(0.05f, Settings.F(SensorKeys[kind])) : 1f;
                Util.ScaleParameter(d.DetectionRange, m);
                Util.ScaleParameter(d.GroupDetectionRange, m);
                // 0.4.19 (DoMe: "limiter, nicht größer als 1000 km"): our factor is cut back so that the range of the
                // player's sensor never exceeds the limit. Near range is in metres, far range in kilometres.
                if (mine && m > 1f)
                {
                    float cap = Mathf.Max(10f, Settings.F(kind == 0 ? "CheatRangeMaxKmLookout" : "CheatRangeMaxKm"));
                    Util.Safe("Sensor-Grenze", () =>
                    {
                        if (d.DetectionRange != null && d.DetectionRange.Value > cap * 1000f) { Util.ScaleParameter(d.DetectionRange, Mathf.Max(0.05f, m * cap * 1000f / d.DetectionRange.Value)); capped++; }
                        if (d.GroupDetectionRange != null && d.GroupDetectionRange.Value > cap) { Util.ScaleParameter(d.GroupDetectionRange, Mathf.Max(0.05f, m * cap / d.GroupDetectionRange.Value)); capped++; }
                    });
                }
                if (mine && sensorLog && d.DetectionRange != null)
                {
                    float near = d.DetectionRange.Value / 1000f, far = d.GroupDetectionRange != null ? d.GroupDetectionRange.Value : 0f;
                    // 0.4.9: the hydrophone values are multiplied by the boat's own listening factor before the game uses
                    // them (map circle: near x factor x 0.64, far x factor); without it the log showed "fern 13500 km".
                    if (kind == 1)
                    {
                        float hs = 1f;
                        try { hs = (float)ship.HydroacousticDetectorsRangeScale; } catch { }
                        near *= hs * 0.64f; far *= hs;
                        if (Time.unscaledTime >= nextSensorLine) hydroParts = "Horchgerät fern: Faktor Boot " + hs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + ", Teile " + Parts(d.GroupDetectionRange);
                    }
                    if (sensorNow[kind] == null || near > sensorNear[kind])
                    {
                        sensorNear[kind] = near;
                        sensorNow[kind] = "(jetzt nah " + near.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " km" + (far > 0f ? ", fern " + far.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " km" : "") + ")";
                    }
                }
            }
            if (sensorLog)
            {
                if (!sensorHooked)
                {
                    sensorHooked = true;
                    for (int i = 0; i < SensorKeys.Length; i++) { int k = i; var o = Settings.Get(SensorKeys[k]); if (o != null) o.Extra = () => sensorNow[k] ?? "(nicht an Bord)"; }
                }
                if (Time.unscaledTime >= nextSensorLine)
                {
                    nextSensorLine = Time.unscaledTime + 60f;
                    var sb = new System.Text.StringBuilder("Sensoren (nur eigenes Boot):");
                    for (int i = 0; i < SensorKeys.Length; i++) sb.Append(" ").Append(SensorKeys[i].Substring(10)).Append(" x").Append(Settings.F(SensorKeys[i]).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(" ").Append(sensorNow[i] ?? "(nicht an Bord)").Append(";");
                    sb.Append(". Grenze Sicht ").Append(Settings.F("CheatRangeMaxKmLookout").ToString("0")).Append(" km, übrige ").Append(Settings.F("CheatRangeMaxKm").ToString("0")).Append(" km, ").Append(capped).Append(" mal gekürzt, doppelte Faktoren entfernt: ").Append(Util.DuplicatesRemoved);
                    capped = 0;
                    Util.Log(sb.ToString());
                    if (hydroParts != null) { Util.Log(hydroParts); hydroParts = null; }
                }
            }

            // Undetectable: seen, heard (sonar + hydrophones) and radar-detected at almost zero range.
            float f = Settings.B("CheatUndetectable") ? 0.001f : 1f;
            var t = Traverse.Create(ship);
            foreach (var field in new[] { "visualDetectability", "sonarDetectability", "radarDetectorDetectability" })
                Util.ScaleParameter(t.Field(field).GetValue<Parameter>(), f);
            // 0.4.22 (option audit): "hydroacousticDetectorsRangeScale" is the range of OUR OWN listening gear. It was scaled
            // down with the cheat, so the own hydrophone and sonar went deaf. Always back to the game value now.
            Util.ScaleParameter(t.Field("hydroacousticDetectorsRangeScale").GetValue<Parameter>(), 1f);
            // Also the boat's own noise (dB, heard by hydrophones) and its radar size (cm): the game's own
            // dev-console "undetectable" sets exactly these to 0. Shown in the detection tooltip too.
            bool und = Settings.B("CheatUndetectable");
            Util.Safe("Undetectable.Noise", () =>
            {
                Util.OneScale(ship.HydrophoneDetectability, Util.ModifierName).Value = und ? 0f : 1f;
                Util.OneScale(ship.SandboxEntity.SignatureRadius, Util.ModifierName).Value = und ? 0f : 1f;
            });

            Util.ScaleParameter(t.Field("hullCrackThreshold").GetValue<Parameter>(), Settings.B("CheatDeepDive") ? 100f : 1f);
            // The game also has a separate "hull crush" timer (HullCrushController): below the boat's CrushDepth
            // it counts down and then sets the hull to 0 = boom, even without any leaks. Push that depth far away.
            Util.Safe("DeepDive.Crush", () =>
                Util.ScaleParameter(ship.SandboxEntity.Parameters.GetParameter("CrushDepth"), Settings.B("CheatDeepDive") ? 100f : 1f));

            if (Settings.B("CheatFullFuel")) Util.Safe("FullFuel", () => FillFuel(ship));
            if (Settings.B("CheatFullBattery")) Fill(ship.Energy);
            if (Settings.B("CheatFullAir")) Fill(ship.Oxygen);
            if (Settings.B("CheatFullDiscipline"))
            {
                Fill(ship.Discipline);
            }
            Util.Safe("Moral", () => NoMoraleLoss(ship));
            if (Settings.B("NoStress") && ship.Stress != null) ship.Stress.Amount = 0;

            if (Settings.B("CheatCrewNeverTired"))
            {
                if (crew == null) crew = Object.FindObjectOfType<PlayerCrew>();
                if (crew != null && crew.Characters != null)
                    foreach (var c in crew.Characters) if (c != null) c.Energy = 1f;
            }

            float money = Settings.F("CheatMoney"), rep = Settings.F("CheatReputation");
            if (money > 0f || rep > 0f)
            {
                if (career == null) career = Util.Career;
                if (career != null)
                {
                    if (money > 0f && career.Budget < money) Util.SetField(career, "budget", Mathf.RoundToInt(money));
                    if (rep > 0f && career.ReputationPoints < rep) career.ReputationPoints = Mathf.RoundToInt(rep);
                }
            }
        }

        // The diesel % is computed by the game from the fuel lying in the tanks, so filling only the
        // number would be undone at once. Fill the tanks themselves.
        // Morale (game: "Discipline") with the cheat on: refilling alone left the display at e.g. "-102" (a big crew drains it
        // fast). Our own delta cancels exactly the negative part of the gain, so it shows 0 and never drops; gains stay.
        private const string MoraleMod = "UboatToolkit Moral";
        private static void NoMoraleLoss(PlayerShip ship)
        {
            var g = ship.Discipline != null ? ship.Discipline.Gain : null;
            if (g == null) return;
            var m = Util.OneDelta(g, MoraleMod);
            float want = 0f;
            if (Settings.B("CheatFullDiscipline"))
            {
                float v0 = g.GetValueWithModifierOverride(m, 0f), v1 = g.GetValueWithModifierOverride(m, 1f);
                if (v0 < 0f && v1 - v0 > 1e-6f) want = -v0 / (v1 - v0);
            }
            if (Mathf.Abs(m.Value - want) > 1e-6f) m.Value = want;
        }

        private static void FillFuel(UBOAT.Game.Scene.Entities.PlayerShip ship)
        {
            var srcs = ship.FuelSources;
            if (srcs == null) return;
            foreach (var f in srcs)
            {
                if (f == null || f.Storage == null || f.Data == null || f.Data.Mass <= 0f) continue;
                double full = f.Storage.ItemsMassLimit / f.Data.Mass;
                if (full > 0 && f.DoubleCount < full) f.DoubleCount = full;
            }
        }

        private static void Fill(Resource r)
        {
            if (r == null || r.Capacity == null) return;
            double cap = r.Capacity.Value;
            if (cap > 0 && r.Amount < cap) r.Amount = cap;
        }
    }

    // Hull health of the player's boat never goes down.
    [HarmonyPatch(typeof(Entity), nameof(Entity.Health), MethodType.Setter)]
    internal static class Cheats_HullPatch
    {
        private static void Prefix(Entity __instance, ref float value)
        {
            if (Settings.B("CheatNoHullDamage") && __instance is PlayerShip && value < __instance.Health) value = __instance.Health;
        }
    }

    // No water pressure damage at great depth.
    [HarmonyPatch(typeof(UBOAT.Game.Scene.Tasks.ApplyWaterDamageToPlayerShip), "DoDamageTick")]
    internal static class Cheats_DeepDivePatch
    {
        private static bool Prefix(ref float __result)
        {
            if (!Settings.B("CheatDeepDive")) return true;
            __result = 5f; // check again in 5 seconds
            return false;
        }
    }
}

namespace UboatToolkit.Modules
{
    // "Crew never tired": also the game's own tired check (Fatigue skill, updated every second per man) always says rested,
    // and the man's energy is refilled right there (Tick alone missed men between ticks / new men).
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.Skills.Fatigue), "UpdateFatigue")]
    internal static class Cheats_NeverTiredPatch
    {
        private static void Prefix(UBOAT.Game.Scene.Characters.Skills.Fatigue __instance)
        {
            if (!Settings.B("CheatCrewNeverTired")) return;
            var c = __instance.Character;
            if (c != null) c.Energy = 1f;
        }
    }

    // Energy never goes down at all (DoMe: prevent the loss, not refill it): every write to Energy becomes "full".
    [HarmonyLib.HarmonyPatch(typeof(UBOAT.Game.Scene.Characters.PlayableCharacter), "Energy", HarmonyLib.MethodType.Setter)]
    internal static class Cheats_NoEnergyLossPatch
    {
        private static void Prefix(ref float value)
        {
            if (Settings.B("CheatCrewNeverTired")) value = 1f;
        }
    }
}

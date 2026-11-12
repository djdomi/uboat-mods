// Ausguck (Teil des UBOAT Toolkit): LrlContactNoticeAudit. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    [HarmonyPatch]
    internal static class LrlContactNoticeAudit
    {
        private const int IndirectObservation = 2;   // DynamicContactType
        private const int FunnelSmoke = 9;           // PerceivedDynamicContactType
        private const int FunnelSmokeSingle = 19;
        private const int ShipMasts = 10;
        private const int ShipMastSingle = 20;
        private const int MovementInTheDark = 21;

        private static readonly MethodInfo MakesVisibleSmoke =
            AccessTools.Method(typeof(CompoundContactInfo), "MakesVisibleSmoke");

        private static bool _warned;

        private const float RepeatWindow = 2f;
        private static string _lastGroupName;
        private static float _lastLoggedAt = -100f;
        private static int _folded;

        private static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("UBOAT.Game.Scene.Entities.DynamicContactsController");
            return t != null ? AccessTools.Method(t, "AddContact") : null;
        }

        private static void Postfix(CompoundContactInfo contact)
        {
            try
            {
                if (!LrlSettings.Enabled || !LrlSettings.DebugLogging || contact == null)
                    return;
                if ((int)contact.Type != IndirectObservation)
                    return;

                var perceived = (int)contact.PerceivedType;
                var airborne = contact.IsAirborne;

                var smoke = "?";
                if (MakesVisibleSmoke != null)
                {
                    var r = MakesVisibleSmoke.Invoke(contact, null);
                    if (r is bool)
                        smoke = (bool)r ? "yes" : "NO";
                }

                var weather = LrlPlayer.Weather;
                var dark = weather != null && weather.IsDarkWeather;
                var vis = weather != null ? weather.VisualDetectabilityFactors : float.NaN;

                var infos = contact.EntityInfos;
                var ships = infos != null ? infos.Count : 0;
                var groupName = "?";
                SandboxGroup group = null;
                if (ships > 0 && infos[0] != null && infos[0].Entity != null && infos[0].Entity.Group != null)
                {
                    group = infos[0].Entity.Group;
                    groupName = group.name;
                }

                var now = Time.unscaledTime;
                if (groupName == _lastGroupName && now - _lastLoggedAt < RepeatWindow)
                {
                    _folded++;
                    return;
                }
                _lastGroupName = groupName;
                _lastLoggedAt = now;

                var where = "dist=? bearing=?";
                var wolfpack = LrlRuntime.CurrentWolfpack;
                if (group != null && wolfpack != null)
                {
                    var delta = group.Position - wolfpack.Position;
                    var b = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                    if (b < 0f)
                        b += 360f;
                    where = string.Format("dist={0:0.0}km bearing={1:000}", delta.magnitude, b);
                }

                var folded = "";
                if (_folded > 0)
                {
                    folded = string.Format("  [{0} repeat add(s) of the previous contact folded]", _folded);
                    _folded = 0;
                }

                var accepted = !airborne && (perceived == FunnelSmoke || perceived == FunnelSmokeSingle);

                string why;
                if (accepted) why = "smoke notice SHOWN";
                else if (airborne) why = "no notice - airborne";
                else if (perceived == MovementInTheDark) why = "no notice - map type is MovementInTheDark (dark weather and visibility < 0.4 at the moment it was added)";
                else if (perceived == ShipMasts || perceived == ShipMastSingle) why = "no notice - map type is ShipMasts (MakesVisibleSmoke=" + smoke + ")";
                else why = "no notice - map type " + perceived;

                LrlLog.Info(string.Format(
                    "map contact added: {0} ({1} ship(s)) {2} perceived={3} dark={4} visibility={5:0.00} makesVisibleSmoke={6} -> {7}{8}",
                    groupName, ships, where, perceived, dark ? "yes" : "no", vis, smoke, why, folded));
            }
            catch (Exception ex)
            {
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("map contact audit failed - disabled for this session", ex);
                }
            }
        }
    }
}

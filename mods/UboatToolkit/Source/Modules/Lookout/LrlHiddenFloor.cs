// Ausguck (Teil des UBOAT Toolkit): LrlHiddenFloor. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlHiddenFloor
    {
        private const string HiddenModifierName = "Hidden";

        private static readonly List<ObservationSpot> Buffer = new List<ObservationSpot>();

        private static bool _warned;

        internal static float LastRawKm { get; private set; }
        internal static float LastFlooredKm { get; private set; }

        internal static bool Active
        {
            get { return LrlSettings.Enabled && LrlSettings.FloorHiddenForGroupDetection && LrlSettings.HiddenFloor > 0f; }
        }

        private static int _cachedFrame = -1;
        private static PlayerShip _cachedShip;
        private static float _cachedKm;

        internal static float MaxGroupRangeKm(PlayerShip ship)
        {
            if (ship == null)
                return 0f;

            if (_cachedFrame == Time.frameCount && ReferenceEquals(_cachedShip, ship))
                return _cachedKm;

            var floor = Active ? Mathf.Clamp01(LrlSettings.HiddenFloor) : 0f;

            try
            {
                Buffer.Clear();
                LrlProviders.GetAll(ship, Buffer);

                var raw = 0f;
                var floored = 0f;

                for (var i = 0; i < Buffer.Count; i++)
                {
                    var spot = Buffer[i];
                    if (spot == null)
                        continue;

                    var p = spot.GroupDetectionRange;
                    if (p == null)
                        continue;

                    var value = p.Value;
                    if (value > raw)
                        raw = value;

                    var lifted = value;

                    if (floor > 0f)
                    {
                        var hidden = p.GetScaleModifier(HiddenModifierName, false);
                        if (hidden != null)
                        {
                            var h = hidden.Value;
                            if (h <= 0.0001f)
                                continue;          // optic fully under: stays blind
                            if (h < floor)
                                lifted = p.GetValueWithModifierOverride(hidden, floor);
                        }
                    }

                    if (lifted > floored)
                        floored = lifted;
                }

                Buffer.Clear();

                LastRawKm = raw;
                LastFlooredKm = floored;

                _cachedShip = ship;
                _cachedFrame = Time.frameCount;
                _cachedKm = floored;
                return floored;
            }
            catch (Exception ex)
            {
                Buffer.Clear();
                if (!_warned)
                {
                    _warned = true;
                    LrlLog.Error("Hidden floor failed - falling back to vanilla provider ranges", ex);
                }

                _cachedShip = ship;
                _cachedFrame = Time.frameCount;
                _cachedKm = 0f;
                return 0f;
            }
        }
    }
}

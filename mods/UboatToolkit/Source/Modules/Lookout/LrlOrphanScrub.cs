// Ausguck (Teil des UBOAT Toolkit): LrlOrphanScrub. Schalter: F10 > QoL > "Ausguck".
using System;
using System.Collections.Generic;
using UBOAT.Game.Core.Data;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;
using UnityEngine;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlOrphanScrub
    {
        private const string LegacyModifierName = "LongRangeLookout.GroupSightRange";

        private static readonly List<ObservationSpot> SpotBuffer = new List<ObservationSpot>();
        private static readonly List<Modifier> Doomed = new List<Modifier>();

        internal static int RemovedCount { get; private set; }

        internal static void Sweep(PlayerShip ship)
        {
            if (ship == null)
                return;

            LrlProviders.GetAll(ship, SpotBuffer);

            for (var i = 0; i < SpotBuffer.Count; i++)
            {
                var spot = SpotBuffer[i];

                SweepParameter(spot, spot.GroupDetectionRange);
            }

            SpotBuffer.Clear();
        }

        private static void SweepParameter(ObservationSpot spot, Parameter parameter)
        {
            if (parameter == null)
                return;

            var scales = parameter.ScaleModifiers;
            if (scales == null || scales.Count == 0)
                return;

            Doomed.Clear();

            for (var i = 0; i < scales.Count; i++)
            {
                if (IsOurs(scales[i]))
                    Doomed.Add(scales[i]);
            }

            for (var i = 0; i < Doomed.Count; i++)
            {
                var modifier = Doomed[i];

                LrlLog.Info(string.Format(
                    "scrub: removing legacy modifier from {0}.GroupSightRange - name=\"{1}\" value={2:0.000}",
                    spot.Name, modifier.Name, modifier.Value));

                modifier.Value = 1f;
                scales.Remove(modifier);

                RemovedCount++;
            }

            Doomed.Clear();
        }

        private static bool IsOurs(Modifier modifier)
        {
            if (modifier == null)
                return false;

            if (LegacyModifierName.Equals(modifier.Name))
                return true;

            if (modifier.GetType() != typeof(Modifier))
                return false;

            return Mathf.Abs(modifier.Value - LrlSettings.LegacyMultiplier) < 0.0005f;
        }
    }
}

// Ausguck (Teil des UBOAT Toolkit): LrlProviders. Schalter: F10 > QoL > "Ausguck".
using System.Collections.Generic;
using UBOAT.Game.Scene.Entities;
using UBOAT.Game.Scene.Items;

namespace UboatToolkit.Modules.Lrl
{
    internal static class LrlProviders
    {
        internal static void GetAll(PlayerShip ship, List<ObservationSpot> results)
        {
            results.Clear();

            if (ship == null)
                return;

            var equipment = ship.Equipment;
            if (equipment == null)
                return;

            for (var i = 0; i < equipment.Count; i++)
            {
                var spot = equipment[i] as ObservationSpot;
                if (spot != null)
                    results.Add(spot);
            }
        }
    }
}

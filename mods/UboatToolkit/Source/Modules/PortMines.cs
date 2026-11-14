using System.Linq;
using UBOAT.Game.Sandbox;
using UBOAT.Game.Scene.Entities;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Key: in port, put mines into the first torpedo slot of the shop right now (no waiting for restock).
    public class PortMines : ModuleBase
    {
        public override void Frame()
        {
            if (!Keys.Down("KeyPortMines")) return;
            Util.Log("Minen-Taste gedrückt");   // 0.3.47: every branch logs (DoMe: key had no effect)
            var ship = Util.PlayerShip;
            var se = ship?.CurrentDock as SandboxEntity;
            if (se == null || se.Country == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: nur im Hafen möglich", 4f); Util.Log("Minen: kein Hafen (Docked=" + (ship != null && ship.Docked) + ", CurrentDock=" + (ship?.CurrentDock != null ? ship.CurrentDock.GetType().Name : "null") + ")"); return; }
            // 0.3.48: only a real port (DoMe: mines ended up in his own tubes)
            if (se.Blueprint?.Type == null || se.Blueprint.Type.Category != EntityTypeCategory.Port)
            {
                UboatToolkit.ToolkitRunner.ShowNotice("Minen: das ist kein Hafen-Laden", 4f);
                Util.Log("Minen: CurrentDock ist kein Hafen (" + se.Name + ", " + se.Blueprint?.Type?.Category + ")");
                return;
            }
            var eqs = se.Equipment.Where(e => e?.Slot != null && e.Slot.EquipmentTag == "Torpedo").ToList();
            var first = eqs.FirstOrDefault();
            if (first == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: Hafen hat keine Torpedo-Plätze", 4f); Util.Log("Minen: " + se.Name + " ohne Torpedo-Platz, Plätze: " + string.Join(", ", se.Equipment.Where(e => e?.Slot != null).Select(e => e.Slot.EquipmentTag).Distinct())); return; }
            var pool = se.Country.GetReservesByTagID(first.Slot.EquipmentTagID);
            var mine = pool?.FirstOrDefault(x => x?.ItemData != null && Controls_TorpedoMixPatch.IsMine(x.ItemData.Name));
            if (mine == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: dieses Land hat keine Minen", 4f); if (pool != null) Util.Log("Minen: keine im Vorrat, Typen: " + string.Join(", ", pool.Select(x => x?.ItemData?.Name))); return; }
            se.SetEquipment(first.Slot, mine.ItemData, true, true);
            UboatToolkit.ToolkitRunner.ShowNotice("Minen im Hafen-Laden: " + mine.ItemData.Name, 5f);
            Util.Log("Minen in Hafen gelegt: " + mine.ItemData.Name);
        }
    }
}

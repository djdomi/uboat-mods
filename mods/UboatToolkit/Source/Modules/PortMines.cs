using System.Linq;
using UBOAT.Game.Sandbox;
using UnityEngine;

namespace UboatToolkit.Modules
{
    // Key: in port, put mines into the first torpedo slot of the shop right now (no waiting for restock).
    public class PortMines : ModuleBase
    {
        public override void Frame()
        {
            if (!Keys.Down("KeyPortMines")) return;
            var ship = Util.PlayerShip;
            var se = ship?.CurrentDock as SandboxEntity;
            if (se == null || se.Country == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: nur im Hafen möglich", 4f); return; }
            var eqs = se.Equipment.Where(e => e?.Slot != null && e.Slot.EquipmentTag == "Torpedo").ToList();
            var first = eqs.FirstOrDefault();
            if (first == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: Hafen hat keine Torpedo-Plätze", 4f); return; }
            var pool = se.Country.GetReservesByTagID(first.Slot.EquipmentTagID);
            var mine = pool?.FirstOrDefault(x => x?.ItemData != null && Controls_TorpedoMixPatch.IsMine(x.ItemData.Name));
            if (mine == null) { UboatToolkit.ToolkitRunner.ShowNotice("Minen: dieses Land hat keine Minen", 4f); if (pool != null) Util.Log("Minen: keine im Vorrat, Typen: " + string.Join(", ", pool.Select(x => x?.ItemData?.Name))); return; }
            se.SetEquipment(first.Slot, mine.ItemData, true, true);
            UboatToolkit.ToolkitRunner.ShowNotice("Minen im Hafen-Laden: " + mine.ItemData.Name, 5f);
            Util.Log("Minen in Hafen gelegt: " + mine.ItemData.Name);
        }
    }
}
